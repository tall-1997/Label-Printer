using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BarTenderPrinter.Domain.Common;
using BarTenderPrinter.Domain.Orders;
using BarTenderPrinter.Domain.Packaging;
using BarTenderPrinter.Domain.Production;
using BarTenderPrinter.Domain.Routing;
using BarTenderPrinter.MesApi;
using BarTenderPrinter.Persistence;
using Npgsql;
using Xunit;

namespace BarTenderPrinter.MesApi.Tests;

public sealed class StationPassAndPackagingApiTests
{
    [PostgresFact]
    public async Task StationPassRequiresPreviousOperationThenReplays()
    {
        await MesApiFactory.MigrateAsync();
        var context = await SeedManufacturingAsync();
        await using var factory = new MesApiFactory(context.StationId);
        using var client = CreateOperator(factory);
        var secondKey = $"pass-{Guid.NewGuid():N}";

        var blocked = await client.PostAsJsonAsync("/api/station-passes", PassRequest(context, "OP-20", secondKey));
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        var blockedError = await blocked.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("PREVIOUS_OPERATION_INCOMPLETE", blockedError?.Code);

        var firstKey = $"pass-{Guid.NewGuid():N}";
        var first = await client.PostAsJsonAsync("/api/station-passes", PassRequest(context, "OP-10", firstKey));
        var replay = await client.PostAsJsonAsync("/api/station-passes", PassRequest(context, "OP-10", firstKey));
        var second = await client.PostAsJsonAsync("/api/station-passes", PassRequest(context, "OP-20", secondKey));

        Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());
        Assert.True((await ReadObjectAsync(replay)).GetProperty("isReplay").GetBoolean());
        Assert.True(second.IsSuccessStatusCode, await second.Content.ReadAsStringAsync());
    }

    [PostgresFact]
    public async Task StationPassUsesAuthenticatedStationQualification()
    {
        await MesApiFactory.MigrateAsync();
        var context = await SeedManufacturingAsync(qualifiedOperations: ["OP-10"]);
        await using var factory = new MesApiFactory(context.StationId);
        using var client = CreateOperator(factory);
        var response = await client.PostAsJsonAsync("/api/station-passes",
            PassRequest(context, "OP-20", $"pass-{Guid.NewGuid():N}"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("STATION_NOT_QUALIFIED", (await response.Content.ReadFromJsonAsync<ApiError>())?.Code);
    }

    [PostgresFact]
    public async Task FullCartonReturnsImmutablePrintIntentAndReplays()
    {
        await MesApiFactory.MigrateAsync();
        var context = await SeedPackagingAsync();
        await using var factory = new MesApiFactory();
        using var client = CreateOperator(factory);
        var key = $"binding-{Guid.NewGuid():N}";
        var request = new BindPackagingRequest(context.CartonId, context.ColorBoxId, 0, key);

        var first = await client.PostAsJsonAsync("/api/packaging-bindings", request);
        var replay = await client.PostAsJsonAsync("/api/packaging-bindings", request);

        Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());
        var firstJson = await ReadObjectAsync(first);
        Assert.True(firstJson.GetProperty("parentClosed").GetBoolean());
        Assert.Equal("Carton", firstJson.GetProperty("printIntent").GetProperty("labelType").GetString());
        Assert.Contains(context.CartonCode,
            firstJson.GetProperty("printIntent").GetProperty("fieldsJson").GetString());
        Assert.True((await ReadObjectAsync(replay)).GetProperty("isReplay").GetBoolean());
    }

    [PostgresFact]
    public async Task PackagingKeyReuseForDifferentChildConflicts()
    {
        await MesApiFactory.MigrateAsync();
        var context = await SeedPackagingAsync(cartonCapacity: 2);
        await using var dataSource = CreateDataSource();
        var anotherBox = new PackagingUnit(EntityId.New(), new EntityId(context.OrderId), PackagingUnitType.ColorBox,
            Unique("BOX"), "M1", "BLACK", 1);
        var anotherBody = new PackagingUnit(EntityId.New(), new EntityId(context.OrderId), PackagingUnitType.Body,
            Unique("BODY"), "M1", "BLACK", 0);
        var repository = new PackagingRepository(dataSource);
        await repository.InsertUnitAsync(anotherBox);
        await repository.InsertUnitAsync(anotherBody);
        await repository.BindPackagingAsync(anotherBox.Id.Value, anotherBody.Id.Value, 0, "seed", DateTimeOffset.UtcNow);
        await using var factory = new MesApiFactory();
        using var client = CreateOperator(factory);
        var key = $"binding-{Guid.NewGuid():N}";

        var first = await client.PostAsJsonAsync("/api/packaging-bindings",
            new BindPackagingRequest(context.CartonId, context.ColorBoxId, 0, key));
        var conflict = await client.PostAsJsonAsync("/api/packaging-bindings",
            new BindPackagingRequest(context.CartonId, anotherBox.Id.Value, 1, key));

        Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("IDEMPOTENCY_CONFLICT", (await conflict.Content.ReadFromJsonAsync<ApiError>())?.Code);
    }

    private static async Task<ManufacturingContext> SeedManufacturingAsync(string[]? qualifiedOperations = null)
    {
        await using var dataSource = CreateDataSource();
        var order = new ProductionOrder(EntityId.New(), Unique("ORDER"), "Customer", "M1", "BLACK", 10);
        var orders = new ProductionOrderRepository(dataSource);
        await orders.InsertAsync(order);
        await orders.UpdateStateAsync(order.Id.Value, ProductionOrderStatus.InProduction, 0);
        var unit = new ProductionUnit(EntityId.New(), order.Id);
        unit.Activate();
        await new ProductionUnitRepository(dataSource).InsertAsync(unit);
        var route = new ManufacturingRoute(EntityId.New(), order.Id, "Assembly", RouteType.Standard,
        [
            new ManufacturingOperation { Id = "OP-10", Name = "Assembly", Sequence = 10 },
            new ManufacturingOperation { Id = "OP-20", Name = "Inspect", Sequence = 20 }
        ]);
        var configuration = new ManufacturingConfigurationRepository(dataSource);
        await configuration.InsertRouteAsync(route);
        var stationId = Unique("STATION");
        await configuration.InsertStationAsync(new Station(new EntityId(stationId), "Station 1",
            qualifiedOperations ?? ["OP-10", "OP-20"]));
        return new ManufacturingContext(order.Id.Value, unit.Id.Value, route.Id.Value, stationId);
    }

    private static async Task<PackagingContext> SeedPackagingAsync(int cartonCapacity = 1)
    {
        await using var dataSource = CreateDataSource();
        var order = new ProductionOrder(EntityId.New(), Unique("ORDER"), "Customer", "M1", "BLACK", 10);
        await new ProductionOrderRepository(dataSource).InsertAsync(order);
        var repository = new PackagingRepository(dataSource);
        var carton = new PackagingUnit(EntityId.New(), order.Id, PackagingUnitType.Carton,
            Unique("CARTON"), "M1", "BLACK", cartonCapacity);
        var box = new PackagingUnit(EntityId.New(), order.Id, PackagingUnitType.ColorBox,
            Unique("BOX"), "M1", "BLACK", 1);
        var body = new PackagingUnit(EntityId.New(), order.Id, PackagingUnitType.Body,
            Unique("BODY"), "M1", "BLACK", 0);
        await repository.InsertUnitAsync(carton);
        await repository.InsertUnitAsync(box);
        await repository.InsertUnitAsync(body);
        await repository.BindPackagingAsync(box.Id.Value, body.Id.Value, 0, "seed", DateTimeOffset.UtcNow);
        return new PackagingContext(order.Id.Value, carton.Id.Value, carton.Code, box.Id.Value);
    }

    private static StationPassRequest PassRequest(ManufacturingContext context, string operationId, string key) =>
        new(context.UnitId, context.OrderId, context.RouteId, operationId, key);

    private static HttpClient CreateOperator(MesApiFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MesApiFactory.OperatorToken);
        return client;
    }

    private static NpgsqlDataSource CreateDataSource() =>
        NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("BARTENDER_TEST_POSTGRES")!);

    private static async Task<JsonElement> ReadObjectAsync(HttpResponseMessage response)
    {
        var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return document.RootElement.Clone();
    }

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private sealed record ManufacturingContext(string OrderId, string UnitId, string RouteId, string StationId);
    private sealed record PackagingContext(string OrderId, string CartonId, string CartonCode, string ColorBoxId);
}
