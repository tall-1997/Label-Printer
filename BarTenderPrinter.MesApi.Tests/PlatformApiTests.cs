using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace BarTenderPrinter.MesApi.Tests;

public sealed class PlatformApiTests
{
    [PostgresFact]
    public async Task SessionRequiresAuthentication()
    {
        await MesApiFactory.MigrateAsync();
        await using var factory = new MesApiFactory();

        var response = await factory.CreateClient().GetAsync("/api/v1/session");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [PostgresFact]
    public async Task SessionReturnsStationContextAndCapabilities()
    {
        await MesApiFactory.MigrateAsync();
        await using var factory = new MesApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MesApiFactory.OperatorToken);

        var response = await client.GetAsync("/api/v1/session");
        var session = await response.Content.ReadFromJsonAsync<PlatformSessionResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(session);
        Assert.Equal("operator-1", session.UserId);
        Assert.Contains("ProductionOperator", session.Roles);
        Assert.Contains("workspace.use", session.Capabilities);
        Assert.Contains("traceability.view", session.Capabilities);
    }

    [PostgresFact]
    public async Task V1BusinessEndpointsRequireAuthentication()
    {
        await MesApiFactory.MigrateAsync();
        await using var factory = new MesApiFactory();

        var response = await factory.CreateClient().GetAsync("/api/v1/orders/missing");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [PostgresFact]
    public async Task V1BusinessEndpointsPreserveAuthorizationPolicies()
    {
        await MesApiFactory.MigrateAsync();
        await using var factory = new MesApiFactory();
        using var client = CreateClient(factory, MesApiFactory.ViewerToken);

        var response = await client.PostAsJsonAsync("/api/v1/orders", ValidOrder());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [PostgresFact]
    public async Task V1AndLegacyEndpointsShareOrderBehavior()
    {
        await MesApiFactory.MigrateAsync();
        await using var factory = new MesApiFactory();
        using var client = CreateClient(factory, MesApiFactory.PlannerToken);

        var createResponse = await client.PostAsJsonAsync("/api/v1/orders", ValidOrder());
        var order = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var orderId = order.GetProperty("id").GetString();

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.Equal($"/api/v1/orders/{orderId}", createResponse.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/orders/{orderId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/orders/{orderId}")).StatusCode);
    }

    private static HttpClient CreateClient(MesApiFactory factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static object ValidOrder() => new
    {
        orderNumber = $"V1-{Guid.NewGuid():N}",
        customer = "V1 customer",
        productModel = "V1 model",
        color = "Black",
        plannedQuantity = 10,
        validFromUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
        validToUtc = DateTimeOffset.UtcNow.AddDays(1)
    };

    private sealed record PlatformSessionResponse(
        string UserId,
        string DisplayName,
        string StationId,
        string ShiftId,
        string[] Roles,
        string[] Capabilities);
}
