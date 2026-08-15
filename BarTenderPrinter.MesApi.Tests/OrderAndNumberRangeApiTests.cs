using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BarTenderPrinter.MesApi;
using Xunit;

namespace BarTenderPrinter.MesApi.Tests;

public sealed class OrderAndNumberRangeApiTests
{
    [PostgresFact]
    public async Task ProtectedEndpointRequiresAuthentication()
    {
        await MesApiFactory.MigrateAsync();
        await using var factory = new MesApiFactory();
        var response = await factory.CreateClient().GetAsync("/api/orders/missing");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("UNAUTHORIZED", error?.Code);
        Assert.False(string.IsNullOrWhiteSpace(error?.CorrelationId));
    }

    [PostgresFact]
    public async Task OrderCreationRequiresPlannerRole()
    {
        await MesApiFactory.MigrateAsync();
        await using var factory = new MesApiFactory();
        using var client = CreateClient(factory, MesApiFactory.ViewerToken);
        var response = await client.PostAsJsonAsync("/api/orders", ValidOrder());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("FORBIDDEN", (await response.Content.ReadFromJsonAsync<ApiError>())?.Code);
    }

    [PostgresFact]
    public async Task InvalidOrderReturnsStableValidationError()
    {
        await MesApiFactory.MigrateAsync();
        await using var factory = new MesApiFactory();
        using var client = CreateClient(factory, MesApiFactory.PlannerToken);
        var response = await client.PostAsJsonAsync("/api/orders", ValidOrder() with { PlannedQuantity = 0 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_FAILED", (await response.Content.ReadFromJsonAsync<ApiError>())?.Code);
    }

    [PostgresFact]
    public async Task OversizedOrderFieldReturnsStableValidationError()
    {
        await MesApiFactory.MigrateAsync();
        await using var factory = new MesApiFactory();
        using var client = CreateClient(factory, MesApiFactory.PlannerToken);
        var response = await client.PostAsJsonAsync("/api/orders", ValidOrder() with { Customer = new string('A', 129) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_FAILED", (await response.Content.ReadFromJsonAsync<ApiError>())?.Code);
    }

    [PostgresFact]
    public async Task UnknownEnumReturnsStableValidationError()
    {
        await MesApiFactory.MigrateAsync();
        await using var factory = new MesApiFactory();
        using var client = CreateClient(factory, MesApiFactory.PlannerToken);
        var response = await client.PostAsJsonAsync("/api/number-ranges", new
        {
            orderId = "order-1",
            numberType = "Unknown",
            prefix = "SN",
            datePattern = "None",
            start = 1,
            end = 10,
            step = 1,
            numericWidth = 4,
            validationPattern = ""
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_FAILED", (await response.Content.ReadFromJsonAsync<ApiError>())?.Code);
    }

    [PostgresFact]
    public async Task CreatesAndQueriesOrderAndNumberRange()
    {
        await MesApiFactory.MigrateAsync();
        await using var factory = new MesApiFactory();
        using var client = CreateClient(factory, MesApiFactory.PlannerToken);
        var orderResponse = await client.PostAsJsonAsync("/api/orders", ValidOrder());
        Assert.Equal(HttpStatusCode.Created, orderResponse.StatusCode);
        var order = await ReadObjectAsync(orderResponse);
        var orderId = order.GetProperty("id").GetString()!;

        var rangeResponse = await client.PostAsJsonAsync("/api/number-ranges", new
        {
            orderId,
            numberType = "SerialNumber",
            prefix = "SN",
            datePattern = "None",
            start = 1,
            end = 10,
            step = 1,
            numericWidth = 4,
            validationPattern = ""
        });
        Assert.Equal(HttpStatusCode.Created, rangeResponse.StatusCode);
        var range = await ReadObjectAsync(rangeResponse);
        var rangeId = range.GetProperty("id").GetString()!;

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/orders/{orderId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/number-ranges/{rangeId}")).StatusCode);
    }

    [PostgresFact]
    public async Task NumberAllocationReplaysAndRejectsKeyReuseForAnotherRange()
    {
        await MesApiFactory.MigrateAsync();
        await using var factory = new MesApiFactory();
        using var planner = CreateClient(factory, MesApiFactory.PlannerToken);
        var order = await ReadObjectAsync(await planner.PostAsJsonAsync("/api/orders", ValidOrder()));
        var orderId = order.GetProperty("id").GetString()!;
        var firstRange = await CreateRangeAsync(planner, orderId);
        var secondRange = await CreateRangeAsync(planner, orderId);
        using var station = CreateClient(factory, MesApiFactory.OperatorToken);
        var key = $"allocation-{Guid.NewGuid():N}";

        var first = await station.PostAsJsonAsync($"/api/number-ranges/{firstRange}/allocations", new { idempotencyKey = key });
        var replay = await station.PostAsJsonAsync($"/api/number-ranges/{firstRange}/allocations", new { idempotencyKey = key });
        var conflict = await station.PostAsJsonAsync($"/api/number-ranges/{secondRange}/allocations", new { idempotencyKey = key });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.True((await ReadObjectAsync(replay)).GetProperty("isReplay").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("IDEMPOTENCY_CONFLICT", (await conflict.Content.ReadFromJsonAsync<ApiError>())?.Code);
    }

    [PostgresFact]
    public async Task ConcurrentNumberAllocationsReturnDistinctValues()
    {
        await MesApiFactory.MigrateAsync();
        await using var factory = new MesApiFactory();
        using var planner = CreateClient(factory, MesApiFactory.PlannerToken);
        var order = await ReadObjectAsync(await planner.PostAsJsonAsync("/api/orders", ValidOrder()));
        var rangeId = await CreateRangeAsync(planner, order.GetProperty("id").GetString()!);
        using var station = CreateClient(factory, MesApiFactory.OperatorToken);

        var requests = Enumerable.Range(0, 8)
            .Select(_ => station.PostAsJsonAsync($"/api/number-ranges/{rangeId}/allocations",
                new { idempotencyKey = $"allocation-{Guid.NewGuid():N}" }))
            .ToArray();
        var responses = await Task.WhenAll(requests);

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var results = await Task.WhenAll(responses.Select(ReadObjectAsync));
        Assert.Equal(results.Length, results.Select(result => result.GetProperty("value").GetString()).Distinct().Count());
    }

    [PostgresFact]
    public async Task MalformedRequestDoesNotEchoSensitiveInput()
    {
        await MesApiFactory.MigrateAsync();
        await using var factory = new MesApiFactory();
        using var client = CreateClient(factory, MesApiFactory.PlannerToken);
        const string sensitiveValue = "861234567890123";
        using var content = new StringContent($"{{\"orderNumber\":\"{sensitiveValue}\"", Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync("/api/orders", content);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(sensitiveValue, body);
        Assert.Contains("VALIDATION_FAILED", body);
    }

    private static HttpClient CreateClient(MesApiFactory factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static CreateProductionOrderRequest ValidOrder() => new(
        $"ORDER-{Guid.NewGuid():N}", "Customer", "M1", "BLACK", 100, null, null);

    private static async Task<string> CreateRangeAsync(HttpClient client, string orderId)
    {
        var response = await client.PostAsJsonAsync("/api/number-ranges", new
        {
            orderId,
            numberType = "SerialNumber",
            prefix = $"SN{Guid.NewGuid():N}"[..10],
            datePattern = "None",
            start = 1,
            end = 10,
            step = 1,
            numericWidth = 4,
            validationPattern = ""
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadObjectAsync(response)).GetProperty("id").GetString()!;
    }

    private static async Task<JsonElement> ReadObjectAsync(HttpResponseMessage response)
    {
        var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return document.RootElement.Clone();
    }
}
