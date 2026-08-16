using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using BarTenderPrinter.MesApi;
using Xunit;

namespace BarTenderPrinter.MesApi.Tests;

public sealed class ExtendedCapabilityApiTests
{
    [PostgresFact]
    public async Task HighRiskEndpointsRejectOperatorRole()
    {
        await MesApiFactory.MigrateAsync();
        await using var factory = new MesApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MesApiFactory.OperatorToken);

        var disposition = await client.PostAsJsonAsync($"/api/inspection-lots/{Guid.NewGuid():N}/disposition",
            new ApplyDispositionRequest(BarTenderPrinter.Domain.Quality.DispositionDecision.Release, "PASS", "key-1"));
        var confirm = await client.PostAsJsonAsync($"/api/shipments/{Guid.NewGuid():N}/confirm",
            new ConfirmShipmentRequest("key-2"));
        var archive = await client.PostAsJsonAsync($"/api/orders/{Guid.NewGuid():N}/archive",
            new ArchiveOrderRequest("key-3"));

        Assert.Equal(HttpStatusCode.Forbidden, disposition.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, confirm.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, archive.StatusCode);
    }

    [PostgresFact]
    public async Task HighRiskRolePassesAuthorizationAndReceivesBusinessErrorContract()
    {
        await MesApiFactory.MigrateAsync();
        await using var factory = new MesApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MesApiFactory.HighRiskToken);

        var response = await client.PostAsJsonAsync($"/api/orders/{Guid.NewGuid():N}/archive",
            new ArchiveOrderRequest("archive-key"));
        var error = await response.Content.ReadFromJsonAsync<ApiError>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("NOT_FOUND", error?.Code);
        Assert.False(string.IsNullOrWhiteSpace(error?.CorrelationId));
    }

    [PostgresFact]
    public async Task ReworkRolesSeparateCreationApprovalAndExecution()
    {
        await MesApiFactory.MigrateAsync();
        await using var factory = new MesApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MesApiFactory.QualityEngineerToken);

        var create = await client.PostAsJsonAsync("/api/rework-orders",
            new CreateReworkOrderRequest("unit-1", "route-1", "REPAIR", "OP-1", 1));
        var activate = await client.PostAsJsonAsync("/api/rework-orders/rework-1/activate",
            new ReworkCommandRequest("activate-key"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MesApiFactory.HighRiskToken);
        var managerCreate = await client.PostAsJsonAsync("/api/rework-orders",
            new CreateReworkOrderRequest("unit-1", "route-1", "REPAIR", "OP-1", 1));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", MesApiFactory.ReworkExecutorToken);
        var executorApprove = await client.PostAsJsonAsync("/api/rework-orders/rework-1/approve",
            new ReworkCommandRequest("approve-key"));

        Assert.NotEqual(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, activate.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, managerCreate.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, executorApprove.StatusCode);
    }

    [PostgresFact]
    public async Task CsvImportRequiresIdempotencyHeaderAndReturnsRowErrors()
    {
        await MesApiFactory.MigrateAsync();
        await using var factory = new MesApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MesApiFactory.PlannerToken);
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"csv-{Guid.NewGuid():N}");
        var orderNumber = $"API-BAD-{Guid.NewGuid():N}";
        using var content = new StringContent($"""
            orderNumber,customer,productModel,color,plannedQuantity,validFromUtc,validToUtc
            {orderNumber},Customer,M1,BLACK,0,,
            """, Encoding.UTF8, "text/csv");

        var response = await client.PostAsync("/api/csv-imports/orders", content);
        var batch = await response.Content.ReadFromJsonAsync<BarTenderPrinter.Persistence.CsvImportBatchSnapshot>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(batch);
        Assert.Equal("Invalid", batch.Status);
        Assert.Single(batch.Errors);
    }

    [PostgresFact]
    public async Task CsvImportRejectsContentLengthAboveTenMegabytesBeforeParsing()
    {
        await MesApiFactory.MigrateAsync();
        await using var factory = new MesApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MesApiFactory.PlannerToken);
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"csv-{Guid.NewGuid():N}");
        using var content = new ByteArrayContent(new byte[10 * 1024 * 1024 + 1]);
        content.Headers.ContentType = new MediaTypeHeaderValue("text/csv");

        var response = await client.PostAsync("/api/csv-imports/orders", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_FAILED", (await response.Content.ReadFromJsonAsync<ApiError>())?.Code);
    }

    [PostgresFact]
    public async Task CsvImportStopsUnknownLengthStreamAboveTenMegabytes()
    {
        await MesApiFactory.MigrateAsync();
        await using var factory = new MesApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MesApiFactory.PlannerToken);
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"csv-{Guid.NewGuid():N}");
        using var content = new UnknownLengthContent(10 * 1024 * 1024 + 1);

        var response = await client.PostAsync("/api/csv-imports/orders", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_FAILED", (await response.Content.ReadFromJsonAsync<ApiError>())?.Code);
    }

    private sealed class UnknownLengthContent(int length) : HttpContent
    {
        protected override bool TryComputeLength(out long computedLength)
        {
            computedLength = 0;
            return false;
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            var buffer = new byte[81920];
            var remaining = length;
            while (remaining > 0)
            {
                var count = Math.Min(buffer.Length, remaining);
                await stream.WriteAsync(buffer.AsMemory(0, count));
                remaining -= count;
            }
        }
    }
}
