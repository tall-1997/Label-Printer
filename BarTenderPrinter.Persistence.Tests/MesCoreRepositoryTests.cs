using System.Text.Json;
using BarTenderPrinter.Domain.Common;
using BarTenderPrinter.Domain.Numbering;
using BarTenderPrinter.Domain.Orders;
using BarTenderPrinter.Domain.Packaging;
using BarTenderPrinter.Domain.Production;
using Npgsql;
using Xunit;

namespace BarTenderPrinter.Persistence.Tests;

public sealed class MesCoreRepositoryTests
{
    [PostgresFact]
    public async Task OrderTransitionIsIdempotentAndAuditedInTransaction()
    {
        await using var dataSource = CreateDataSource();
        await new PostgresMigrator(dataSource).MigrateAsync();
        var order = CreateOrder();
        await new ProductionOrderRepository(dataSource).InsertAsync(order);
        var repository = new MesCoreRepository(dataSource);
        var key = new IdempotencyKey(Unique("transition"));
        var now = DateTimeOffset.UtcNow;

        var first = await repository.TransitionOrderAsync(order.Id.Value, ProductionOrderStatus.Published, 0,
            key, "same", now, auditFactory: (_, result) => Audit(result.Id, now));
        var replay = await repository.TransitionOrderAsync(order.Id.Value, ProductionOrderStatus.Published, 0,
            key, "same", now, auditFactory: (_, result) => Audit(result.Id, now));

        Assert.Equal(ProductionOrderStatus.Published.ToString(), first.Status);
        Assert.Equal(first, replay);
        await using var count = dataSource.CreateCommand(
            "SELECT count(*) FROM audit_events WHERE action='OrderStatusChanged' AND entity_id=$1");
        count.Parameters.AddWithValue(order.Id.Value);
        Assert.Equal(1L, await count.ExecuteScalarAsync());
    }

    [PostgresFact]
    public async Task FullPackagingHierarchyRegistersFourClaimableLabelJobs()
    {
        await using var dataSource = CreateDataSource();
        await new PostgresMigrator(dataSource).MigrateAsync();
        var order = CreateOrder();
        await new ProductionOrderRepository(dataSource).InsertAsync(order);
        var unit = new ProductionUnit(EntityId.New(), order.Id);
        unit.Activate();
        await new ProductionUnitRepository(dataSource).InsertAsync(unit);
        var core = new MesCoreRepository(dataSource);
        var body = new PackagingUnit(EntityId.New(), order.Id, PackagingUnitType.Body, Unique("BODY"), "M1", "BLACK", 0, unit.Id);
        var box = new PackagingUnit(EntityId.New(), order.Id, PackagingUnitType.ColorBox, Unique("BOX"), "M1", "BLACK", 1);
        var carton = new PackagingUnit(EntityId.New(), order.Id, PackagingUnitType.Carton, Unique("CARTON"), "M1", "BLACK", 1);
        var pallet = new PackagingUnit(EntityId.New(), order.Id, PackagingUnitType.Pallet, Unique("PALLET"), "M1", "BLACK", 1);
        foreach (var item in new[] { body, box, carton, pallet })
            await core.CreatePackagingUnitAsync(item, new IdempotencyKey(Unique("create")), Unique("hash"), DateTimeOffset.UtcNow);
        var packaging = new PackagingRepository(dataSource);
        await packaging.BindPackagingAsync(box.Id.Value, body.Id.Value, 0, "operator", DateTimeOffset.UtcNow);
        await packaging.BindPackagingAsync(carton.Id.Value, box.Id.Value, 0, "operator", DateTimeOffset.UtcNow);
        await packaging.BindPackagingAsync(pallet.Id.Value, carton.Id.Value, 0, "operator", DateTimeOffset.UtcNow);

        await using var command = dataSource.CreateCommand(
            "SELECT label_type FROM print_jobs WHERE trace_order_id=$1 AND state='Received' ORDER BY label_type");
        command.Parameters.AddWithValue(order.Id.Value);
        await using var reader = await command.ExecuteReaderAsync();
        var labels = new List<string>();
        while (await reader.ReadAsync()) labels.Add(reader.GetString(0));
        Assert.Equal(new[] { "Body", "Carton", "ColorBox", "Pallet" }, labels);
    }

    [PostgresFact]
    public async Task WeightAndUncertainWriteResultPersistHistoryAndFreezeIdentifiers()
    {
        await using var dataSource = CreateDataSource();
        await new PostgresMigrator(dataSource).MigrateAsync();
        var order = CreateOrder();
        await new ProductionOrderRepository(dataSource).InsertAsync(order);
        var range = new NumberRange(EntityId.New(), order.Id, NumberType.SerialNumber,
            $"SN{Guid.NewGuid():N}"[..10], NumberDatePattern.None, 1, 10);
        var ranges = new NumberRangeRepository(dataSource);
        await ranges.InsertAsync(range);
        var allocation = await ranges.AllocateAsync(range.Id.Value, new IdempotencyKey(Unique("allocate")), "allocation",
            "station", "operator", DateTimeOffset.UtcNow);
        var core = new MesCoreRepository(dataSource);
        var unit = await core.CreateProductionUnitAsync(order.Id.Value,
            new Dictionary<NumberType, string> { [NumberType.SerialNumber] = allocation.Id },
            new IdempotencyKey(Unique("unit")), "unit", DateTimeOffset.UtcNow);
        var carton = new PackagingUnit(EntityId.New(), order.Id, PackagingUnitType.Carton, Unique("CARTON"), "M1", "BLACK", 1);
        await core.CreatePackagingUnitAsync(carton, new IdempotencyKey(Unique("carton")), "carton", DateTimeOffset.UtcNow);
        var rule = new WeightRule(EntityId.New(), order.Id, "Carton", 10, 20, "kg");
        await core.CreateWeightRuleAsync(rule, new IdempotencyKey(Unique("rule")), "rule", DateTimeOffset.UtcNow);
        var measurement = await core.RecordWeightAsync(carton.Id.Value, 25, "kg", "scale-1", false,
            new IdempotencyKey(Unique("weight")), "weight", DateTimeOffset.UtcNow);
        Assert.Equal("Failed", measurement.Result);

        var task = await core.CreateWriteTaskAsync(unit.Id, [allocation.Id], "android", "station",
            new IdempotencyKey(Unique("write")), "write", DateTimeOffset.UtcNow);
        await using (var isolateQueue = dataSource.CreateCommand(
            "UPDATE identifier_write_tasks SET state='Failed' WHERE state='Pending' AND id<>$1"))
        {
            isolateQueue.Parameters.AddWithValue(task.Id);
            await isolateQueue.ExecuteNonQueryAsync();
        }
        var wrongPlatform = await core.ClaimWriteTaskAsync("station", "operator", "windows",
            new IdempotencyKey(Unique("claim")), "wrong-platform", DateTimeOffset.UtcNow);
        Assert.Null(wrongPlatform.Task);
        var wrongStation = await core.ClaimWriteTaskAsync("other-station", "operator", "android",
            new IdempotencyKey(Unique("claim")), "wrong-station", DateTimeOffset.UtcNow);
        Assert.Null(wrongStation.Task);
        var claim = await core.ClaimWriteTaskAsync("station", "operator", "android",
            new IdempotencyKey(Unique("claim")),
            "claim", DateTimeOffset.UtcNow);
        Assert.Equal(task.Id, claim.Task?.Id);
        var stationMismatch = await Assert.ThrowsAsync<PersistenceBusinessException>(() =>
            core.RecordWriteResultAsync(task.Id, "other-station", IdentifierWriteTaskState.Failed,
                JsonSerializer.Serialize(new { message = "failed" }), "FAILED",
                new IdempotencyKey(Unique("result")), "wrong-result-station", DateTimeOffset.UtcNow));
        Assert.Equal("WRITE_TASK_STATION_MISMATCH", stationMismatch.Code);
        await core.RecordWriteResultAsync(task.Id, "station", IdentifierWriteTaskState.Uncertain,
            JsonSerializer.Serialize(new { message = "timeout" }), "TOOL_TIMEOUT",
            new IdempotencyKey(Unique("result")), "result", DateTimeOffset.UtcNow);

        await using var status = dataSource.CreateCommand("SELECT status FROM number_allocations WHERE id=$1");
        status.Parameters.AddWithValue(allocation.Id);
        Assert.Equal("Frozen", await status.ExecuteScalarAsync());
        var history = await core.GetNumberHistoryAsync(allocation.Id);
        Assert.Contains(history, item => item.NextStatus == "Frozen" && item.ReasonCode == "WRITE_RESULT_UNCERTAIN");
    }

    private static ProductionOrder CreateOrder() =>
        new(EntityId.New(), Unique("ORDER"), "Customer", "M1", "BLACK", 100);
    private static AuditEventSnapshot Audit(string entityId, DateTimeOffset now) =>
        new(EntityId.New().Value, "actor", "station", "shift", Unique("correlation"),
            "OrderStatusChanged", "ProductionOrder", entityId, null, "{}", now);
    private static NpgsqlDataSource CreateDataSource() =>
        NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("BARTENDER_TEST_POSTGRES")!);
    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";
}
