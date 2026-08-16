using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BarTenderPrinter.Domain.Common;
using BarTenderPrinter.Domain.Numbering;
using BarTenderPrinter.Domain.Orders;
using BarTenderPrinter.Domain.Packaging;
using BarTenderPrinter.Domain.Production;
using BarTenderPrinter.Domain.Routing;
using Npgsql;
using NpgsqlTypes;
using static BarTenderPrinter.Persistence.PostgresRepositoryHelpers;

namespace BarTenderPrinter.Persistence;

public sealed class MesCoreRepository(NpgsqlDataSource dataSource)
{
    public async Task<IReadOnlyList<NumberAllocationHistorySnapshot>> GetNumberHistoryAsync(string allocationId,
        CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT id, allocation_id, previous_status, next_status, reason_code, actor_id, station_id, changed_at_utc
            FROM number_allocation_status_history WHERE allocation_id=$1 ORDER BY changed_at_utc,id
            """);
        Add(command, Required(allocationId, nameof(allocationId)));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<NumberAllocationHistorySnapshot>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new NumberAllocationHistorySnapshot(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), ReadUtc(reader, 7)));
        return result;
    }

    public async Task<ProductionOrderSnapshot> TransitionOrderAsync(string orderId, ProductionOrderStatus target,
        long expectedVersion, IdempotencyKey key, string requestHash, DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default,
        Func<ProductionOrderSnapshot, ProductionOrderSnapshot, AuditEventSnapshot>? auditFactory = null)
    {
        ValidateUtc(changedAtUtc, nameof(changedAtUtc));
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockAsync(connection, transaction, key.Value, cancellationToken);
        var replay = await ReplayAsync<ProductionOrderSnapshot>(connection, transaction, key.Value, requestHash,
            "OrderTransition", cancellationToken);
        if (replay != null) return await CommitReplayAsync(transaction, replay, cancellationToken);
        var before = await ReadOrderAsync(connection, transaction, Required(orderId, nameof(orderId)), true, cancellationToken)
            ?? throw new KeyNotFoundException("生产订单不存在。");
        if (before.Version != expectedVersion) throw new PersistenceConcurrencyException("ProductionOrder", orderId);
        ValidateOrderTransition(before, target, changedAtUtc);
        await ExecuteAsync(connection, transaction,
            "UPDATE production_orders SET status=$1, version=version+1 WHERE id=$2 AND version=$3",
            cancellationToken, target.ToString(), orderId, expectedVersion);
        var result = before with { Status = target.ToString(), Version = before.Version + 1 };
        await SaveCommandAsync(connection, transaction, key.Value, requestHash, "OrderTransition", orderId, result,
            changedAtUtc, cancellationToken);
        await TransactionalAudit.AppendAsync(connection, transaction, auditFactory?.Invoke(before, result), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<ProductionUnitSnapshot> CreateProductionUnitAsync(string orderId,
        IReadOnlyDictionary<NumberType, string> allocationIds, IdempotencyKey key, string requestHash,
        DateTimeOffset createdAtUtc, CancellationToken cancellationToken = default,
        Func<ProductionUnitSnapshot, AuditEventSnapshot>? auditFactory = null)
    {
        ValidateUtc(createdAtUtc, nameof(createdAtUtc));
        if (allocationIds == null || allocationIds.Count == 0)
            throw new ArgumentException("生产单元至少需要一个标识。", nameof(allocationIds));
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockAsync(connection, transaction, key.Value, cancellationToken);
        var replay = await ReplayAsync<ProductionUnitSnapshot>(connection, transaction, key.Value, requestHash,
            "ProductionUnitCreate", cancellationToken);
        if (replay != null) return await CommitReplayAsync(transaction, replay, cancellationToken);
        var order = await ReadOrderAsync(connection, transaction, Required(orderId, nameof(orderId)), true, cancellationToken)
            ?? throw new KeyNotFoundException("生产订单不存在。");
        if (order.Status is "Closed")
            throw new PersistenceBusinessException("ORDER_UNAVAILABLE", "已关闭订单无法创建生产单元。");

        var identifiers = new Dictionary<string, string>();
        var allocations = new List<(string Id, string Status)>();
        foreach (var item in allocationIds)
        {
            await using var command = new NpgsqlCommand("""
                SELECT a.value, a.status, r.order_id, r.number_type
                FROM number_allocations a JOIN number_ranges r ON r.id=a.range_id
                WHERE a.id=$1 FOR UPDATE OF a
                """, connection, transaction);
            Add(command, Required(item.Value, "allocationId"));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) throw new KeyNotFoundException("号码分配记录不存在。");
            var value = reader.GetString(0);
            var status = reader.GetString(1);
            var allocationOrderId = reader.GetString(2);
            var storedType = reader.GetString(3);
            if (allocationOrderId != orderId || storedType != item.Key.ToString())
                throw new PersistenceBusinessException("IDENTIFIER_ORDER_MISMATCH", "号码类型或订单与生产单元不匹配。");
            if (status != NumberAllocationStatus.Reserved.ToString())
                throw new PersistenceBusinessException("NUMBER_STATUS_CONFLICT", "只有已保留号码可以绑定生产单元。");
            identifiers.Add(item.Key.ToString(), value);
            allocations.Add((item.Value, status));
        }

        var result = new ProductionUnitSnapshot(EntityId.New().Value, orderId,
            ProductionUnitStatus.Active.ToString(), "", 0);
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO production_units(id, order_id, status, current_operation_id, identifiers_json, version)
            VALUES ($1,$2,'Active','',$3,0)
            """, connection, transaction))
        {
            Add(insert, result.Id, orderId);
            insert.Parameters.AddWithValue(NpgsqlDbType.Jsonb, JsonSerializer.Serialize(identifiers));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (var allocation in allocations)
        {
            await ExecuteAsync(connection, transaction,
                "UPDATE number_allocations SET status='Assigned', unit_id=$1 WHERE id=$2", cancellationToken,
                result.Id, allocation.Id);
            await InsertNumberHistoryAsync(connection, transaction, allocation.Id, allocation.Status, "Assigned",
                "PRODUCTION_UNIT_BOUND", "system", "system", $"{key.Value}:{allocation.Id}", requestHash,
                createdAtUtc, cancellationToken);
        }
        await SaveCommandAsync(connection, transaction, key.Value, requestHash, "ProductionUnitCreate", result.Id,
            result, createdAtUtc, cancellationToken);
        await TransactionalAudit.AppendAsync(connection, transaction, auditFactory?.Invoke(result), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<ManufacturingRouteSnapshot> CreateRouteAsync(ManufacturingRoute route, IdempotencyKey key,
        string requestHash, DateTimeOffset createdAtUtc, CancellationToken cancellationToken = default,
        Func<ManufacturingRouteSnapshot, AuditEventSnapshot>? auditFactory = null)
    {
        ArgumentNullException.ThrowIfNull(route);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockAsync(connection, transaction, key.Value, cancellationToken);
        var replay = await ReplayAsync<ManufacturingRouteSnapshot>(connection, transaction, key.Value, requestHash,
            "RouteCreate", cancellationToken);
        if (replay != null) return await CommitReplayAsync(transaction, replay, cancellationToken);
        await ExecuteAsync(connection, transaction,
            "INSERT INTO manufacturing_routes(id, order_id, name, route_type) VALUES ($1,$2,$3,$4)", cancellationToken,
            route.Id.Value, route.OrderId.Value, route.Name, route.Type.ToString());
        foreach (var operation in route.Operations)
            await ExecuteAsync(connection, transaction,
                "INSERT INTO manufacturing_operations(route_id, operation_id, name, sequence) VALUES ($1,$2,$3,$4)",
                cancellationToken, route.Id.Value, operation.Id, operation.Name, operation.Sequence);
        var result = new ManufacturingRouteSnapshot(route.Id.Value, route.OrderId.Value, route.Name,
            route.Type.ToString(), route.Operations.Select(x => new ManufacturingOperationSnapshot(x.Id, x.Name, x.Sequence)).ToArray());
        await SaveCommandAsync(connection, transaction, key.Value, requestHash, "RouteCreate", result.Id, result,
            createdAtUtc, cancellationToken);
        await TransactionalAudit.AppendAsync(connection, transaction, auditFactory?.Invoke(result), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<StationSnapshot> CreateStationAsync(Station station, IdempotencyKey key, string requestHash,
        DateTimeOffset createdAtUtc, CancellationToken cancellationToken = default,
        Func<StationSnapshot, AuditEventSnapshot>? auditFactory = null)
    {
        ArgumentNullException.ThrowIfNull(station);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockAsync(connection, transaction, key.Value, cancellationToken);
        var replay = await ReplayAsync<StationSnapshot>(connection, transaction, key.Value, requestHash,
            "StationCreate", cancellationToken);
        if (replay != null) return await CommitReplayAsync(transaction, replay, cancellationToken);
        var operationIds = station.QualifiedOperationIds.Order(StringComparer.Ordinal).ToArray();
        await using (var validate = new NpgsqlCommand(
            "SELECT count(DISTINCT operation_id) FROM manufacturing_operations WHERE operation_id=ANY($1)", connection, transaction))
        {
            validate.Parameters.AddWithValue(operationIds);
            if (Convert.ToInt32(await validate.ExecuteScalarAsync(cancellationToken)) != operationIds.Length)
                throw new PersistenceBusinessException("OPERATION_NOT_FOUND", "一个或多个工序不存在。");
        }
        await ExecuteAsync(connection, transaction, "INSERT INTO stations(id, name) VALUES ($1,$2)", cancellationToken,
            station.Id.Value, station.Name);
        foreach (var operationId in operationIds)
            await ExecuteAsync(connection, transaction,
                "INSERT INTO station_qualifications(station_id, operation_id) VALUES ($1,$2)", cancellationToken,
                station.Id.Value, operationId);
        var result = new StationSnapshot(station.Id.Value, station.Name, operationIds);
        await SaveCommandAsync(connection, transaction, key.Value, requestHash, "StationCreate", result.Id, result,
            createdAtUtc, cancellationToken);
        await TransactionalAudit.AppendAsync(connection, transaction, auditFactory?.Invoke(result), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<PackagingUnitSnapshot> CreatePackagingUnitAsync(PackagingUnit unit, IdempotencyKey key,
        string requestHash, DateTimeOffset createdAtUtc, CancellationToken cancellationToken = default,
        Func<PackagingUnitSnapshot, AuditEventSnapshot>? auditFactory = null)
    {
        ArgumentNullException.ThrowIfNull(unit);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockAsync(connection, transaction, key.Value, cancellationToken);
        var replay = await ReplayAsync<PackagingUnitSnapshot>(connection, transaction, key.Value, requestHash,
            "PackagingUnitCreate", cancellationToken);
        if (replay != null) return await CommitReplayAsync(transaction, replay, cancellationToken);
        if (unit.ProductionUnitId.HasValue)
        {
            await using var check = new NpgsqlCommand(
                "SELECT EXISTS(SELECT 1 FROM production_units WHERE id=$1 AND order_id=$2 AND status IN ('Active','Completed'))",
                connection, transaction);
            Add(check, unit.ProductionUnitId.Value.Value, unit.OrderId.Value);
            if (!(bool)(await check.ExecuteScalarAsync(cancellationToken) ?? false))
                throw new PersistenceBusinessException("UNIT_UNAVAILABLE", "机身关联生产单元不存在、订单不匹配或状态无效。");
        }
        await ExecuteAsync(connection, transaction, """
            INSERT INTO packaging_units(id, order_id, unit_type, code, product_model, color, capacity, status, version, production_unit_id)
            VALUES ($1,$2,$3,$4,$5,$6,$7,$8,0,$9)
            """, cancellationToken, unit.Id.Value, unit.OrderId.Value, unit.Type.ToString(), unit.Code,
            unit.ProductModel, unit.Color, unit.Capacity, unit.Status.ToString(), DbValue(unit.ProductionUnitId?.Value));
        var result = new PackagingUnitSnapshot(unit.Id.Value, unit.OrderId.Value, unit.Type.ToString(), unit.Code,
            unit.ProductModel, unit.Color, unit.Capacity, unit.Status.ToString(), 0, unit.ProductionUnitId?.Value);
        if (unit.Type == PackagingUnitType.Body)
            await RegisterPackagingLabelAsync(connection, transaction, result, Array.Empty<string>(), createdAtUtc, cancellationToken);
        await SaveCommandAsync(connection, transaction, key.Value, requestHash, "PackagingUnitCreate", result.Id, result,
            createdAtUtc, cancellationToken);
        await TransactionalAudit.AppendAsync(connection, transaction, auditFactory?.Invoke(result), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<NumberAllocationStatusSnapshot> ChangeNumberStatusAsync(string allocationId,
        NumberAllocationStatus target, string reasonCode, string actorId, string stationId, IdempotencyKey key,
        string requestHash, DateTimeOffset changedAtUtc, CancellationToken cancellationToken = default,
        Func<NumberAllocationStatusSnapshot, AuditEventSnapshot>? auditFactory = null)
    {
        ValidateUtc(changedAtUtc, nameof(changedAtUtc));
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockAsync(connection, transaction, key.Value, cancellationToken);
        var replay = await ReplayAsync<NumberAllocationStatusSnapshot>(connection, transaction, key.Value, requestHash,
            "NumberStatusChange", cancellationToken);
        if (replay != null) return await CommitReplayAsync(transaction, replay with { IsReplay = true }, cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT value, status, unit_id FROM number_allocations WHERE id=$1 FOR UPDATE", connection, transaction);
        Add(command, Required(allocationId, nameof(allocationId)));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new KeyNotFoundException("号码分配记录不存在。");
        var value = reader.GetString(0);
        var previous = Enum.Parse<NumberAllocationStatus>(reader.GetString(1));
        var unitId = reader.GetString(2);
        await reader.CloseAsync();
        if (!AllowedNumberTransition(previous, target))
            throw new PersistenceBusinessException("NUMBER_STATUS_CONFLICT", $"号码状态不能从 {previous} 变更为 {target}。");
        await ExecuteAsync(connection, transaction, "UPDATE number_allocations SET status=$1 WHERE id=$2",
            cancellationToken, target.ToString(), allocationId);
        await InsertNumberHistoryAsync(connection, transaction, allocationId, previous.ToString(), target.ToString(),
            Required(reasonCode, nameof(reasonCode)), Required(actorId, nameof(actorId)), Required(stationId, nameof(stationId)),
            key.Value, requestHash, changedAtUtc, cancellationToken);
        var result = new NumberAllocationStatusSnapshot(allocationId, value, target.ToString(), unitId, false);
        await SaveCommandAsync(connection, transaction, key.Value, requestHash, "NumberStatusChange", allocationId,
            result, changedAtUtc, cancellationToken);
        await TransactionalAudit.AppendAsync(connection, transaction, auditFactory?.Invoke(result), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<WeightRuleSnapshot> CreateWeightRuleAsync(WeightRule rule, IdempotencyKey key,
        string requestHash, DateTimeOffset createdAtUtc, CancellationToken cancellationToken = default,
        Func<WeightRuleSnapshot, AuditEventSnapshot>? auditFactory = null)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockAsync(connection, transaction, key.Value, cancellationToken);
        var replay = await ReplayAsync<WeightRuleSnapshot>(connection, transaction, key.Value, requestHash,
            "WeightRuleCreate", cancellationToken);
        if (replay != null) return await CommitReplayAsync(transaction, replay, cancellationToken);
        var result = new WeightRuleSnapshot(rule.Id.Value, rule.OrderId.Value, rule.PackagingUnitType,
            rule.MinimumWeight, rule.MaximumWeight, rule.Unit, 0);
        await ExecuteAsync(connection, transaction, """
            INSERT INTO weight_rules(id, order_id, packaging_unit_type, minimum_weight, maximum_weight, unit, version)
            VALUES ($1,$2,$3,$4,$5,$6,0)
            """, cancellationToken, result.Id, result.OrderId, result.PackagingUnitType, result.MinimumWeight,
            result.MaximumWeight, result.Unit);
        await SaveCommandAsync(connection, transaction, key.Value, requestHash, "WeightRuleCreate", result.Id, result,
            createdAtUtc, cancellationToken);
        await TransactionalAudit.AppendAsync(connection, transaction, auditFactory?.Invoke(result), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<WeightMeasurementSnapshot> RecordWeightAsync(string packagingUnitId, decimal weight,
        string unit, string deviceId, bool isSimulated, IdempotencyKey key, string requestHash,
        DateTimeOffset measuredAtUtc, CancellationToken cancellationToken = default,
        Func<WeightMeasurementSnapshot, AuditEventSnapshot>? auditFactory = null)
    {
        ValidateUtc(measuredAtUtc, nameof(measuredAtUtc));
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockAsync(connection, transaction, key.Value, cancellationToken);
        var replay = await ReplayAsync<WeightMeasurementSnapshot>(connection, transaction, key.Value, requestHash,
            "WeightMeasurement", cancellationToken);
        if (replay != null) return await CommitReplayAsync(transaction, replay with { IsReplay = true }, cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT r.id, r.minimum_weight, r.maximum_weight, r.unit
            FROM packaging_units p JOIN weight_rules r
              ON r.order_id=p.order_id AND r.packaging_unit_type=p.unit_type
            WHERE p.id=$1
            """, connection, transaction);
        Add(command, Required(packagingUnitId, nameof(packagingUnitId)));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new PersistenceBusinessException("WEIGHT_RULE_NOT_FOUND", "包装单元不存在或尚未配置称重规则。");
        var ruleId = reader.GetString(0);
        var minimum = reader.GetDecimal(1);
        var maximum = reader.GetDecimal(2);
        var configuredUnit = reader.GetString(3);
        await reader.CloseAsync();
        if (!string.Equals(configuredUnit, Required(unit, nameof(unit)), StringComparison.OrdinalIgnoreCase))
            throw new PersistenceBusinessException("WEIGHT_UNIT_MISMATCH", "称重单位与规则不一致。");
        if (weight < 0) throw new ArgumentOutOfRangeException(nameof(weight));
        var outcome = weight >= minimum && weight <= maximum ? "Passed" : "Failed";
        var result = new WeightMeasurementSnapshot(EntityId.New().Value, packagingUnitId, ruleId, weight,
            configuredUnit, Required(deviceId, nameof(deviceId)), isSimulated, outcome, minimum, maximum,
            measuredAtUtc, false);
        await ExecuteAsync(connection, transaction, """
            INSERT INTO weight_measurements(id, packaging_unit_id, rule_id, weight, unit, device_id, is_simulated,
                result, measured_at_utc, idempotency_key, request_hash)
            VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11)
            """, cancellationToken, result.Id, result.PackagingUnitId, result.RuleId, result.Weight, result.Unit,
            result.DeviceId, result.IsSimulated, result.Result, result.MeasuredAtUtc, key.Value, requestHash);
        await SaveCommandAsync(connection, transaction, key.Value, requestHash, "WeightMeasurement", result.Id,
            result, measuredAtUtc, cancellationToken);
        await TransactionalAudit.AppendAsync(connection, transaction, auditFactory?.Invoke(result), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<IdentifierWriteTaskSnapshot> CreateWriteTaskAsync(string unitId, string[] allocationIds,
        string platform, string targetStationId, IdempotencyKey key, string requestHash, DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken = default,
        Func<IdentifierWriteTaskSnapshot, AuditEventSnapshot>? auditFactory = null)
    {
        if (allocationIds == null || allocationIds.Length == 0)
            throw new ArgumentException("写号任务至少需要一个号码。", nameof(allocationIds));
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockAsync(connection, transaction, key.Value, cancellationToken);
        var replay = await ReplayAsync<IdentifierWriteTaskSnapshot>(connection, transaction, key.Value, requestHash,
            "IdentifierWriteTaskCreate", cancellationToken);
        if (replay != null) return await CommitReplayAsync(transaction, replay with { IsReplay = true }, cancellationToken);
        var identifiers = new Dictionary<string, string>();
        foreach (var allocationId in allocationIds.Distinct(StringComparer.Ordinal))
        {
            await using var command = new NpgsqlCommand("""
                SELECT a.value, a.status, a.unit_id, r.number_type
                FROM number_allocations a JOIN number_ranges r ON r.id=a.range_id WHERE a.id=$1 FOR UPDATE OF a
                """, connection, transaction);
            Add(command, allocationId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) throw new KeyNotFoundException("号码分配记录不存在。");
            if (reader.GetString(1) != "Assigned" || reader.GetString(2) != unitId)
                throw new PersistenceBusinessException("NUMBER_STATUS_CONFLICT", "写号号码必须已分配给目标生产单元。");
            identifiers.Add(reader.GetString(3), reader.GetString(0));
        }
        var result = new IdentifierWriteTaskSnapshot(EntityId.New().Value, Required(unitId, nameof(unitId)),
            allocationIds.Distinct(StringComparer.Ordinal).ToArray(), JsonSerializer.Serialize(identifiers),
            Required(platform, nameof(platform)), Required(targetStationId, nameof(targetStationId)), "Pending", "", "",
            null, "", 0, createdAtUtc, createdAtUtc);
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO identifier_write_tasks(id, unit_id, allocation_ids, identifiers_json, platform,
                target_station_id, state, created_at_utc, updated_at_utc, idempotency_key, request_hash)
            VALUES ($1,$2,$3,$4,$5,$6,'Pending',$7,$7,$8,$9)
            """, connection, transaction))
        {
            Add(insert, result.Id, result.UnitId);
            insert.Parameters.AddWithValue(result.AllocationIds);
            insert.Parameters.AddWithValue(NpgsqlDbType.Jsonb, result.IdentifiersJson);
            Add(insert, result.Platform, result.TargetStationId, createdAtUtc, key.Value, requestHash);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        await SaveCommandAsync(connection, transaction, key.Value, requestHash, "IdentifierWriteTaskCreate",
            result.Id, result, createdAtUtc, cancellationToken);
        await TransactionalAudit.AppendAsync(connection, transaction, auditFactory?.Invoke(result), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<IdentifierWriteClaimResult> ClaimWriteTaskAsync(string stationId, string operatorId,
        string platform, IdempotencyKey key, string requestHash, DateTimeOffset claimedAtUtc,
        CancellationToken cancellationToken = default,
        Func<IdentifierWriteTaskSnapshot, AuditEventSnapshot>? auditFactory = null)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockAsync(connection, transaction, key.Value, cancellationToken);
        await using (var replayCommand = new NpgsqlCommand(
            "SELECT request_hash, task_id FROM identifier_write_claims WHERE idempotency_key=$1", connection, transaction))
        {
            Add(replayCommand, key.Value);
            await using var reader = await replayCommand.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                if (reader.GetString(0) != requestHash) throw IdempotencyConflict();
                var taskId = reader.IsDBNull(1) ? null : reader.GetString(1);
                await reader.CloseAsync();
                var replay = taskId == null ? null : await ReadWriteTaskAsync(connection, transaction, taskId, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new IdentifierWriteClaimResult(replay, true);
            }
        }
        var task = await SelectPendingWriteTaskAsync(connection, transaction, Required(stationId, nameof(stationId)),
            Required(platform, nameof(platform)), cancellationToken);
        if (task == null)
        {
            await ExecuteAsync(connection, transaction,
                "INSERT INTO identifier_write_claims(idempotency_key, request_hash, task_id, created_at_utc) VALUES ($1,$2,NULL,$3)",
                cancellationToken, key.Value, requestHash, claimedAtUtc);
            await transaction.CommitAsync(cancellationToken);
            return new IdentifierWriteClaimResult(null, false);
        }
        await ExecuteAsync(connection, transaction, """
            UPDATE identifier_write_tasks SET state='InProgress', claimed_by_station_id=$1,
                claimed_by_operator_id=$2, version=version+1, updated_at_utc=$3 WHERE id=$4
            """, cancellationToken, Required(stationId, nameof(stationId)), Required(operatorId, nameof(operatorId)),
            claimedAtUtc, task.Id);
        await ExecuteAsync(connection, transaction,
            "INSERT INTO identifier_write_claims(idempotency_key, request_hash, task_id, created_at_utc) VALUES ($1,$2,$3,$4)",
            cancellationToken, key.Value, requestHash, task.Id, claimedAtUtc);
        var result = (await ReadWriteTaskAsync(connection, transaction, task.Id, cancellationToken))!;
        await TransactionalAudit.AppendAsync(connection, transaction, auditFactory?.Invoke(result), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new IdentifierWriteClaimResult(result, false);
    }

    public async Task<IdentifierWriteTaskSnapshot> RecordWriteResultAsync(string taskId, string stationId,
        IdentifierWriteTaskState state, string resultJson, string diagnosticCode, IdempotencyKey key,
        string requestHash, DateTimeOffset completedAtUtc, CancellationToken cancellationToken = default,
        Func<IdentifierWriteTaskSnapshot, AuditEventSnapshot>? auditFactory = null)
    {
        if (state is not (IdentifierWriteTaskState.Succeeded or IdentifierWriteTaskState.Failed or IdentifierWriteTaskState.Uncertain))
            throw new ArgumentException("写号结果状态无效。", nameof(state));
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockAsync(connection, transaction, key.Value, cancellationToken);
        await using (var keyOwner = new NpgsqlCommand(
            "SELECT id FROM identifier_write_tasks WHERE result_idempotency_key=$1", connection, transaction))
        {
            Add(keyOwner, key.Value);
            if (await keyOwner.ExecuteScalarAsync(cancellationToken) is string ownerId && ownerId != taskId)
                throw IdempotencyConflict();
        }
        var task = await ReadWriteTaskAsync(connection, transaction, Required(taskId, nameof(taskId)), cancellationToken, true)
            ?? throw new KeyNotFoundException("写号任务不存在。");
        if (!string.Equals(task.ClaimedByStationId, Required(stationId, nameof(stationId)), StringComparison.Ordinal))
            throw new PersistenceBusinessException("WRITE_TASK_STATION_MISMATCH", "写号结果必须由领取工位提交。");
        if (!string.IsNullOrEmpty(task.ResultJson))
        {
            await using var replay = new NpgsqlCommand(
                "SELECT result_idempotency_key, result_request_hash FROM identifier_write_tasks WHERE id=$1", connection, transaction);
            Add(replay, taskId);
            await using var replayReader = await replay.ExecuteReaderAsync(cancellationToken);
            await replayReader.ReadAsync(cancellationToken);
            if (replayReader.GetString(0) != key.Value || replayReader.GetString(1) != requestHash)
                throw IdempotencyConflict();
            await replayReader.CloseAsync();
            await transaction.CommitAsync(cancellationToken);
            return task with { IsReplay = true };
        }
        if (task.State != "InProgress")
            throw new PersistenceBusinessException("WRITE_TASK_STATE_CONFLICT", "写号任务当前状态不接受结果回传。");
        await using (var update = new NpgsqlCommand("""
            UPDATE identifier_write_tasks SET state=$1, result_json=$2, diagnostic_code=$3,
                result_idempotency_key=$4, result_request_hash=$5, version=version+1, updated_at_utc=$6
            WHERE id=$7 AND version=$8
            """, connection, transaction))
        {
            Add(update, state.ToString());
            update.Parameters.AddWithValue(NpgsqlDbType.Jsonb, Required(resultJson, nameof(resultJson)));
            Add(update, diagnosticCode?.Trim() ?? "", key.Value, requestHash, completedAtUtc, taskId, task.Version);
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new PersistenceConcurrencyException("IdentifierWriteTask", taskId);
        }
        if (state == IdentifierWriteTaskState.Uncertain)
        {
            var statuses = new Dictionary<string, string>(StringComparer.Ordinal);
            await using (var readStatuses = new NpgsqlCommand(
                "SELECT id,status FROM number_allocations WHERE id=ANY($1) FOR UPDATE", connection, transaction))
            {
                readStatuses.Parameters.AddWithValue(task.AllocationIds);
                await using var statusReader = await readStatuses.ExecuteReaderAsync(cancellationToken);
                while (await statusReader.ReadAsync(cancellationToken))
                    statuses.Add(statusReader.GetString(0), statusReader.GetString(1));
            }
            await using var freeze = new NpgsqlCommand(
                "UPDATE number_allocations SET status='Frozen' WHERE id=ANY($1) AND status IN ('Reserved','Assigned')",
                connection, transaction);
            freeze.Parameters.AddWithValue(task.AllocationIds);
            await freeze.ExecuteNonQueryAsync(cancellationToken);
            foreach (var allocation in statuses.Where(x => x.Value is "Reserved" or "Assigned"))
                await InsertNumberHistoryAsync(connection, transaction, allocation.Key, allocation.Value, "Frozen",
                    "WRITE_RESULT_UNCERTAIN", task.ClaimedByOperatorId, stationId, $"{key.Value}:{allocation.Key}",
                    requestHash, completedAtUtc, cancellationToken);
            await ExecuteAsync(connection, transaction,
                "UPDATE production_units SET status='Frozen', version=version+1 WHERE id=$1 AND status='Active'",
                cancellationToken, task.UnitId);
        }
        var result = (await ReadWriteTaskAsync(connection, transaction, taskId, cancellationToken))!;
        await TransactionalAudit.AppendAsync(connection, transaction, auditFactory?.Invoke(result), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    internal static async Task RegisterPackagingLabelAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        PackagingUnitSnapshot unit, IReadOnlyList<string> childCodes, DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken, string? printIntentId = null)
    {
        var labelType = unit.UnitType;
        var fields = new Dictionary<string, string>
        {
            ["PACKAGE_CODE"] = unit.Code,
            ["ORDER_ID"] = unit.OrderId,
            ["PRODUCT_MODEL"] = unit.ProductModel,
            ["COLOR"] = unit.Color,
            ["QUANTITY"] = childCodes.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["CHILD_CODES"] = string.Join(",", childCodes)
        };
        var requestJson = JsonSerializer.Serialize(fields);
        var intentId = printIntentId ?? EntityId.New().Value;
        await using (var intent = new NpgsqlCommand("""
            INSERT INTO packaging_print_intents(id, packaging_unit_id, label_type, fields_json, created_at_utc)
            VALUES ($1,$2,$3,$4,$5) ON CONFLICT (packaging_unit_id) DO NOTHING
            """, connection, transaction))
        {
            Add(intent, intentId, unit.Id, labelType);
            intent.Parameters.AddWithValue(NpgsqlDbType.Jsonb, requestJson);
            Add(intent, createdAtUtc);
            await intent.ExecuteNonQueryAsync(cancellationToken);
        }
        var idempotencyKey = $"packaging-label:{unit.Id}:{labelType}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(requestJson))).ToLowerInvariant();
        await using var job = new NpgsqlCommand("""
            INSERT INTO print_jobs(job_id, idempotency_key, label_type, template_id, template_version, state,
                request_hash, request_json, result_json, version, created_at_utc, updated_at_utc,
                trace_order_id, trace_unit_id, trace_packaging_unit_id)
            VALUES ($1,$2,$3,$4,'v1','Received',$5,$6,NULL,0,$7,$7,$8,$9,$10)
            ON CONFLICT (idempotency_key) DO NOTHING
            """, connection, transaction);
        Add(job, EntityId.New().Value, idempotencyKey, labelType, $"{labelType.ToLowerInvariant()}-label", hash);
        job.Parameters.AddWithValue(NpgsqlDbType.Jsonb, requestJson);
        Add(job, createdAtUtc, unit.OrderId, DbValue(unit.ProductionUnitId), unit.Id);
        await job.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void ValidateOrderTransition(ProductionOrderSnapshot order, ProductionOrderStatus target,
        DateTimeOffset changedAtUtc)
    {
        var current = Enum.Parse<ProductionOrderStatus>(order.Status);
        var allowed = (current, target) is
            (ProductionOrderStatus.Draft, ProductionOrderStatus.Published) or
            (ProductionOrderStatus.Published, ProductionOrderStatus.InProduction) or
            (ProductionOrderStatus.Published, ProductionOrderStatus.Closed) or
            (ProductionOrderStatus.InProduction, ProductionOrderStatus.Paused) or
            (ProductionOrderStatus.InProduction, ProductionOrderStatus.Closed) or
            (ProductionOrderStatus.Paused, ProductionOrderStatus.InProduction) or
            (ProductionOrderStatus.Paused, ProductionOrderStatus.Closed);
        if (!allowed) throw new PersistenceBusinessException("ORDER_STATE_CONFLICT", $"订单状态不能从 {current} 变更为 {target}。");
        if (target == ProductionOrderStatus.InProduction &&
            ((order.ValidFromUtc.HasValue && changedAtUtc < order.ValidFromUtc) ||
             (order.ValidToUtc.HasValue && changedAtUtc > order.ValidToUtc)))
            throw new PersistenceBusinessException("ORDER_UNAVAILABLE", "订单当前不在有效期内。");
    }

    private static bool AllowedNumberTransition(NumberAllocationStatus current, NumberAllocationStatus target) =>
        (current, target) is
            (NumberAllocationStatus.Reserved, NumberAllocationStatus.Released) or
            (NumberAllocationStatus.Reserved, NumberAllocationStatus.Scrapped) or
            (NumberAllocationStatus.Reserved, NumberAllocationStatus.Frozen) or
            (NumberAllocationStatus.Assigned, NumberAllocationStatus.Scrapped) or
            (NumberAllocationStatus.Assigned, NumberAllocationStatus.Frozen) or
            (NumberAllocationStatus.Frozen, NumberAllocationStatus.Released) or
            (NumberAllocationStatus.Frozen, NumberAllocationStatus.Assigned);

    private static async Task<ProductionOrderSnapshot?> ReadOrderAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, string orderId, bool forUpdate, CancellationToken cancellationToken)
    {
        var sql = """
            SELECT o.id,o.order_number,o.customer,o.product_model,o.color,o.planned_quantity,o.valid_from_utc,
                   o.valid_to_utc,o.status,o.version,
                   (SELECT count(*) FROM production_units u WHERE u.order_id=o.id AND u.status='Completed'),
                   (SELECT count(*) FROM production_units u WHERE u.order_id=o.id AND u.status IN ('Frozen','Scrapped'))
            FROM production_orders o WHERE o.id=$1
            """ + (forUpdate ? " FOR UPDATE" : "");
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        Add(command, orderId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new ProductionOrderSnapshot(reader.GetString(0), reader.GetString(1), reader.GetString(2),
            reader.GetString(3), reader.GetString(4), reader.GetInt32(5), ReadNullableUtc(reader, 6),
            ReadNullableUtc(reader, 7), reader.GetString(8), reader.GetInt64(9),
            Convert.ToInt32(reader.GetInt64(10)), Convert.ToInt32(reader.GetInt64(11)));
    }

    private static async Task<T?> ReplayAsync<T>(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string key, string requestHash, string commandType, CancellationToken cancellationToken) where T : class
    {
        await using var command = new NpgsqlCommand(
            "SELECT request_hash, command_type, result_json::text FROM mes_commands WHERE idempotency_key=$1",
            connection, transaction);
        Add(command, key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        if (reader.GetString(0) != requestHash || reader.GetString(1) != commandType) throw IdempotencyConflict();
        return JsonSerializer.Deserialize<T>(reader.GetString(2));
    }

    private static async Task SaveCommandAsync<T>(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string key, string requestHash, string commandType, string entityId, T result, DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO mes_commands(idempotency_key, request_hash, command_type, entity_id, result_json, created_at_utc)
            VALUES ($1,$2,$3,$4,$5,$6)
            """, connection, transaction);
        Add(command, key, requestHash, commandType, entityId);
        command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, JsonSerializer.Serialize(result));
        Add(command, createdAtUtc);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertNumberHistoryAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string allocationId, string previous, string next, string reasonCode, string actorId, string stationId,
        string key, string requestHash, DateTimeOffset changedAtUtc, CancellationToken cancellationToken) =>
        await ExecuteAsync(connection, transaction, """
            INSERT INTO number_allocation_status_history(id, allocation_id, previous_status, next_status,
                reason_code, actor_id, station_id, changed_at_utc, idempotency_key, request_hash)
            VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10)
            """, cancellationToken, EntityId.New().Value, allocationId, previous, next, reasonCode, actorId,
            stationId, changedAtUtc, key, requestHash);

    private static async Task<IdentifierWriteTaskSnapshot?> SelectPendingWriteTaskAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, string stationId, string platform, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT id FROM identifier_write_tasks
            WHERE state='Pending' AND target_station_id=$1 AND platform=$2
            ORDER BY created_at_utc,id FOR UPDATE SKIP LOCKED LIMIT 1
            """, connection, transaction);
        Add(command, stationId, platform);
        var id = (string?)await command.ExecuteScalarAsync(cancellationToken);
        return id == null ? null : await ReadWriteTaskAsync(connection, transaction, id, cancellationToken);
    }

    private static async Task<IdentifierWriteTaskSnapshot?> ReadWriteTaskAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, string id, CancellationToken cancellationToken, bool forUpdate = false)
    {
        var sql = """
            SELECT id,unit_id,allocation_ids,identifiers_json::text,platform,target_station_id,state,
                   claimed_by_station_id,claimed_by_operator_id,result_json::text,diagnostic_code,version,
                   created_at_utc,updated_at_utc
            FROM identifier_write_tasks WHERE id=$1
            """ + (forUpdate ? " FOR UPDATE" : "");
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        Add(command, id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new IdentifierWriteTaskSnapshot(reader.GetString(0), reader.GetString(1), reader.GetFieldValue<string[]>(2),
            reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6),
            reader.IsDBNull(7) ? "" : reader.GetString(7), reader.IsDBNull(8) ? "" : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetString(9), reader.GetString(10), reader.GetInt64(11),
            ReadUtc(reader, 12), ReadUtc(reader, 13));
    }

    private static async Task LockAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string key,
        CancellationToken cancellationToken) => await ExecuteAsync(connection, transaction,
        "SELECT pg_advisory_xact_lock(hashtextextended($1, 44))", cancellationToken, key);

    private static async Task<T> CommitReplayAsync<T>(NpgsqlTransaction transaction, T result,
        CancellationToken cancellationToken)
    {
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private static PersistenceBusinessException IdempotencyConflict() =>
        new("IDEMPOTENCY_CONFLICT", "幂等键已用于其他请求。");
    private static DateTimeOffset? ReadNullableUtc(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : ReadUtc(reader, ordinal);
    private static DateTimeOffset ReadUtc(NpgsqlDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql,
        CancellationToken cancellationToken, params object[] values)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        Add(command, values);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
