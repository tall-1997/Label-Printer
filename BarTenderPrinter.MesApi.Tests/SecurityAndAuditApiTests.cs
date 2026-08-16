using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using BarTenderPrinter.MesApi;
using BarTenderPrinter.Persistence;
using Npgsql;
using Xunit;

namespace BarTenderPrinter.MesApi.Tests;

public sealed class SecurityAndAuditApiTests
{
    [PostgresFact]
    public async Task SessionWithoutShiftIsRejected()
    {
        await MesApiFactory.MigrateAsync();
        await using var factory = new MesApiFactory(operatorShiftId: "");
        using var client = CreateOperator(factory);

        var response = await client.GetAsync("/api/orders/missing");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("UNAUTHORIZED", (await response.Content.ReadFromJsonAsync<ApiError>())?.Code);
    }

    [PostgresFact]
    public async Task NumberAllocationAuditUsesSessionAndIsWrittenOnce()
    {
        await MesApiFactory.MigrateAsync();
        await using var factory = new MesApiFactory();
        using var planner = CreateClient(factory, MesApiFactory.PlannerToken);
        var order = await PostObjectAsync(planner, "/api/orders", new CreateProductionOrderRequest(
            $"ORDER-{Guid.NewGuid():N}", "Customer", "M1", "BLACK", 1, null, null));
        var range = await PostObjectAsync(planner, "/api/number-ranges", new
        {
            orderId = order.GetProperty("id").GetString(),
            numberType = "SerialNumber",
            prefix = "SN",
            datePattern = "None",
            start = 1,
            end = 2,
            step = 1,
            numericWidth = 4,
            validationPattern = ""
        });
        var rangeId = range.GetProperty("id").GetString()!;
        var key = $"allocation-{Guid.NewGuid():N}";
        using var station = CreateOperator(factory);

        await station.PostAsJsonAsync($"/api/number-ranges/{rangeId}/allocations", new { idempotencyKey = key });
        await station.PostAsJsonAsync($"/api/number-ranges/{rangeId}/allocations", new { idempotencyKey = key });

        await using var dataSource = CreateDataSource();
        await using var command = dataSource.CreateCommand("""
            SELECT actor_id, station_id, shift_id, count(*) OVER ()
            FROM audit_events WHERE action='NumberAllocated' AND entity_id=$1
            """);
        command.Parameters.AddWithValue(rangeId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("operator-1", reader.GetString(0));
        Assert.Equal("station-1", reader.GetString(1));
        Assert.Equal("shift-a", reader.GetString(2));
        Assert.Equal(1, reader.GetInt64(3));
    }

    [Fact]
    public void AuditSnapshotRedactsIdentifiersAndNestedDiagnostics()
    {
        var json = AuditSnapshot.Serialize(new
        {
            Imei = "861234567890123",
            SerialNumber = "SN001",
            ResultJson = JsonSerializer.Serialize(new { diagnostics = "device-path", message = "failed" })
        });

        Assert.DoesNotContain("861234567890123", json);
        Assert.DoesNotContain("SN001", json);
        Assert.DoesNotContain("device-path", json);
        Assert.Contains("***", json);
        Assert.DoesNotContain("failed", json);
        Assert.Contains("sha256", json);
        Assert.Contains("redacted", json);
    }

    [Fact]
    public void AuditSnapshotSummarizesDiagnosticVariantsAndRetainsWhitelistedMetadata()
    {
        var json = AuditSnapshot.Serialize(new
        {
            State = "Failed",
            Diagnostic = "secret diagnostic",
            DiagnosticCode = "DEVICE_SECRET",
            ResultJson = JsonSerializer.Serialize(new { token = "secret token" }),
            SafeCount = 3
        });

        Assert.DoesNotContain("secret", json);
        Assert.Contains("Failed", json);
        Assert.Contains("SafeCount", json);
        Assert.Equal(3, JsonNode.Parse(json!)!["SafeCount"]!.GetValue<int>());
    }

    private static async Task<JsonElement> PostObjectAsync(HttpClient client, string path, object request)
    {
        var response = await client.PostAsJsonAsync(path, request);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return document.RootElement.Clone();
    }

    private static HttpClient CreateOperator(MesApiFactory factory) =>
        CreateClient(factory, MesApiFactory.OperatorToken);

    private static HttpClient CreateClient(MesApiFactory factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static NpgsqlDataSource CreateDataSource() =>
        NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("BARTENDER_TEST_POSTGRES")!);
}
