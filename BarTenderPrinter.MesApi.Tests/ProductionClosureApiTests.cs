using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BarTenderPrinter.Domain.Common;
using BarTenderPrinter.Domain.Numbering;
using BarTenderPrinter.Domain.Orders;
using BarTenderPrinter.Domain.Packaging;
using BarTenderPrinter.Domain.Production;
using BarTenderPrinter.Domain.Routing;
using BarTenderPrinter.MesApi;
using Xunit;

namespace BarTenderPrinter.MesApi.Tests;

public sealed class ProductionClosureApiTests
{
    [PostgresFact]
    public async Task OrderTransitionsEnforceStateMachineAndReplay()
    {
        await MesApiFactory.MigrateAsync();
        await using var factory = new MesApiFactory();
        using var client = Client(factory, MesApiFactory.PlannerToken);
        var order = await Json(await client.PostAsJsonAsync("/api/orders", Order()));
        var orderId = order.GetProperty("id").GetString()!;
        var invalid = await client.PostAsJsonAsync($"/api/orders/{orderId}/transitions",
            new TransitionOrderRequest(ProductionOrderStatus.Paused, 0, Unique("transition")));
        Assert.Equal(HttpStatusCode.Conflict, invalid.StatusCode);
        Assert.Equal("ORDER_STATE_CONFLICT", (await invalid.Content.ReadFromJsonAsync<ApiError>())?.Code);

        var request = new TransitionOrderRequest(ProductionOrderStatus.Published, 0, Unique("transition"));
        var first = await client.PostAsJsonAsync($"/api/orders/{orderId}/transitions", request);
        var replay = await client.PostAsJsonAsync($"/api/orders/{orderId}/transitions", request);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal((await Json(first)).GetProperty("version").GetInt64(),
            (await Json(replay)).GetProperty("version").GetInt64());
    }

    [PostgresFact]
    public async Task MasterDataWeightAndWriteTaskCompleteFromEmptySchema()
    {
        await MesApiFactory.MigrateAsync();
        await using var factory = new MesApiFactory();
        using var planner = Client(factory, MesApiFactory.PlannerToken);
        using var station = Client(factory, MesApiFactory.OperatorToken);
        var order = await Json(await planner.PostAsJsonAsync("/api/orders", Order()));
        var orderId = order.GetProperty("id").GetString()!;
        var operationId = Unique("OP");
        var route = await planner.PostAsJsonAsync("/api/routes", new CreateRouteRequest(orderId, "Assembly",
            RouteType.Standard, [new CreateOperationRequest(operationId, "Assembly", 10)], Unique("route")));
        Assert.Equal(HttpStatusCode.Created, route.StatusCode);
        var stationMaster = await planner.PostAsJsonAsync("/api/stations",
            new CreateStationRequest("Assembly Station", [operationId], Unique("station")));
        Assert.Equal(HttpStatusCode.Created, stationMaster.StatusCode);
        var numberPrefix = $"SN{Guid.NewGuid():N}"[..10];
        var range = await Json(await planner.PostAsJsonAsync("/api/number-ranges", new CreateNumberRangeRequest(
            orderId, NumberType.SerialNumber, numberPrefix, NumberDatePattern.None, 1, 10, 1, 4, "")));
        var allocation = await Json(await station.PostAsJsonAsync(
            $"/api/number-ranges/{range.GetProperty("id").GetString()}/allocations",
            new AllocateNumberRequest(Unique("allocation"))));
        var allocationId = allocation.GetProperty("id").GetString()!;
        var releasable = await Json(await station.PostAsJsonAsync(
            $"/api/number-ranges/{range.GetProperty("id").GetString()}/allocations",
            new AllocateNumberRequest(Unique("allocation"))));
        using (var manager = Client(factory, MesApiFactory.HighRiskToken))
        {
            var releasableId = releasable.GetProperty("id").GetString()!;
            var release = await manager.PostAsJsonAsync($"/api/number-allocations/{releasableId}/status",
                new ChangeNumberStatusRequest(NumberAllocationStatus.Released, "UNUSED", Unique("release")));
            Assert.Equal(HttpStatusCode.OK, release.StatusCode);
            var history = await Json(await manager.GetAsync($"/api/number-allocations/{releasableId}/history"));
            Assert.Equal("Released", history[0].GetProperty("nextStatus").GetString());
        }
        var unitResponse = await planner.PostAsJsonAsync("/api/production-units", new CreateProductionUnitRequest(
            orderId, new Dictionary<NumberType, string> { [NumberType.SerialNumber] = allocationId }, Unique("unit")));
        Assert.Equal(HttpStatusCode.Created, unitResponse.StatusCode);
        var unitId = (await Json(unitResponse)).GetProperty("id").GetString()!;

        var bodyResponse = await planner.PostAsJsonAsync("/api/packaging-units", new CreatePackagingUnitRequest(
            orderId, PackagingUnitType.Body, Unique("BODY"), "M1", "BLACK", 0, unitId, Unique("body")));
        Assert.Equal(HttpStatusCode.Created, bodyResponse.StatusCode);
        var cartonResponse = await planner.PostAsJsonAsync("/api/packaging-units", new CreatePackagingUnitRequest(
            orderId, PackagingUnitType.Carton, Unique("CARTON"), "M1", "BLACK", 1, null, Unique("carton")));
        var cartonId = (await Json(cartonResponse)).GetProperty("id").GetString()!;
        var rule = await planner.PostAsJsonAsync("/api/weight-rules", new CreateWeightRuleRequest(orderId,
            PackagingUnitType.Carton, 10, 20, "kg", Unique("rule")));
        Assert.Equal(HttpStatusCode.Created, rule.StatusCode);
        var measurement = await station.PostAsJsonAsync($"/api/packaging-units/{cartonId}/weights",
            new RecordWeightRequest(25, "kg", "scale-1", true, Unique("weight")));
        Assert.Equal("Failed", (await Json(measurement)).GetProperty("result").GetString());

        var taskResponse = await station.PostAsJsonAsync("/api/identifier-write-tasks",
            new CreateIdentifierWriteTaskRequest(unitId, [allocationId], "android", "station-1", Unique("write")));
        var taskId = (await Json(taskResponse)).GetProperty("id").GetString()!;
        await using (var dataSource = Npgsql.NpgsqlDataSource.Create(
            Environment.GetEnvironmentVariable("BARTENDER_TEST_POSTGRES")!))
        await using (var isolateQueue = dataSource.CreateCommand(
            "UPDATE identifier_write_tasks SET state='Failed' WHERE state='Pending' AND id<>$1"))
        {
            isolateQueue.Parameters.AddWithValue(taskId);
            await isolateQueue.ExecuteNonQueryAsync();
        }
        var claim = await station.PostAsJsonAsync("/api/identifier-write-tasks/claims",
            new ClaimIdentifierWriteTaskRequest("android", Unique("claim")));
        Assert.Equal(taskId, (await Json(claim)).GetProperty("task").GetProperty("id").GetString());
        using var result = JsonDocument.Parse("{\"message\":\"timeout\"}");
        var receipt = await station.PostAsJsonAsync($"/api/identifier-write-tasks/{taskId}/results",
            new IdentifierWriteResultRequest(IdentifierWriteTaskState.Uncertain, result.RootElement.Clone(),
                "TOOL_TIMEOUT", Unique("result")));
        Assert.Equal("Uncertain", (await Json(receipt)).GetProperty("state").GetString());
    }

    [PostgresFact]
    public async Task NumberDispositionRequiresPrivilegedRole()
    {
        await MesApiFactory.MigrateAsync();
        await using var factory = new MesApiFactory();
        using var station = Client(factory, MesApiFactory.OperatorToken);

        var response = await station.PostAsJsonAsync($"/api/number-allocations/{Unique("allocation")}/status",
            new ChangeNumberStatusRequest(NumberAllocationStatus.Scrapped, "DAMAGE", Unique("status")));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [PostgresFact]
    public async Task NumberHistoryRequiresNumberDispositionRole()
    {
        await MesApiFactory.MigrateAsync();
        await using var factory = new MesApiFactory();
        using var station = Client(factory, MesApiFactory.OperatorToken);

        var response = await station.GetAsync($"/api/number-allocations/{Unique("allocation")}/history");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [PostgresFact]
    public async Task CoreEndpointPrefersHeaderIdempotencyKeyAndRejectsMismatch()
    {
        await MesApiFactory.MigrateAsync();
        await using var factory = new MesApiFactory();
        using var planner = Client(factory, MesApiFactory.PlannerToken);
        var orderId = (await Json(await planner.PostAsJsonAsync("/api/orders", Order()))).GetProperty("id").GetString()!;
        var headerKey = Unique("header");
        using var matching = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{orderId}/transitions")
        {
            Content = JsonContent.Create(new { targetStatus = "Published", expectedVersion = 0L })
        };
        matching.Headers.Add("Idempotency-Key", headerKey);

        var accepted = await planner.SendAsync(matching);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        using var reorderedReplay = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/orders/{orderId}/transitions")
        {
            Content = new StringContent("{\"expectedVersion\":0,\"targetStatus\":\"Published\"}",
                System.Text.Encoding.UTF8, "application/json")
        };
        reorderedReplay.Headers.Add("Idempotency-Key", headerKey);
        var replay = await planner.SendAsync(reorderedReplay);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal((await Json(accepted)).GetProperty("version").GetInt64(),
            (await Json(replay)).GetProperty("version").GetInt64());

        using var mismatching = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{orderId}/transitions")
        {
            Content = JsonContent.Create(new TransitionOrderRequest(ProductionOrderStatus.InProduction, 1, Unique("body")))
        };
        mismatching.Headers.Add("Idempotency-Key", Unique("header"));
        var rejected = await planner.SendAsync(mismatching);

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal("VALIDATION_FAILED", (await rejected.Content.ReadFromJsonAsync<ApiError>())?.Code);

        var missing = await planner.PostAsJsonAsync($"/api/orders/{orderId}/transitions",
            new { targetStatus = "InProduction", expectedVersion = 1L });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal("VALIDATION_FAILED", (await missing.Content.ReadFromJsonAsync<ApiError>())?.Code);

        using var duplicateHeaders = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{orderId}/transitions")
        {
            Content = JsonContent.Create(new { targetStatus = "InProduction", expectedVersion = 1L })
        };
        duplicateHeaders.Headers.TryAddWithoutValidation("Idempotency-Key", [Unique("first"), Unique("second")]);
        var duplicateRejected = await planner.SendAsync(duplicateHeaders);
        Assert.Equal(HttpStatusCode.BadRequest, duplicateRejected.StatusCode);
        Assert.Equal("VALIDATION_FAILED", (await duplicateRejected.Content.ReadFromJsonAsync<ApiError>())?.Code);
    }

    private static HttpClient Client(MesApiFactory factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static CreateProductionOrderRequest Order() =>
        new(Unique("ORDER"), "Customer", "M1", "BLACK", 100, null, null);
    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return document.RootElement.Clone();
    }
    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";
}
