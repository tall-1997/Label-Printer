using BarTenderPrinter.Domain.Common;
using BarTenderPrinter.Domain.Numbering;
using BarTenderPrinter.Domain.Orders;
using BarTenderPrinter.Domain.Packaging;
using BarTenderPrinter.Domain.Routing;
using Npgsql;
using Xunit;

namespace BarTenderPrinter.Persistence.Tests;

public sealed class PostgresRepositoryTests
{
    [PostgresFact]
    public async Task MigrationIsIdempotentAndCreatesCurrentVersion()
    {
        await using var dataSource = CreateDataSource();
        var migrator = new PostgresMigrator(dataSource);

        await migrator.MigrateAsync();
        await migrator.MigrateAsync();

        await using var command = dataSource.CreateCommand("SELECT max(version) FROM schema_migrations");
        Assert.Equal(9, Convert.ToInt32(await command.ExecuteScalarAsync()));
    }

    [PostgresFact]
    public async Task ConcurrentNumberAllocationReturnsSingleValueForSameKey()
    {
        await using var dataSource = CreateDataSource();
        await new PostgresMigrator(dataSource).MigrateAsync();
        await using (var cleanup = dataSource.CreateCommand(
            "UPDATE print_jobs SET state='Failed' WHERE state IN ('Received', 'Submitting')"))
            await cleanup.ExecuteNonQueryAsync();
        var orderId = EntityId.New();
        await new ProductionOrderRepository(dataSource).InsertAsync(CreateOrder(orderId));
        var range = new NumberRange(EntityId.New(), orderId, NumberType.SerialNumber, "SN", NumberDatePattern.None, 1, 100, numericWidth: 4);
        var repository = new NumberRangeRepository(dataSource);
        await repository.InsertAsync(range);

        var key = new IdempotencyKey($"allocation-{Guid.NewGuid():N}");
        var now = DateTimeOffset.UtcNow;
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ =>
            repository.AllocateAsync(range.Id.Value, key, "same-request", "station-1", "operator-1", now)));

        Assert.Single(results.Select(result => result.Value).Distinct());
        Assert.Equal(1, results.Count(result => !result.IsReplay));
    }

    [PostgresFact]
    public async Task PackagingBindingRejectsSecondActiveParent()
    {
        await using var dataSource = CreateDataSource();
        await new PostgresMigrator(dataSource).MigrateAsync();
        var orderId = EntityId.New();
        await new ProductionOrderRepository(dataSource).InsertAsync(CreateOrder(orderId));
        var repository = new PackagingRepository(dataSource);
        var child = new PackagingUnit(EntityId.New(), orderId, PackagingUnitType.Body, Unique("BODY"), "M1", "BLACK", 0);
        var firstParent = new PackagingUnit(EntityId.New(), orderId, PackagingUnitType.ColorBox, Unique("BOX"), "M1", "BLACK", 1);
        var secondParent = new PackagingUnit(EntityId.New(), orderId, PackagingUnitType.ColorBox, Unique("BOX"), "M1", "BLACK", 1);
        await repository.InsertUnitAsync(child);
        await repository.InsertUnitAsync(firstParent);
        await repository.InsertUnitAsync(secondParent);
        await repository.BindPackagingAsync(firstParent.Id.Value, child.Id.Value, 0, "operator-1", DateTimeOffset.UtcNow);

        var exception = await Assert.ThrowsAsync<PersistenceBusinessException>(() =>
            repository.BindPackagingAsync(secondParent.Id.Value, child.Id.Value, 0, "operator-1", DateTimeOffset.UtcNow));

        Assert.Equal("PACKAGING_BINDING_CONFLICT", exception.Code);
    }

    [PostgresFact]
    public async Task StationPassSameHashReplaysAndDifferentHashConflicts()
    {
        await using var dataSource = CreateDataSource();
        await new PostgresMigrator(dataSource).MigrateAsync();
        var orderId = EntityId.New();
        await new ProductionOrderRepository(dataSource).InsertAsync(CreateOrder(orderId));
        var unit = new BarTenderPrinter.Domain.Production.ProductionUnit(EntityId.New(), orderId);
        await new ProductionUnitRepository(dataSource).InsertAsync(unit);
        var key = new IdempotencyKey($"pass-{Guid.NewGuid():N}");
        var record = new StationPassRecord(EntityId.New(), unit.Id, orderId, EntityId.New(), "OP-10",
            EntityId.New(), "operator-1", DateTimeOffset.UtcNow, key, "", 0);
        var repository = new StationPassRepository(dataSource);

        Assert.True(await repository.InsertAsync(record, "hash-1"));
        Assert.False(await repository.InsertAsync(record with { Id = EntityId.New() }, "hash-1"));
        var conflict = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.InsertAsync(record with { Id = EntityId.New() }, "hash-2"));

        Assert.Equal("IDEMPOTENCY_CONFLICT", conflict.Message);
    }

    [PostgresFact]
    public async Task PrintJobSameHashReplaysAndDifferentHashConflicts()
    {
        await using var dataSource = CreateDataSource();
        await new PostgresMigrator(dataSource).MigrateAsync();
        var repository = new PrintJobRepository(dataSource);
        var key = $"print-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        var snapshot = new PrintJobSnapshot(Guid.NewGuid().ToString("N"), key, "Body", "template-1", "v1",
            "Received", "hash-1", "{}", null, 0, now, now);

        var first = await repository.RegisterAsync(snapshot);
        var replay = await repository.RegisterAsync(snapshot with { JobId = Guid.NewGuid().ToString("N") });
        var conflict = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.RegisterAsync(snapshot with { JobId = Guid.NewGuid().ToString("N"), RequestHash = "hash-2" }));

        Assert.Equal(first.JobId, replay.JobId);
        Assert.Equal("IDEMPOTENCY_CONFLICT", conflict.Message);
    }

    [PostgresFact]
    public async Task ConcurrentPrintJobClaimsReturnDifferentJobs()
    {
        await using var dataSource = CreateDataSource();
        await new PostgresMigrator(dataSource).MigrateAsync();
        await using (var cleanup = dataSource.CreateCommand(
            "UPDATE print_jobs SET state='Failed' WHERE state IN ('Received', 'Submitting')"))
            await cleanup.ExecuteNonQueryAsync();
        var repository = new PrintJobRepository(dataSource);
        var now = DateTimeOffset.UtcNow;
        var first = await repository.RegisterAsync(CreatePrintJob(now));
        var second = await repository.RegisterAsync(CreatePrintJob(now.AddMilliseconds(1)));

        var claims = await Task.WhenAll(
            repository.ClaimNextAsync("station-1", "operator-1", new IdempotencyKey($"claim-{Guid.NewGuid():N}"),
                "request-1", now.AddSeconds(1)),
            repository.ClaimNextAsync("station-2", "operator-2", new IdempotencyKey($"claim-{Guid.NewGuid():N}"),
                "request-2", now.AddSeconds(1)));

        Assert.Equal(2, claims.Select(result => result.Job?.JobId).Distinct().Count());
        Assert.Equal(new[] { first.JobId, second.JobId }.Order(),
            claims.Select(result => result.Job!.JobId).Order());
    }

    [PostgresFact]
    public async Task PrintJobClaimSkipsFrozenAndQualityHeldPackaging()
    {
        await using var dataSource = CreateDataSource();
        await new PostgresMigrator(dataSource).MigrateAsync();
        await using (var cleanup = dataSource.CreateCommand(
            "UPDATE print_jobs SET state='Failed' WHERE state IN ('Received', 'Submitting')"))
            await cleanup.ExecuteNonQueryAsync();
        var orderId = EntityId.New();
        await new ProductionOrderRepository(dataSource).InsertAsync(CreateOrder(orderId));
        var packaging = new PackagingRepository(dataSource);
        var frozen = new PackagingUnit(EntityId.New(), orderId, PackagingUnitType.Carton, Unique("FROZEN"), "M1", "BLACK", 1);
        var held = new PackagingUnit(EntityId.New(), orderId, PackagingUnitType.Carton, Unique("HELD"), "M1", "BLACK", 1);
        await packaging.InsertUnitAsync(frozen);
        await packaging.InsertUnitAsync(held);
        await using (var freeze = dataSource.CreateCommand("UPDATE packaging_units SET status='Frozen' WHERE id=$1"))
        {
            freeze.Parameters.AddWithValue(frozen.Id.Value);
            await freeze.ExecuteNonQueryAsync();
        }
        var unit = new BarTenderPrinter.Domain.Production.ProductionUnit(EntityId.New(), orderId);
        await new ProductionUnitRepository(dataSource).InsertAsync(unit);
        var lot = new BarTenderPrinter.Domain.Quality.InspectionLot(EntityId.New(), orderId, "OQC", "ONE", [unit.Id]);
        await new InspectionRepository(dataSource).CreateLotAsync(lot, DateTimeOffset.UtcNow);
        await using (var hold = dataSource.CreateCommand(
            "INSERT INTO packaging_quality_holds(lot_id, packaging_unit_id, previous_status) VALUES ($1,$2,'Closed')"))
        {
            hold.Parameters.AddWithValue(lot.Id.Value);
            hold.Parameters.AddWithValue(held.Id.Value);
            await hold.ExecuteNonQueryAsync();
        }
        var repository = new PrintJobRepository(dataSource);
        var now = DateTimeOffset.UtcNow;
        await repository.RegisterAsync(CreatePrintJob(now) with { TracePackagingUnitId = frozen.Id.Value });
        await repository.RegisterAsync(CreatePrintJob(now.AddMilliseconds(1)) with { TracePackagingUnitId = held.Id.Value });

        var claim = await repository.ClaimNextAsync("station-1", "operator-1",
            new IdempotencyKey(Unique("claim")), Unique("hash"), now.AddSeconds(1));

        Assert.Null(claim.Job);
    }

    [PostgresFact]
    public async Task OrderVersionConditionRejectsStaleUpdate()
    {
        await using var dataSource = CreateDataSource();
        await new PostgresMigrator(dataSource).MigrateAsync();
        var order = CreateOrder(EntityId.New());
        var repository = new ProductionOrderRepository(dataSource);
        await repository.InsertAsync(order);
        await repository.UpdateStateAsync(order.Id.Value, ProductionOrderStatus.Published, 0);

        await Assert.ThrowsAsync<PersistenceConcurrencyException>(() =>
            repository.UpdateStateAsync(order.Id.Value, ProductionOrderStatus.Paused, 0));
    }

    [PostgresFact]
    public async Task AuditEventPersistsActorCorrelationAndSnapshots()
    {
        await using var dataSource = CreateDataSource();
        await new PostgresMigrator(dataSource).MigrateAsync();
        var auditEvent = new AuditEventSnapshot(Guid.NewGuid().ToString("N"), "operator-1", "station-1",
            "shift-1", Guid.NewGuid().ToString("N"), "OrderPublished", "ProductionOrder",
            Guid.NewGuid().ToString("N"), "{\"status\":\"Draft\"}", "{\"status\":\"Published\"}", DateTimeOffset.UtcNow);

        await new AuditEventRepository(dataSource).AppendAsync(auditEvent);

        await using var command = dataSource.CreateCommand(
            "SELECT actor_id, correlation_id, before_json::text, after_json::text FROM audit_events WHERE id=$1");
        command.Parameters.AddWithValue(auditEvent.Id);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(auditEvent.ActorId, reader.GetString(0));
        Assert.Equal(auditEvent.CorrelationId, reader.GetString(1));
        Assert.Contains("Draft", reader.GetString(2));
        Assert.Contains("Published", reader.GetString(3));
    }

    private static NpgsqlDataSource CreateDataSource() =>
        NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("BARTENDER_TEST_POSTGRES")!);

    private static ProductionOrder CreateOrder(EntityId id) => new(
        id, Unique("ORDER"), "Customer", "M1", "BLACK", 100);

    private static PrintJobSnapshot CreatePrintJob(DateTimeOffset now) => new(
        Guid.NewGuid().ToString("N"), $"print-{Guid.NewGuid():N}", "Carton", "template-1", "v1",
        "Received", Guid.NewGuid().ToString("N"), "{}", null, 0, now, now);

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";
}
