using BarTenderPrinter.Domain.Common;
using System.Text.Json;
using BarTenderPrinter.Domain.Orders;
using BarTenderPrinter.Domain.Quality;
using BarTenderPrinter.Domain.Production;
using BarTenderPrinter.Domain.Shipping;
using BarTenderPrinter.Domain.Archiving;
using BarTenderPrinter.Domain.Packaging;
using BarTenderPrinter.Domain.Rework;
using BarTenderPrinter.Domain.Routing;
using System.Security.Cryptography;
using System.Text;
using Npgsql;
using Xunit;

namespace BarTenderPrinter.Persistence.Tests;

public sealed class QualityShippingArchiveRepositoryTests
{
    [PostgresFact]
    public async Task InspectionLotRejectsSampleFromAnotherOrder()
    {
        await using var dataSource = CreateDataSource();
        await new PostgresMigrator(dataSource).MigrateAsync();
        var firstOrder = new ProductionOrder(EntityId.New(), Unique("ORDER"), "Customer", "M1", "BLACK", 1);
        var secondOrder = new ProductionOrder(EntityId.New(), Unique("ORDER"), "Customer", "M1", "BLACK", 1);
        var orders = new ProductionOrderRepository(dataSource);
        await orders.InsertAsync(firstOrder);
        await orders.InsertAsync(secondOrder);
        var unit = new ProductionUnit(EntityId.New(), secondOrder.Id);
        await new ProductionUnitRepository(dataSource).InsertAsync(unit);
        var lot = new InspectionLot(EntityId.New(), firstOrder.Id, "OQC", "ONE", [unit.Id]);

        var exception = await Assert.ThrowsAsync<PersistenceBusinessException>(() =>
            new InspectionRepository(dataSource).CreateLotAsync(lot, DateTimeOffset.UtcNow));

        Assert.Equal("INSPECTION_SAMPLE_ORDER_MISMATCH", exception.Code);
    }

    [PostgresFact]
    public async Task ConcurrentInspectionResultWithSameKeyCreatesOneResult()
    {
        await using var dataSource = CreateDataSource();
        await new PostgresMigrator(dataSource).MigrateAsync();
        var order = new ProductionOrder(EntityId.New(), Unique("ORDER"), "Customer", "M1", "BLACK", 1);
        await new ProductionOrderRepository(dataSource).InsertAsync(order);
        var unit = new ProductionUnit(EntityId.New(), order.Id);
        await new ProductionUnitRepository(dataSource).InsertAsync(unit);
        var lot = new InspectionLot(EntityId.New(), order.Id, "OQC", "ONE", [unit.Id]);
        var repository = new InspectionRepository(dataSource);
        await repository.CreateLotAsync(lot, DateTimeOffset.UtcNow);
        var key = new IdempotencyKey(Unique("inspection"));
        var now = DateTimeOffset.UtcNow;

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => repository.AddResultAsync(
            lot.Id.Value, unit.Id.Value, "APPEARANCE", InspectionOutcome.Passed, "", "OP-1", "",
            key, "same-hash", now)));

        Assert.Single(results.Select(value => value.Id).Distinct());
        Assert.Equal(1, results.Count(value => !value.IsReplay));
    }

    [PostgresFact]
    public async Task ReleaseDispositionRestoresOriginalPackagingStatus()
    {
        await using var dataSource = CreateDataSource();
        await new PostgresMigrator(dataSource).MigrateAsync();
        var order = new ProductionOrder(EntityId.New(), Unique("ORDER"), "Customer", "M1", "BLACK", 1);
        await new ProductionOrderRepository(dataSource).InsertAsync(order);
        var unit = new ProductionUnit(EntityId.New(), order.Id);
        await new ProductionUnitRepository(dataSource).InsertAsync(unit);
        var body = new PackagingUnit(EntityId.New(), order.Id, PackagingUnitType.Body, Unique("BODY"),
            "M1", "BLACK", 0, unit.Id);
        await new PackagingRepository(dataSource).InsertUnitAsync(body);
        var lot = new InspectionLot(EntityId.New(), order.Id, "OQC", "ONE", [unit.Id]);
        var repository = new InspectionRepository(dataSource);
        await repository.CreateLotAsync(lot, DateTimeOffset.UtcNow);
        await repository.AddResultAsync(lot.Id.Value, unit.Id.Value, "APPEARANCE", InspectionOutcome.Failed,
            "SCRATCH", "OP-1", "", new IdempotencyKey(Unique("result")), "result-hash", DateTimeOffset.UtcNow);

        await repository.CompleteLotAsync(lot.Id.Value, 0);
        await repository.ApplyDispositionAsync(lot.Id.Value, DispositionDecision.Release, "ACCEPT", "manager",
            new IdempotencyKey(Unique("disposition")), "disposition-hash", DateTimeOffset.UtcNow);

        await using var status = dataSource.CreateCommand("SELECT status FROM packaging_units WHERE id=$1");
        status.Parameters.AddWithValue(body.Id.Value);
        Assert.Equal("Closed", await status.ExecuteScalarAsync());
    }

    [PostgresFact]
    public async Task InspectionResultAndDispositionReplayWithoutDuplicates()
    {
        await using var dataSource = CreateDataSource();
        await new PostgresMigrator(dataSource).MigrateAsync();
        var order = new ProductionOrder(EntityId.New(), Unique("ORDER"), "Customer", "M1", "BLACK", 1);
        await new ProductionOrderRepository(dataSource).InsertAsync(order);
        var unit = new ProductionUnit(EntityId.New(), order.Id);
        await new ProductionUnitRepository(dataSource).InsertAsync(unit);
        var lot = new InspectionLot(EntityId.New(), order.Id, "OQC", "ONE", [unit.Id]);
        var repository = new InspectionRepository(dataSource);
        await repository.CreateLotAsync(lot, DateTimeOffset.UtcNow);
        var resultKey = new IdempotencyKey(Unique("inspection"));

        var first = await repository.AddResultAsync(lot.Id.Value, unit.Id.Value, "APPEARANCE",
            InspectionOutcome.Failed, "SCRATCH", "OP-1", "", resultKey, "hash-1", DateTimeOffset.UtcNow);
        var replay = await repository.AddResultAsync(lot.Id.Value, unit.Id.Value, "APPEARANCE",
            InspectionOutcome.Failed, "SCRATCH", "OP-1", "", resultKey, "hash-1", DateTimeOffset.UtcNow);
        var completed = await repository.CompleteLotAsync(lot.Id.Value, 0);
        var disposition = await repository.ApplyDispositionAsync(lot.Id.Value, DispositionDecision.Rework,
            "REPAIR", "quality-manager", new IdempotencyKey(Unique("disposition")), "hash-2", DateTimeOffset.UtcNow);

        Assert.False(first.IsReplay);
        Assert.True(replay.IsReplay);
        Assert.Equal("Failed", completed.Status);
        Assert.Equal("Rework", disposition.Decision);
    }

    [PostgresFact]
    public async Task AuditInsertFailureRollsBackBusinessWrite()
    {
        await using var dataSource = CreateDataSource();
        await new PostgresMigrator(dataSource).MigrateAsync();
        var order = new ProductionOrder(EntityId.New(), Unique("ORDER"), "Customer", "M1", "BLACK", 1);
        await new ProductionOrderRepository(dataSource).InsertAsync(order);
        var duplicateId = Unique("audit");
        var audit = new AuditEventSnapshot(duplicateId, "actor", "station", "shift", "correlation",
            "ShipmentCreated", "Shipment", "existing", null, null, DateTimeOffset.UtcNow);
        await new AuditEventRepository(dataSource).AppendAsync(audit);
        var shipment = new Shipment(EntityId.New(), order.Id, "Customer", 1, "DELIVERY-1");

        await Assert.ThrowsAsync<PostgresException>(() => new ShipmentRepository(dataSource).CreateAsync(
            shipment, DateTimeOffset.UtcNow, default, _ => audit with { EntityId = shipment.Id.Value }));

        await using var count = dataSource.CreateCommand("SELECT count(*) FROM shipments WHERE id=$1");
        count.Parameters.AddWithValue(shipment.Id.Value);
        Assert.Equal(0L, await count.ExecuteScalarAsync());
    }

    [PostgresFact]
    public async Task CompletingInspectionLotReplayWritesOneAuditEvent()
    {
        await using var dataSource = CreateDataSource();
        await new PostgresMigrator(dataSource).MigrateAsync();
        var order = new ProductionOrder(EntityId.New(), Unique("ORDER"), "Customer", "M1", "BLACK", 1);
        await new ProductionOrderRepository(dataSource).InsertAsync(order);
        var unit = new ProductionUnit(EntityId.New(), order.Id);
        await new ProductionUnitRepository(dataSource).InsertAsync(unit);
        var lot = new InspectionLot(EntityId.New(), order.Id, "OQC", "ONE", [unit.Id]);
        var repository = new InspectionRepository(dataSource);
        await repository.CreateLotAsync(lot, DateTimeOffset.UtcNow);
        await repository.AddResultAsync(lot.Id.Value, unit.Id.Value, "APPEARANCE", InspectionOutcome.Passed,
            "", "OP-1", "", new IdempotencyKey(Unique("result")), "result-hash", DateTimeOffset.UtcNow);
        var key = new IdempotencyKey(Unique("complete"));
        var auditId = Unique("audit");
        AuditEventSnapshot Factory(InspectionLotSnapshot value) => new(auditId, "actor", "station", "shift",
            "correlation", "InspectionLotCompleted", "InspectionLot", value.Id, null, null, DateTimeOffset.UtcNow);

        var first = await repository.CompleteLotAsync(lot.Id.Value, 0, key, "same-hash", DateTimeOffset.UtcNow,
            default, Factory);
        var replay = await repository.CompleteLotAsync(lot.Id.Value, 0, key, "same-hash", DateTimeOffset.UtcNow,
            default, Factory);

        await using var count = dataSource.CreateCommand("SELECT count(*) FROM audit_events WHERE id=$1");
        count.Parameters.AddWithValue(auditId);
        Assert.False(first.IsReplay);
        Assert.True(replay.IsReplay);
        Assert.Equal(1L, await count.ExecuteScalarAsync());
    }

    [PostgresFact]
    public async Task ShipmentConfirmationKeepsMismatchPending()
    {
        await using var dataSource = CreateDataSource();
        await new PostgresMigrator(dataSource).MigrateAsync();
        var order = new ProductionOrder(EntityId.New(), Unique("ORDER"), "Customer", "M1", "BLACK", 2);
        await new ProductionOrderRepository(dataSource).InsertAsync(order);
        var shipment = new Shipment(EntityId.New(), order.Id, "Customer", 2, "DELIVERY-1");
        var repository = new ShipmentRepository(dataSource);
        await repository.CreateAsync(shipment, DateTimeOffset.UtcNow);

        var exception = await Assert.ThrowsAsync<PersistenceBusinessException>(() => repository.ConfirmAsync(
            shipment.Id.Value, "warehouse-supervisor", new IdempotencyKey(Unique("confirm")), "hash", DateTimeOffset.UtcNow));

        Assert.Equal("SHIPMENT_STATE_CONFLICT", exception.Code);
    }

    [PostgresFact]
    public async Task ReworkStartsAtConfiguredOperationAndRequiresOnlyFollowingOperations()
    {
        await using var dataSource = CreateDataSource();
        await new PostgresMigrator(dataSource).MigrateAsync();
        var order = new ProductionOrder(EntityId.New(), Unique("ORDER"), "Customer", "M1", "BLACK", 1);
        var orders = new ProductionOrderRepository(dataSource);
        await orders.InsertAsync(order);
        await orders.UpdateStateAsync(order.Id.Value, ProductionOrderStatus.InProduction, 0);
        var unit = new ProductionUnit(EntityId.New(), order.Id);
        await new ProductionUnitRepository(dataSource).InsertAsync(unit);
        var route = new ManufacturingRoute(EntityId.New(), order.Id, "Repair", RouteType.Rework,
        [
            new ManufacturingOperation { Id = "RW-1", Name = "Before start", Sequence = 1 },
            new ManufacturingOperation { Id = "RW-2", Name = "Start", Sequence = 2 },
            new ManufacturingOperation { Id = "RW-3", Name = "Finish", Sequence = 3 }
        ]);
        var configuration = new ManufacturingConfigurationRepository(dataSource);
        await configuration.InsertRouteAsync(route);
        var station = new Station(EntityId.New(), "Rework station", ["RW-1", "RW-2", "RW-3"]);
        await configuration.InsertStationAsync(station);
        var rework = new ReworkOrder(EntityId.New(), unit.Id, route.Id, "REPAIR", "RW-2", 1);
        var reworks = new ReworkOrderRepository(dataSource);
        await reworks.CreateAsync(rework);
        await reworks.ApproveAsync(rework.Id.Value, "quality-manager", new IdempotencyKey(Unique("approve")),
            Unique("hash"), DateTimeOffset.UtcNow);
        await reworks.ActivateAsync(rework.Id.Value, "production-supervisor", new IdempotencyKey(Unique("activate")),
            Unique("hash"), DateTimeOffset.UtcNow);
        var passes = new StationPassRepository(dataSource);

        await passes.PassAsync(unit.Id.Value, order.Id.Value, route.Id.Value, "RW-2", station.Id.Value,
            "operator", new IdempotencyKey(Unique("pass")), Unique("hash"), DateTimeOffset.UtcNow,
            rework.Id.Value, 1);
        var incomplete = await Assert.ThrowsAsync<PersistenceBusinessException>(() => reworks.CompleteAsync(
            rework.Id.Value, "production-supervisor", new IdempotencyKey(Unique("complete")), Unique("hash"),
            DateTimeOffset.UtcNow));
        await passes.PassAsync(unit.Id.Value, order.Id.Value, route.Id.Value, "RW-3", station.Id.Value,
            "operator", new IdempotencyKey(Unique("pass")), Unique("hash"), DateTimeOffset.UtcNow,
            rework.Id.Value, 1);
        var completed = await reworks.CompleteAsync(rework.Id.Value, "production-supervisor",
            new IdempotencyKey(Unique("complete")), Unique("hash"), DateTimeOffset.UtcNow);

        Assert.Equal("REWORK_ROUTE_INCOMPLETE", incomplete.Code);
        Assert.Equal("Completed", completed.Status);
    }

    [PostgresFact]
    public async Task ArchiveReadValidatesHashAndDatabaseRejectsMutation()
    {
        await using var dataSource = CreateDataSource();
        await new PostgresMigrator(dataSource).MigrateAsync();
        var order = new ProductionOrder(EntityId.New(), Unique("ORDER"), "Customer", "M1", "BLACK", 1);
        await new ProductionOrderRepository(dataSource).InsertAsync(order);
        var payload = "{\"value\":1}";
        var archive = new OrderArchiveSnapshot(EntityId.New(), order.Id, payload, DateTimeOffset.UtcNow, "archiver");
        await using var canonical = dataSource.CreateCommand("SELECT $1::jsonb::text");
        canonical.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Jsonb, payload);
        var canonicalPayload = (string)(await canonical.ExecuteScalarAsync())!;
        var canonicalHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPayload))).ToLowerInvariant();
        await using (var insert = dataSource.CreateCommand("""
            INSERT INTO order_archive_snapshots
                (id, order_id, payload_json, payload_hash, archived_at_utc, archived_by, idempotency_key, request_hash)
            VALUES ($1,$2,$3,$4,$5,$6,$7,$8)
            """))
        {
            insert.Parameters.AddWithValue(archive.Id.Value);
            insert.Parameters.AddWithValue(archive.OrderId.Value);
            insert.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Jsonb, archive.PayloadJson);
            insert.Parameters.AddWithValue(canonicalHash);
            insert.Parameters.AddWithValue(archive.ArchivedAtUtc);
            insert.Parameters.AddWithValue(archive.ArchivedBy);
            insert.Parameters.AddWithValue(Unique("archive"));
            insert.Parameters.AddWithValue("hash");
            await insert.ExecuteNonQueryAsync();
        }

        await using var update = dataSource.CreateCommand(
            "UPDATE order_archive_snapshots SET archived_by='tampered' WHERE id=$1");
        update.Parameters.AddWithValue(archive.Id.Value);
        var repository = new OrderArchiveRepository(dataSource,
            new ExtendedTraceabilityRepository(dataSource, new TraceabilityRepository(dataSource)));
        await using (var corrupt = dataSource.CreateCommand("""
            INSERT INTO order_archive_snapshots
                (id, order_id, payload_json, payload_hash, archived_at_utc, archived_by, idempotency_key, request_hash)
            SELECT $1,$2,$3,'invalid-hash',$4,$5,$6,$7
            WHERE false
            """))
        {
            corrupt.Parameters.AddWithValue(Unique("unused"));
            corrupt.Parameters.AddWithValue(order.Id.Value);
            corrupt.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Jsonb, payload);
            corrupt.Parameters.AddWithValue(DateTimeOffset.UtcNow);
            corrupt.Parameters.AddWithValue("archiver");
            corrupt.Parameters.AddWithValue(Unique("unused"));
            corrupt.Parameters.AddWithValue("hash");
            await corrupt.ExecuteNonQueryAsync();
        }
        Assert.Equal(1, JsonDocument.Parse((await repository.GetByOrderIdAsync(order.Id.Value))!.PayloadJson)
            .RootElement.GetProperty("value").GetInt32());
        var exception = await Assert.ThrowsAsync<PostgresException>(() => update.ExecuteNonQueryAsync());

        Assert.Equal(PostgresErrorCodes.ObjectNotInPrerequisiteState, exception.SqlState);
    }

    private static NpgsqlDataSource CreateDataSource() =>
        NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("BARTENDER_TEST_POSTGRES")!);
    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";
}
