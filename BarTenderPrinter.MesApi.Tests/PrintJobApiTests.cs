using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BarTenderPrinter.MesApi;
using BarTenderPrinter.Persistence;
using Npgsql;
using Xunit;

namespace BarTenderPrinter.MesApi.Tests;

public sealed class PrintJobApiTests
{
    [PostgresFact]
    public async Task ClaimReturnsOldestJobAndReplays()
    {
        await MesApiFactory.MigrateAsync();
        var job = await SeedPrintJobAsync();
        await using var factory = new MesApiFactory();
        using var client = CreateOperator(factory);
        var request = new ClaimPrintJobRequest($"claim-{Guid.NewGuid():N}");

        var first = await client.PostAsJsonAsync("/api/print-jobs/claims", request);
        var replay = await client.PostAsJsonAsync("/api/print-jobs/claims", request);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstResult = await first.Content.ReadFromJsonAsync<PrintJobClaimResult>();
        Assert.Equal(job.JobId, firstResult?.Job?.JobId);
        Assert.Equal("Submitting", firstResult?.Job?.State);
        Assert.Equal("station-1", firstResult?.Job?.ClaimedByStationId);
        Assert.False(firstResult?.IsReplay);
        Assert.True((await replay.Content.ReadFromJsonAsync<PrintJobClaimResult>())?.IsReplay);
    }

    [PostgresFact]
    public async Task EmptyClaimReturnsNoContent()
    {
        await MesApiFactory.MigrateAsync();
        await using var factory = new MesApiFactory();
        using var client = CreateOperator(factory);

        var request = new ClaimPrintJobRequest($"empty-{Guid.NewGuid():N}");
        var response = await client.PostAsJsonAsync("/api/print-jobs/claims", request);
        await SeedPrintJobAsync();
        var replay = await client.PostAsJsonAsync("/api/print-jobs/claims", request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
    }

    [PostgresFact]
    public async Task ReceiptUpdatesJobAndReplaysThenSupportsRecoveryQueries()
    {
        await MesApiFactory.MigrateAsync();
        var job = await SeedPrintJobAsync();
        await using var factory = new MesApiFactory();
        using var client = CreateOperator(factory);
        await ClaimAsync(client);
        var receipt = new PrintJobReceiptRequest($"receipt-{Guid.NewGuid():N}", "Submitted",
            JsonSerializer.SerializeToElement(new { message = "accepted", diagnostics = "queue=1" }));

        var first = await client.PostAsJsonAsync($"/api/print-jobs/{job.JobId}/receipts", receipt);
        var replay = await client.PostAsJsonAsync($"/api/print-jobs/{job.JobId}/receipts", receipt);
        var byId = await client.GetFromJsonAsync<PrintJobSnapshot>($"/api/print-jobs/{job.JobId}");
        var byKey = await client.GetFromJsonAsync<PrintJobSnapshot>(
            $"/api/print-jobs/by-idempotency-key/{job.IdempotencyKey}");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("Submitted", (await first.Content.ReadFromJsonAsync<PrintJobReceiptResult>())?.Job.State);
        Assert.True((await replay.Content.ReadFromJsonAsync<PrintJobReceiptResult>())?.IsReplay);
        Assert.Equal("Submitted", byId?.State);
        Assert.Equal(job.JobId, byKey?.JobId);
    }

    [PostgresFact]
    public async Task ReceiptRejectsUnknownStateAndDifferentStation()
    {
        await MesApiFactory.MigrateAsync();
        var job = await SeedPrintJobAsync();
        await using var factory = new MesApiFactory();
        using var client = CreateOperator(factory);
        await ClaimAsync(client);
        var invalid = new PrintJobReceiptRequest($"receipt-{Guid.NewGuid():N}", "Verified",
            JsonSerializer.SerializeToElement(new { message = "done" }));

        var invalidResponse = await client.PostAsJsonAsync($"/api/print-jobs/{job.JobId}/receipts", invalid);
        await using var otherFactory = new MesApiFactory("station-2");
        using var otherClient = CreateOperator(otherFactory);
        var otherResponse = await otherClient.PostAsJsonAsync($"/api/print-jobs/{job.JobId}/receipts",
            invalid with { IdempotencyKey = $"receipt-{Guid.NewGuid():N}", State = "Failed" });

        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        Assert.Equal("VALIDATION_FAILED", (await invalidResponse.Content.ReadFromJsonAsync<ApiError>())?.Code);
        Assert.Equal(HttpStatusCode.Conflict, otherResponse.StatusCode);
        Assert.Equal("PRINT_JOB_STATION_MISMATCH", (await otherResponse.Content.ReadFromJsonAsync<ApiError>())?.Code);
    }

    private static async Task<PrintJobSnapshot> SeedPrintJobAsync()
    {
        await using var dataSource = CreateDataSource();
        await using (var command = dataSource.CreateCommand(
            "UPDATE print_jobs SET state='Failed' WHERE state IN ('Received', 'Submitting')"))
            await command.ExecuteNonQueryAsync();
        var now = DateTimeOffset.UtcNow;
        var job = new PrintJobSnapshot(Guid.NewGuid().ToString("N"), $"print-{Guid.NewGuid():N}", "Carton",
            "carton-template", "v1", "Received", Guid.NewGuid().ToString("N"),
            JsonSerializer.Serialize(new { fields = new { PACKAGE_CODE = "C001" } }), null, 0, now, now);
        return await new PrintJobRepository(dataSource).RegisterAsync(job);
    }

    private static async Task ClaimAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/print-jobs/claims",
            new ClaimPrintJobRequest($"claim-{Guid.NewGuid():N}"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static HttpClient CreateOperator(MesApiFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MesApiFactory.OperatorToken);
        return client;
    }

    private static NpgsqlDataSource CreateDataSource() =>
        NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("BARTENDER_TEST_POSTGRES")!);
}
