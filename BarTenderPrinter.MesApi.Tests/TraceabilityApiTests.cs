using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BarTenderPrinter.Domain.Common;
using BarTenderPrinter.Domain.Numbering;
using BarTenderPrinter.Domain.Orders;
using BarTenderPrinter.Domain.Packaging;
using BarTenderPrinter.Domain.Production;
using BarTenderPrinter.Domain.Routing;
using BarTenderPrinter.MesApi;
using BarTenderPrinter.Persistence;
using Npgsql;
using Xunit;

namespace BarTenderPrinter.MesApi.Tests;

public sealed class TraceabilityApiTests
{
    [PostgresFact]
    public async Task AllSupportedKeysReturnAssociatedProductionHistory()
    {
        await MesApiFactory.MigrateAsync();
        var trace = await SeedTraceAsync();
        await using var factory = new MesApiFactory(trace.StationId);
        using var client = CreateViewer(factory);
        var queries = new Dictionary<string, string>
        {
            ["Order"] = trace.OrderNumber,
            ["Imei"] = trace.Imei,
            ["SerialNumber"] = trace.SerialNumber,
            ["Carton"] = trace.CartonCode,
            ["Pallet"] = trace.PalletCode
        };

        foreach (var query in queries)
        {
            var response = await client.GetAsync($"/api/traceability?type={query.Key}&value={query.Value}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<TraceabilitySnapshot>(JsonOptions);
            Assert.Equal(trace.OrderId, result?.Order.Id);
            Assert.Contains(result!.ProductionUnits, unit => unit.Id == trace.UnitId);
            Assert.Contains(result.StationPasses, pass => pass.OperationId == "OP-10");
            Assert.Contains(result.PackagingUnits, unit => unit.Code == trace.PalletCode);
            Assert.Contains(result.PrintJobs, job => job.JobId == trace.PrintJobId);
            Assert.Contains(result.AuditEvents, audit => audit.EntityId == trace.UnitId);
        }
    }

    [PostgresFact]
    public async Task InvalidTypeAndMissingValueReturnStableErrors()
    {
        await MesApiFactory.MigrateAsync();
        await using var factory = new MesApiFactory();
        using var client = CreateViewer(factory);

        var invalid = await client.GetAsync("/api/traceability?type=Unknown&value=x");
        var missing = await client.GetAsync($"/api/traceability?type=Imei&value={Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("VALIDATION_FAILED", (await invalid.Content.ReadFromJsonAsync<ApiError>())?.Code);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("NOT_FOUND", (await missing.Content.ReadFromJsonAsync<ApiError>())?.Code);
    }

    private static async Task<TraceContext> SeedTraceAsync()
    {
        await using var dataSource = CreateDataSource();
        var now = DateTimeOffset.UtcNow;
        var order = new ProductionOrder(EntityId.New(), Unique("ORDER"), "Customer", "M1", "BLACK", 1);
        var orders = new ProductionOrderRepository(dataSource);
        await orders.InsertAsync(order);
        await orders.UpdateStateAsync(order.Id.Value, ProductionOrderStatus.InProduction, 0);

        var unit = new ProductionUnit(EntityId.New(), order.Id);
        var sn = Unique("SN");
        var imei = $"86{Random.Shared.NextInt64(1000000000000, 9999999999999)}";
        unit.AssignIdentifier(NumberType.SerialNumber, Allocation(sn, NumberType.SerialNumber, now));
        unit.AssignIdentifier(NumberType.Imei, Allocation(imei, NumberType.Imei, now));
        unit.Activate();
        await new ProductionUnitRepository(dataSource).InsertAsync(unit);

        var route = new ManufacturingRoute(EntityId.New(), order.Id, "Assembly", RouteType.Standard,
        [
            new ManufacturingOperation { Id = "OP-10", Name = "Assembly", Sequence = 10 }
        ]);
        var configuration = new ManufacturingConfigurationRepository(dataSource);
        await configuration.InsertRouteAsync(route);
        var stationId = Unique("STATION");
        await configuration.InsertStationAsync(new Station(new EntityId(stationId), "Station 1", ["OP-10"]));
        await new StationPassRepository(dataSource).PassAsync(unit.Id.Value, order.Id.Value, route.Id.Value, "OP-10",
            stationId, "operator-1", new IdempotencyKey(Unique("pass")), Unique("hash"), now);

        var packaging = new PackagingRepository(dataSource);
        var body = new PackagingUnit(EntityId.New(), order.Id, PackagingUnitType.Body, Unique("BODY"),
            "M1", "BLACK", 0, unit.Id);
        var box = new PackagingUnit(EntityId.New(), order.Id, PackagingUnitType.ColorBox, Unique("BOX"), "M1", "BLACK", 1);
        var carton = new PackagingUnit(EntityId.New(), order.Id, PackagingUnitType.Carton, Unique("CARTON"), "M1", "BLACK", 1);
        var pallet = new PackagingUnit(EntityId.New(), order.Id, PackagingUnitType.Pallet, Unique("PALLET"), "M1", "BLACK", 1);
        foreach (var packagingUnit in new[] { body, box, carton, pallet })
            await packaging.InsertUnitAsync(packagingUnit);
        await packaging.BindPackagingAsync(box.Id.Value, body.Id.Value, 0, "operator-1", now);
        await packaging.BindPackagingAsync(carton.Id.Value, box.Id.Value, 0, "operator-1", now.AddMilliseconds(1));
        await packaging.BindPackagingAsync(pallet.Id.Value, carton.Id.Value, 0, "operator-1", now.AddMilliseconds(2));

        var printJob = new PrintJobSnapshot(Guid.NewGuid().ToString("N"), Unique("print"), "Carton",
            "carton-template", "v1", "Received", Unique("hash"), "{}", null, 0, now, now,
            TraceOrderId: order.Id.Value, TraceUnitId: unit.Id.Value, TracePackagingUnitId: carton.Id.Value);
        await new PrintJobRepository(dataSource).RegisterAsync(printJob);
        await new AuditEventRepository(dataSource).AppendAsync(new AuditEventSnapshot(Guid.NewGuid().ToString("N"),
            "operator-1", stationId, "shift-a", Unique("correlation"), "UnitTraced", "ProductionUnit",
            unit.Id.Value, null, JsonSerializer.Serialize(new { unit.Status }), now));

        return new TraceContext(order.Id.Value, order.OrderNumber, unit.Id.Value, sn, imei, carton.Code,
            pallet.Code, stationId, printJob.JobId);
    }

    private static NumberAllocation Allocation(string value, NumberType type, DateTimeOffset now) => new(
        EntityId.New(), new EntityId($"range-{type}"), value, new IdempotencyKey(Unique("allocation")),
        "station-1", "operator-1", now);

    private static HttpClient CreateViewer(MesApiFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MesApiFactory.ViewerToken);
        return client;
    }

    private static NpgsqlDataSource CreateDataSource() =>
        NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("BARTENDER_TEST_POSTGRES")!);

    private static JsonSerializerOptions JsonOptions { get; } = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private sealed record TraceContext(string OrderId, string OrderNumber, string UnitId, string SerialNumber,
        string Imei, string CartonCode, string PalletCode, string StationId, string PrintJobId);
}
