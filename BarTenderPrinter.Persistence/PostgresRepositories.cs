using System.Globalization;
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

public sealed class ProductionOrderRepository(NpgsqlDataSource dataSource)
{
    public async Task InsertAsync(ProductionOrder order, CancellationToken cancellationToken = default,
        Func<ProductionOrder, AuditEventSnapshot>? auditFactory = null)
    {
        ArgumentNullException.ThrowIfNull(order);
        const string sql = """
            INSERT INTO production_orders
                (id, order_number, customer, product_model, color, planned_quantity, valid_from_utc, valid_to_utc, status, version)
            VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10)
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        Add(command, order.Id.Value, order.OrderNumber, order.Customer, order.ProductModel, order.Color,
            order.PlannedQuantity, DbValue(order.ValidFromUtc), DbValue(order.ValidToUtc), order.Status.ToString(), order.Version);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await TransactionalAudit.AppendAsync(connection, transaction, auditFactory?.Invoke(order), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<ProductionOrderSnapshot?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT o.id, o.order_number, o.customer, o.product_model, o.color, o.planned_quantity,
                   o.valid_from_utc, o.valid_to_utc, o.status, o.version,
                   count(u.id) FILTER (WHERE u.status='Completed'),
                   count(u.id) FILTER (WHERE u.status IN ('Frozen','Scrapped'))
            FROM production_orders o
            LEFT JOIN production_units u ON u.order_id=o.id
            WHERE o.id=$1
            GROUP BY o.id
            """;
        await using var command = dataSource.CreateCommand(sql);
        Add(command, Required(id, nameof(id)));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new ProductionOrderSnapshot(
            reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
            reader.GetInt32(5), ReadNullableUtc(reader, 6), ReadNullableUtc(reader, 7), reader.GetString(8),
            reader.GetInt64(9), Convert.ToInt32(reader.GetInt64(10), CultureInfo.InvariantCulture),
            Convert.ToInt32(reader.GetInt64(11), CultureInfo.InvariantCulture));
    }

    public async Task UpdateStateAsync(string id, ProductionOrderStatus status, long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        const string sql = "UPDATE production_orders SET status=$1, version=version+1 WHERE id=$2 AND version=$3";
        await using var command = dataSource.CreateCommand(sql);
        Add(command, status.ToString(), Required(id, nameof(id)), expectedVersion);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new PersistenceConcurrencyException(nameof(ProductionOrder), id);
    }

    private static DateTimeOffset? ReadNullableUtc(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));
}

public sealed class NumberRangeRepository(NpgsqlDataSource dataSource)
{
    public async Task InsertAsync(NumberRange range, CancellationToken cancellationToken = default,
        Func<NumberRange, AuditEventSnapshot>? auditFactory = null)
    {
        ArgumentNullException.ThrowIfNull(range);
        const string sql = """
            INSERT INTO number_ranges
                (id, order_id, number_type, prefix, date_pattern, start_value, end_value, next_value, step, numeric_width, validation_pattern, is_exhausted, version)
            VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,false,$12)
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        Add(command, range.Id.Value, range.OrderId.Value, range.Type.ToString(), range.Prefix,
            range.DatePattern.ToString(), range.Start, range.End, range.NextValue, range.Step,
            range.NumericWidth, range.ValidationPattern, range.Version);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await TransactionalAudit.AppendAsync(connection, transaction, auditFactory?.Invoke(range), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<NumberRangeSnapshot?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, order_id, number_type, prefix, date_pattern, start_value, end_value, next_value,
                   step, numeric_width, validation_pattern, is_exhausted, version
            FROM number_ranges WHERE id=$1
            """;
        await using var command = dataSource.CreateCommand(sql);
        Add(command, Required(id, nameof(id)));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new NumberRangeSnapshot(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                reader.GetInt64(5), reader.GetInt64(6), reader.GetInt64(7), reader.GetInt64(8), reader.GetInt32(9),
                reader.GetString(10), reader.GetBoolean(11), reader.GetInt64(12))
            : null;
    }

    public async Task<NumberAllocationResult> AllocateAsync(string rangeId, IdempotencyKey idempotencyKey,
        string requestHash, string stationId, string operatorId, DateTimeOffset utcNow,
        CancellationToken cancellationToken = default,
        Func<NumberAllocationResult, AuditEventSnapshot>? auditFactory = null)
    {
        ValidateUtc(utcNow, nameof(utcNow));
        requestHash = Required(requestHash, nameof(requestHash));
        stationId = Required(stationId, nameof(stationId));
        operatorId = Required(operatorId, nameof(operatorId));

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var existing = await ReadAllocationAsync(connection, transaction, idempotencyKey.Value, cancellationToken);
        if (existing != null)
        {
            if (!string.Equals(existing.Value.RequestHash, requestHash, StringComparison.Ordinal))
                throw new InvalidOperationException("IDEMPOTENCY_CONFLICT");
            await transaction.CommitAsync(cancellationToken);
            return existing.Value.Result with { IsReplay = true };
        }

        const string rangeSql = """
            SELECT prefix, date_pattern, next_value, end_value, step, numeric_width, is_exhausted
            FROM number_ranges WHERE id=$1 FOR UPDATE
            """;
        await using var rangeCommand = new NpgsqlCommand(rangeSql, connection, transaction);
        Add(rangeCommand, Required(rangeId, nameof(rangeId)));
        await using var reader = await rangeCommand.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new KeyNotFoundException("号段不存在。");
        var prefix = reader.GetString(0);
        var datePattern = Enum.Parse<NumberDatePattern>(reader.GetString(1), true);
        var nextValue = reader.GetInt64(2);
        var endValue = reader.GetInt64(3);
        var step = reader.GetInt64(4);
        var width = reader.GetInt32(5);
        var exhausted = reader.GetBoolean(6);
        await reader.CloseAsync();
        existing = await ReadAllocationAsync(connection, transaction, idempotencyKey.Value, cancellationToken);
        if (existing != null)
        {
            if (!string.Equals(existing.Value.RequestHash, requestHash, StringComparison.Ordinal))
                throw new InvalidOperationException("IDEMPOTENCY_CONFLICT");
            await transaction.CommitAsync(cancellationToken);
            return existing.Value.Result with { IsReplay = true };
        }
        if (exhausted) throw new InvalidOperationException("NUMBER_RANGE_EXHAUSTED");

        var value = prefix + FormatDate(utcNow, datePattern) +
                    (width == 0 ? nextValue.ToString(CultureInfo.InvariantCulture) : nextValue.ToString($"D{width}", CultureInfo.InvariantCulture));
        var allocationId = EntityId.New().Value;
        const string allocationSql = """
            INSERT INTO number_allocations
                (id, range_id, value, station_id, operator_id, status, idempotency_key, request_hash, allocated_at_utc)
            VALUES ($1,$2,$3,$4,$5,'Reserved',$6,$7,$8)
            """;
        await using var allocationCommand = new NpgsqlCommand(allocationSql, connection, transaction);
        Add(allocationCommand, allocationId, rangeId, value, stationId, operatorId, idempotencyKey.Value, requestHash, utcNow);
        await allocationCommand.ExecuteNonQueryAsync(cancellationToken);

        var isNowExhausted = endValue - nextValue < step;
        const string updateSql = """
            UPDATE number_ranges
            SET next_value=CASE WHEN $1 THEN next_value ELSE next_value+step END,
                is_exhausted=$1,
                version=version+1
            WHERE id=$2
            """;
        await using var updateCommand = new NpgsqlCommand(updateSql, connection, transaction);
        Add(updateCommand, isNowExhausted, rangeId);
        await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        var result = new NumberAllocationResult(allocationId, value, NumberAllocationStatus.Reserved.ToString(), false);
        await TransactionalAudit.AppendAsync(connection, transaction, auditFactory?.Invoke(result), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private static async Task<(NumberAllocationResult Result, string RequestHash)?> ReadAllocationAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string key, CancellationToken cancellationToken)
    {
        const string sql = "SELECT id, value, status, request_hash FROM number_allocations WHERE idempotency_key=$1";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        Add(command, key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return (new NumberAllocationResult(reader.GetString(0), reader.GetString(1), reader.GetString(2), false), reader.GetString(3));
    }

    private static string FormatDate(DateTimeOffset value, NumberDatePattern pattern) => pattern switch
    {
        NumberDatePattern.None => "",
        NumberDatePattern.YyMm => value.ToString("yyMM", CultureInfo.InvariantCulture),
        NumberDatePattern.YyMmDd => value.ToString("yyMMdd", CultureInfo.InvariantCulture),
        NumberDatePattern.YyyyMm => value.ToString("yyyyMM", CultureInfo.InvariantCulture),
        NumberDatePattern.YyyyMmDd => value.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
        NumberDatePattern.YyDayOfYear => value.ToString("yy", CultureInfo.InvariantCulture) + value.DayOfYear.ToString("D3", CultureInfo.InvariantCulture),
        NumberDatePattern.YyyyDayOfYear => value.ToString("yyyy", CultureInfo.InvariantCulture) + value.DayOfYear.ToString("D3", CultureInfo.InvariantCulture),
        NumberDatePattern.MmDd => value.ToString("MMdd", CultureInfo.InvariantCulture),
        _ => throw new ArgumentOutOfRangeException(nameof(pattern))
    };
}

public sealed class ProductionUnitRepository(NpgsqlDataSource dataSource)
{
    public async Task InsertAsync(ProductionUnit unit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(unit);
        const string sql = """
            INSERT INTO production_units (id, order_id, status, current_operation_id, identifiers_json, version)
            VALUES ($1,$2,$3,$4,$5,$6)
            """;
        await using var command = dataSource.CreateCommand(sql);
        Add(command, unit.Id.Value, unit.OrderId.Value, unit.Status.ToString(), unit.CurrentOperationId);
        command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, JsonSerializer.Serialize(unit.Identifiers.ToDictionary(item => item.Key.ToString(), item => item.Value.Value)));
        command.Parameters.AddWithValue(unit.Version);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ProductionUnitSnapshot?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT id, order_id, status, current_operation_id, version FROM production_units WHERE id=$1";
        await using var command = dataSource.CreateCommand(sql);
        Add(command, Required(id, nameof(id)));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new ProductionUnitSnapshot(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt64(4))
            : null;
    }

    public async Task UpdateStateAsync(string id, ProductionUnitStatus status, string currentOperationId,
        long expectedVersion, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE production_units
            SET status=$1, current_operation_id=$2, version=version+1
            WHERE id=$3 AND version=$4
            """;
        await using var command = dataSource.CreateCommand(sql);
        Add(command, status.ToString(), currentOperationId?.Trim() ?? "", Required(id, nameof(id)), expectedVersion);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new PersistenceConcurrencyException(nameof(ProductionUnit), id);
    }
}

public sealed class ManufacturingConfigurationRepository(NpgsqlDataSource dataSource)
{
    public async Task InsertRouteAsync(ManufacturingRoute route, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(route);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var routeCommand = new NpgsqlCommand(
            "INSERT INTO manufacturing_routes(id, order_id, name, route_type) VALUES ($1,$2,$3,$4)", connection, transaction);
        Add(routeCommand, route.Id.Value, route.OrderId.Value, route.Name, route.Type.ToString());
        await routeCommand.ExecuteNonQueryAsync(cancellationToken);
        foreach (var operation in route.Operations)
        {
            await using var operationCommand = new NpgsqlCommand(
                "INSERT INTO manufacturing_operations(route_id, operation_id, name, sequence) VALUES ($1,$2,$3,$4)", connection, transaction);
            Add(operationCommand, route.Id.Value, operation.Id, operation.Name, operation.Sequence);
            await operationCommand.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task InsertStationAsync(Station station, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(station);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var stationCommand = new NpgsqlCommand("INSERT INTO stations(id, name) VALUES ($1,$2)", connection, transaction);
        Add(stationCommand, station.Id.Value, station.Name);
        await stationCommand.ExecuteNonQueryAsync(cancellationToken);
        foreach (var operationId in station.QualifiedOperationIds)
        {
            await using var qualificationCommand = new NpgsqlCommand(
                "INSERT INTO station_qualifications(station_id, operation_id) VALUES ($1,$2)", connection, transaction);
            Add(qualificationCommand, station.Id.Value, operationId);
            await qualificationCommand.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }
}

public sealed class StationPassRepository(NpgsqlDataSource dataSource)
{
    public async Task<bool> InsertAsync(StationPassRecord record, string requestHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        const string sql = """
            INSERT INTO station_pass_records
                (id, unit_id, order_id, route_id, operation_id, station_id, operator_id, occurred_at_utc, idempotency_key, request_hash, rework_order_id, rework_sequence)
            VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12)
            ON CONFLICT (idempotency_key) DO NOTHING
            """;
        await using var command = dataSource.CreateCommand(sql);
        Add(command, record.Id.Value, record.UnitId.Value, record.OrderId.Value, record.RouteId.Value,
            record.OperationId, record.StationId.Value, record.OperatorId, record.OccurredAtUtc,
            record.IdempotencyKey.Value, Required(requestHash, nameof(requestHash)),
            DbValue(string.IsNullOrWhiteSpace(record.ReworkOrderId) ? null : record.ReworkOrderId), record.ReworkSequence);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 1) return true;
        await using var existingCommand = dataSource.CreateCommand(
            "SELECT request_hash FROM station_pass_records WHERE idempotency_key=$1");
        Add(existingCommand, record.IdempotencyKey.Value);
        var existingHash = (string?)await existingCommand.ExecuteScalarAsync(cancellationToken);
        if (!string.Equals(existingHash, requestHash, StringComparison.Ordinal))
            throw new InvalidOperationException("IDEMPOTENCY_CONFLICT");
        return false;
    }

    public async Task<StationPassSnapshot> PassAsync(
        string unitId,
        string orderId,
        string routeId,
        string operationId,
        string stationId,
        string operatorId,
        IdempotencyKey idempotencyKey,
        string requestHash,
        DateTimeOffset occurredAtUtc,
        string reworkOrderId = "",
        int reworkSequence = 0,
        CancellationToken cancellationToken = default,
        Func<StationPassSnapshot, AuditEventSnapshot>? auditFactory = null)
    {
        ValidateUtc(occurredAtUtc, nameof(occurredAtUtc));
        unitId = Required(unitId, nameof(unitId));
        orderId = Required(orderId, nameof(orderId));
        routeId = Required(routeId, nameof(routeId));
        operationId = Required(operationId, nameof(operationId));
        stationId = Required(stationId, nameof(stationId));
        operatorId = Required(operatorId, nameof(operatorId));
        requestHash = Required(requestHash, nameof(requestHash));

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await AcquireIdempotencyLockAsync(connection, transaction, idempotencyKey.Value, cancellationToken);
        var existing = await ReadPassAsync(connection, transaction, idempotencyKey.Value, cancellationToken);
        if (existing != null)
        {
            if (!string.Equals(existing.Value.RequestHash, requestHash, StringComparison.Ordinal))
                throw new PersistenceBusinessException("IDEMPOTENCY_CONFLICT", "幂等键已用于其他过站请求。");
            await transaction.CommitAsync(cancellationToken);
            return existing.Value.Snapshot with { IsReplay = true };
        }

        const string contextSql = """
            SELECT o.status, o.valid_from_utc, o.valid_to_utc,
                   u.order_id, u.status, u.version,
                   r.order_id, r.route_type,
                   op.sequence,
                   EXISTS(SELECT 1 FROM station_qualifications q WHERE q.station_id=$5 AND q.operation_id=$4)
            FROM production_orders o
            JOIN production_units u ON u.id=$1
            JOIN manufacturing_routes r ON r.id=$3
            JOIN manufacturing_operations op ON op.route_id=r.id AND op.operation_id=$4
            WHERE o.id=$2
            FOR UPDATE OF o, u
            """;
        await using var contextCommand = new NpgsqlCommand(contextSql, connection, transaction);
        Add(contextCommand, unitId, orderId, routeId, operationId, stationId);
        await using var reader = await contextCommand.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new PersistenceBusinessException("NOT_FOUND", "订单、生产单元、路线或工序不存在。");
        var orderStatus = reader.GetString(0);
        DateTimeOffset? validFrom = reader.IsDBNull(1) ? null : ReadUtc(reader, 1);
        DateTimeOffset? validTo = reader.IsDBNull(2) ? null : ReadUtc(reader, 2);
        var unitOrderId = reader.GetString(3);
        var unitStatus = reader.GetString(4);
        var unitVersion = reader.GetInt64(5);
        var routeOrderId = reader.GetString(6);
        var routeType = reader.GetString(7);
        var operationSequence = reader.GetInt32(8);
        var qualified = reader.GetBoolean(9);
        await reader.CloseAsync();

        if (!string.Equals(orderStatus, ProductionOrderStatus.InProduction.ToString(), StringComparison.Ordinal) ||
            (validFrom.HasValue && validFrom.Value > occurredAtUtc) ||
            (validTo.HasValue && validTo.Value < occurredAtUtc))
            throw new PersistenceBusinessException("ORDER_UNAVAILABLE", "订单当前不接受过站。");
        if (!string.Equals(unitOrderId, orderId, StringComparison.Ordinal) || !string.Equals(routeOrderId, orderId, StringComparison.Ordinal))
            throw new PersistenceBusinessException("ROUTE_MISMATCH", "订单、生产单元和工艺路线不匹配。");
        if (!string.Equals(unitStatus, ProductionUnitStatus.Active.ToString(), StringComparison.Ordinal))
            throw new PersistenceBusinessException("UNIT_UNAVAILABLE", "生产单元当前不接受过站。");
        if (!qualified)
            throw new PersistenceBusinessException("STATION_NOT_QUALIFIED", "当前工位未取得指定工序资格。");
        if (!string.Equals(routeType, RouteType.Standard.ToString(), StringComparison.Ordinal) && string.IsNullOrWhiteSpace(reworkOrderId))
            throw new PersistenceBusinessException("REWORK_CONTEXT_REQUIRED", "返工路线需要有效返工任务。");
        int? reworkStartSequence = null;
        if (!string.Equals(routeType, RouteType.Standard.ToString(), StringComparison.Ordinal))
        {
            await using var reworkCommand = new NpgsqlCommand("""
                SELECT op.sequence
                FROM rework_orders rw
                JOIN manufacturing_operations op
                  ON op.route_id=rw.route_id AND op.operation_id=rw.start_operation_id
                WHERE rw.id=$1 AND rw.status='Active' AND rw.production_unit_id=$2
                  AND rw.route_id=$3 AND rw.sequence=$4
                """, connection, transaction);
            Add(reworkCommand, reworkOrderId, unitId, routeId, reworkSequence);
            var startSequenceValue = await reworkCommand.ExecuteScalarAsync(cancellationToken);
            if (startSequenceValue == null)
                throw new PersistenceBusinessException("REWORK_CONTEXT_REQUIRED", "返工任务必须存在、已激活并匹配生产单元、路线和返工序次。");
            reworkStartSequence = Convert.ToInt32(startSequenceValue, System.Globalization.CultureInfo.InvariantCulture);
            if (operationSequence < reworkStartSequence)
                throw new PersistenceBusinessException("REWORK_OPERATION_BEFORE_START", "返工过站必须从指定起始工序开始。");
        }

        await using var duplicateCommand = new NpgsqlCommand(
            "SELECT EXISTS(SELECT 1 FROM station_pass_records WHERE unit_id=$1 AND route_id=$2 AND operation_id=$3 AND rework_order_id IS NOT DISTINCT FROM $4)",
            connection, transaction);
        Add(duplicateCommand, unitId, routeId, operationId, DbValue(EmptyToNull(reworkOrderId)));
        if ((bool)(await duplicateCommand.ExecuteScalarAsync(cancellationToken) ?? false))
            throw new PersistenceBusinessException("OPERATION_ALREADY_COMPLETED", "指定工序已经完成。");

        const string previousSql = """
            SELECT operation_id FROM manufacturing_operations
            WHERE route_id=$1 AND sequence < $2 AND ($3::integer IS NULL OR sequence >= $3)
            ORDER BY sequence DESC LIMIT 1
            """;
        await using var previousCommand = new NpgsqlCommand(previousSql, connection, transaction);
        Add(previousCommand, routeId, operationSequence, DbValue(reworkStartSequence));
        var previousOperationId = (string?)await previousCommand.ExecuteScalarAsync(cancellationToken);
        if (!string.IsNullOrEmpty(previousOperationId))
        {
            await using var completedCommand = new NpgsqlCommand(
                "SELECT EXISTS(SELECT 1 FROM station_pass_records WHERE unit_id=$1 AND route_id=$2 AND operation_id=$3 AND rework_order_id IS NOT DISTINCT FROM $4)",
                connection, transaction);
            Add(completedCommand, unitId, routeId, previousOperationId, DbValue(EmptyToNull(reworkOrderId)));
            if (!(bool)(await completedCommand.ExecuteScalarAsync(cancellationToken) ?? false))
                throw new PersistenceBusinessException("PREVIOUS_OPERATION_INCOMPLETE", "上一工序尚未完成。", new { MissingOperationId = previousOperationId });
        }

        var snapshot = new StationPassSnapshot(EntityId.New().Value, unitId, orderId, routeId, operationId,
            stationId, operatorId, occurredAtUtc, idempotencyKey.Value, reworkOrderId, reworkSequence, false);
        const string insertSql = """
            INSERT INTO station_pass_records
                (id, unit_id, order_id, route_id, operation_id, station_id, operator_id, occurred_at_utc,
                 idempotency_key, request_hash, rework_order_id, rework_sequence)
            VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12)
            """;
        await using var insertCommand = new NpgsqlCommand(insertSql, connection, transaction);
        Add(insertCommand, snapshot.Id, unitId, orderId, routeId, operationId, stationId, operatorId,
            occurredAtUtc, idempotencyKey.Value, requestHash, DbValue(EmptyToNull(reworkOrderId)), reworkSequence);
        await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        await using var updateCommand = new NpgsqlCommand(
            "UPDATE production_units SET current_operation_id=$1, version=version+1 WHERE id=$2 AND version=$3", connection, transaction);
        Add(updateCommand, operationId, unitId, unitVersion);
        if (await updateCommand.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new PersistenceConcurrencyException(nameof(ProductionUnit), unitId);
        await TransactionalAudit.AppendAsync(connection, transaction, auditFactory?.Invoke(snapshot), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return snapshot;
    }

    private static async Task<(StationPassSnapshot Snapshot, string RequestHash)?> ReadPassAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string key, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, unit_id, order_id, route_id, operation_id, station_id, operator_id, occurred_at_utc,
                   idempotency_key, rework_order_id, rework_sequence, request_hash
            FROM station_pass_records WHERE idempotency_key=$1
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        Add(command, key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var snapshot = new StationPassSnapshot(
            reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
             reader.GetString(5), reader.GetString(6), ReadUtc(reader, 7), reader.GetString(8), reader.IsDBNull(9) ? "" : reader.GetString(9),
             reader.GetInt32(10), false);
        return (snapshot, reader.GetString(11));
    }

    private static DateTimeOffset ReadUtc(NpgsqlDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static async Task AcquireIdempotencyLockAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string key, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended($1, 12))", connection, transaction);
        Add(command, key);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

public sealed class PackagingRepository(NpgsqlDataSource dataSource)
{
    public async Task InsertUnitAsync(PackagingUnit unit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(unit);
        const string sql = """
            INSERT INTO packaging_units
                (id, order_id, unit_type, code, product_model, color, capacity, status, version, production_unit_id)
            VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10)
            """;
        await using var command = dataSource.CreateCommand(sql);
        Add(command, unit.Id.Value, unit.OrderId.Value, unit.Type.ToString(), unit.Code, unit.ProductModel,
            unit.Color, unit.Capacity, unit.Status.ToString(), unit.Version, DbValue(unit.ProductionUnitId?.Value));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<PackagingBindingResult> BindPackagingAsync(string parentId, string childId,
        long expectedParentVersion, string operatorId, DateTimeOffset utcNow,
        CancellationToken cancellationToken = default) =>
        await BindPackagingAsync(parentId, childId, expectedParentVersion, operatorId, utcNow,
            new IdempotencyKey(Guid.NewGuid().ToString("N")), Guid.NewGuid().ToString("N"), cancellationToken);

    public async Task<PackagingBindingResult> BindPackagingAsync(string parentId, string childId,
        long expectedParentVersion, string operatorId, DateTimeOffset utcNow,
        IdempotencyKey idempotencyKey, string requestHash,
        CancellationToken cancellationToken = default,
        Func<PackagingBindingResult, AuditEventSnapshot>? auditFactory = null)
    {
        ValidateUtc(utcNow, nameof(utcNow));
        parentId = Required(parentId, nameof(parentId));
        childId = Required(childId, nameof(childId));
        operatorId = Required(operatorId, nameof(operatorId));
        requestHash = Required(requestHash, nameof(requestHash));
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var existing = await ReadBindingAsync(connection, transaction, idempotencyKey.Value, cancellationToken);
        if (existing != null)
        {
            if (!string.Equals(existing.Value.RequestHash, requestHash, StringComparison.Ordinal))
                throw new PersistenceBusinessException("IDEMPOTENCY_CONFLICT", "幂等键已用于其他包装绑定请求。");
            await transaction.CommitAsync(cancellationToken);
            return existing.Value.Result with { IsReplay = true };
        }

        const string unitsSql = """
            SELECT id, order_id, unit_type, code, product_model, color, capacity, status, version
            FROM packaging_units WHERE id IN ($1,$2) ORDER BY id FOR UPDATE
            """;
        await using var unitsCommand = new NpgsqlCommand(unitsSql, connection, transaction);
        Add(unitsCommand, parentId, childId);
        var units = new Dictionary<string, StoredPackagingUnit>(StringComparer.Ordinal);
        await using (var unitsReader = await unitsCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await unitsReader.ReadAsync(cancellationToken))
            {
                units.Add(unitsReader.GetString(0), new StoredPackagingUnit(
                    unitsReader.GetString(1),
                    Enum.Parse<PackagingUnitType>(unitsReader.GetString(2), true),
                    unitsReader.GetString(3),
                    unitsReader.GetString(4),
                    unitsReader.GetString(5),
                    unitsReader.GetInt32(6),
                    Enum.Parse<PackagingUnitStatus>(unitsReader.GetString(7), true),
                    unitsReader.GetInt64(8)));
            }
        }
        if (!units.TryGetValue(parentId, out var parent) || !units.TryGetValue(childId, out var child))
            throw new KeyNotFoundException("父级或子级包装单元不存在。");
        var capacity = parent.Capacity;
        var status = parent.Status;
        var version = parent.Version;
        if (version != expectedParentVersion) throw new PersistenceConcurrencyException(nameof(PackagingUnit), parentId);
        if (status != PackagingUnitStatus.Open)
            throw new PersistenceBusinessException("PACKAGING_UNIT_CLOSED", "父级包装单元已经关闭。");
        if (!IsAllowedRelationship(parent.Type, child.Type))
            throw new PersistenceBusinessException("PACKAGING_TYPE_MISMATCH", "包装层级关系无效。");
        if (!string.Equals(parent.OrderId, child.OrderId, StringComparison.Ordinal) ||
            !string.Equals(parent.ProductModel, child.ProductModel, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(parent.Color, child.Color, StringComparison.OrdinalIgnoreCase))
            throw new PersistenceBusinessException("PACKAGING_PRODUCT_MISMATCH", "订单、型号或颜色不一致。");
        if (child.Status != PackagingUnitStatus.Closed)
            throw new PersistenceBusinessException("PACKAGING_CHILD_NOT_READY", "子级包装单元尚未关闭。");

        existing = await ReadBindingAsync(connection, transaction, idempotencyKey.Value, cancellationToken);
        if (existing != null)
        {
            if (!string.Equals(existing.Value.RequestHash, requestHash, StringComparison.Ordinal))
                throw new PersistenceBusinessException("IDEMPOTENCY_CONFLICT", "幂等键已用于其他包装绑定请求。");
            await transaction.CommitAsync(cancellationToken);
            return existing.Value.Result with { IsReplay = true };
        }
        await using var activeParentCommand = new NpgsqlCommand(
            "SELECT parent_id FROM packaging_bindings WHERE child_id=$1 AND is_active", connection, transaction);
        Add(activeParentCommand, childId);
        if (await activeParentCommand.ExecuteScalarAsync(cancellationToken) is string activeParentId)
            throw new PersistenceBusinessException("PACKAGING_BINDING_CONFLICT", $"子级包装单元已绑定到父级 {activeParentId}。");

        await using var countCommand = new NpgsqlCommand("SELECT count(*) FROM packaging_bindings WHERE parent_id=$1 AND is_active", connection, transaction);
        Add(countCommand, parentId);
        var count = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        if (count >= capacity)
            throw new PersistenceBusinessException("PACKAGING_CAPACITY_EXCEEDED", "父级包装单元已达到容量上限。");
        var closesParent = count + 1 == capacity;

        const string bindingSql = """
            INSERT INTO packaging_bindings
                (parent_id, child_id, bound_at_utc, operator_id, is_active, idempotency_key, request_hash)
            VALUES ($1,$2,$3,$4,true,$5,$6)
            """;
        await using var bindingCommand = new NpgsqlCommand(bindingSql, connection, transaction);
        Add(bindingCommand, parentId, childId, utcNow, operatorId, idempotencyKey.Value, requestHash);
        await bindingCommand.ExecuteNonQueryAsync(cancellationToken);
        const string updateSql = "UPDATE packaging_units SET status=$1, version=version+1 WHERE id=$2 AND version=$3";
        await using var updateCommand = new NpgsqlCommand(updateSql, connection, transaction);
        Add(updateCommand, closesParent ? PackagingUnitStatus.Closed.ToString() : PackagingUnitStatus.Open.ToString(), parentId, expectedParentVersion);
        if (await updateCommand.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new PersistenceConcurrencyException(nameof(PackagingUnit), parentId);

        PackagingPrintIntentSnapshot? printIntent = null;
        if (closesParent && parent.Type is PackagingUnitType.ColorBox or PackagingUnitType.Carton or PackagingUnitType.Pallet)
        {
            await using var childrenCommand = new NpgsqlCommand("""
                SELECT u.code FROM packaging_bindings b
                JOIN packaging_units u ON u.id=b.child_id
                WHERE b.parent_id=$1 AND b.is_active ORDER BY b.bound_at_utc, u.code
                """, connection, transaction);
            Add(childrenCommand, parentId);
            var childCodes = new List<string>();
            await using (var childrenReader = await childrenCommand.ExecuteReaderAsync(cancellationToken))
                while (await childrenReader.ReadAsync(cancellationToken)) childCodes.Add(childrenReader.GetString(0));
            var fieldsJson = JsonSerializer.Serialize(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["PACKAGE_CODE"] = parent.Code,
                ["ORDER_ID"] = parent.OrderId,
                ["PRODUCT_MODEL"] = parent.ProductModel,
                ["COLOR"] = parent.Color,
                ["QUANTITY"] = childCodes.Count.ToString(CultureInfo.InvariantCulture),
                ["CHILD_CODES"] = string.Join(",", childCodes)
            });
            printIntent = new PackagingPrintIntentSnapshot(EntityId.New().Value, parentId,
                parent.Type.ToString(),
                fieldsJson, utcNow);
            await MesCoreRepository.RegisterPackagingLabelAsync(connection, transaction,
                new PackagingUnitSnapshot(parentId, parent.OrderId, parent.Type.ToString(), parent.Code,
                    parent.ProductModel, parent.Color, parent.Capacity, PackagingUnitStatus.Closed.ToString(),
                    expectedParentVersion + 1, null), childCodes, utcNow, cancellationToken, printIntent.Id);
        }
        var result = new PackagingBindingResult(parentId, childId, expectedParentVersion + 1, closesParent, false, printIntent);
        await TransactionalAudit.AppendAsync(connection, transaction, auditFactory?.Invoke(result), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task InsertPrintIntentAsync(PackagingPrintIntent intent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        const string sql = """
            INSERT INTO packaging_print_intents(id, packaging_unit_id, label_type, fields_json, created_at_utc)
            VALUES ($1,$2,$3,$4,$5)
            ON CONFLICT (packaging_unit_id) DO NOTHING
            """;
        await using var command = dataSource.CreateCommand(sql);
        Add(command, intent.Id.Value, intent.PackagingUnitId.Value, intent.LabelType.ToString());
        command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, JsonSerializer.Serialize(intent.Fields));
        command.Parameters.AddWithValue(intent.CreatedAtUtc);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed record StoredPackagingUnit(
        string OrderId,
        PackagingUnitType Type,
        string Code,
        string ProductModel,
        string Color,
        int Capacity,
        PackagingUnitStatus Status,
        long Version);

    private static async Task<(PackagingBindingResult Result, string RequestHash)?> ReadBindingAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string key, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT b.parent_id, b.child_id, b.request_hash, p.version, p.status,
                   i.id, i.label_type, i.fields_json::text, i.created_at_utc
            FROM packaging_bindings b
            JOIN packaging_units p ON p.id=b.parent_id
            LEFT JOIN packaging_print_intents i ON i.packaging_unit_id=b.parent_id
            WHERE b.idempotency_key=$1
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        Add(command, key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        PackagingPrintIntentSnapshot? intent = reader.IsDBNull(5) ? null : new PackagingPrintIntentSnapshot(
            reader.GetString(5), reader.GetString(0), reader.GetString(6), reader.GetString(7),
            new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(8), DateTimeKind.Utc)));
        var result = new PackagingBindingResult(reader.GetString(0), reader.GetString(1), reader.GetInt64(3),
            string.Equals(reader.GetString(4), PackagingUnitStatus.Closed.ToString(), StringComparison.Ordinal), true, intent);
        return (result, reader.GetString(2));
    }

    private static bool IsAllowedRelationship(PackagingUnitType parent, PackagingUnitType child) =>
        (parent, child) is
            (PackagingUnitType.ColorBox, PackagingUnitType.Body) or
            (PackagingUnitType.Carton, PackagingUnitType.ColorBox) or
            (PackagingUnitType.Pallet, PackagingUnitType.Carton);
}

public sealed class PrintJobRepository(NpgsqlDataSource dataSource)
{
    public async Task<PrintJobSnapshot> RegisterAsync(PrintJobSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO print_jobs
                (job_id, idempotency_key, label_type, template_id, template_version, state, request_hash,
                 request_json, result_json, version, created_at_utc, updated_at_utc,
                 trace_order_id, trace_unit_id, trace_packaging_unit_id)
            VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15)
            ON CONFLICT (idempotency_key) DO NOTHING
            """;
        await using var command = dataSource.CreateCommand(sql);
        Add(command, snapshot.JobId, snapshot.IdempotencyKey, snapshot.LabelType, snapshot.TemplateId,
            snapshot.TemplateVersion, snapshot.State, snapshot.RequestHash);
        command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, snapshot.RequestJson);
        command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, DbValue(snapshot.ResultJson));
        Add(command, snapshot.Version, snapshot.CreatedAtUtc, snapshot.UpdatedAtUtc);
        Add(command, DbValue(snapshot.TraceOrderId), DbValue(snapshot.TraceUnitId), DbValue(snapshot.TracePackagingUnitId));
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 1) return snapshot;
        var existing = await GetByIdempotencyKeyAsync(snapshot.IdempotencyKey, cancellationToken)
            ?? throw new InvalidOperationException("打印作业写入冲突。");
        if (!string.Equals(existing.RequestHash, snapshot.RequestHash, StringComparison.Ordinal))
            throw new InvalidOperationException("IDEMPOTENCY_CONFLICT");
        return existing;
    }

    public async Task<PrintJobSnapshot?> GetByIdempotencyKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT job_id, idempotency_key, label_type, template_id, template_version, state,
                    request_hash, request_json::text, result_json::text, version, created_at_utc, updated_at_utc,
                    claimed_by_station_id, claimed_by_operator_id, claim_idempotency_key, receipt_idempotency_key,
                    trace_order_id, trace_unit_id, trace_packaging_unit_id
            FROM print_jobs WHERE idempotency_key=$1
            """;
        await using var command = dataSource.CreateCommand(sql);
        Add(command, Required(key, nameof(key)));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadPrintJob(reader) : null;
    }

    public async Task<PrintJobSnapshot?> GetByJobIdAsync(string jobId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT job_id, idempotency_key, label_type, template_id, template_version, state,
                   request_hash, request_json::text, result_json::text, version, created_at_utc, updated_at_utc,
                   claimed_by_station_id, claimed_by_operator_id, claim_idempotency_key, receipt_idempotency_key,
                   trace_order_id, trace_unit_id, trace_packaging_unit_id
            FROM print_jobs WHERE job_id=$1
            """;
        await using var command = dataSource.CreateCommand(sql);
        Add(command, Required(jobId, nameof(jobId)));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadPrintJob(reader) : null;
    }

    public async Task<PrintJobClaimResult> ClaimNextAsync(string stationId, string operatorId,
        IdempotencyKey idempotencyKey, string requestHash, DateTimeOffset claimedAtUtc,
        CancellationToken cancellationToken = default,
        Func<PrintJobSnapshot, AuditEventSnapshot>? auditFactory = null)
    {
        ValidateUtc(claimedAtUtc, nameof(claimedAtUtc));
        stationId = Required(stationId, nameof(stationId));
        operatorId = Required(operatorId, nameof(operatorId));
        requestHash = Required(requestHash, nameof(requestHash));
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var lockCommand = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended($1, 0))", connection, transaction))
        {
            Add(lockCommand, idempotencyKey.Value);
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        var existingRequest = await ReadClaimRequestAsync(connection, transaction, idempotencyKey.Value, cancellationToken);
        if (existingRequest != null)
        {
            if (!string.Equals(existingRequest.Value.RequestHash, requestHash, StringComparison.Ordinal))
                throw new PersistenceBusinessException("IDEMPOTENCY_CONFLICT", "幂等键已用于其他打印作业领取请求。");
            var existing = existingRequest.Value.HasJob
                ? await ReadClaimAsync(connection, transaction, idempotencyKey.Value, cancellationToken)
                : null;
            await transaction.CommitAsync(cancellationToken);
            return new PrintJobClaimResult(existing?.Job, true);
        }

        const string selectSql = """
            SELECT j.job_id FROM print_jobs j
            WHERE j.state='Received'
              AND NOT EXISTS (
                  SELECT 1 FROM packaging_units p
                  WHERE p.id=j.trace_packaging_unit_id AND p.status='Frozen')
              AND NOT EXISTS (
                  SELECT 1 FROM packaging_quality_holds h
                  WHERE h.packaging_unit_id=j.trace_packaging_unit_id
                    AND NOT EXISTS (
                        SELECT 1 FROM dispositions d
                        WHERE d.lot_id=h.lot_id AND d.decision='Release'))
            ORDER BY j.created_at_utc, j.job_id
            FOR UPDATE SKIP LOCKED
            LIMIT 1
            """;
        await using var selectCommand = new NpgsqlCommand(selectSql, connection, transaction);
        var jobId = (string?)await selectCommand.ExecuteScalarAsync(cancellationToken);
        if (jobId == null)
        {
            await InsertClaimRequestAsync(connection, transaction, idempotencyKey.Value, requestHash, null,
                claimedAtUtc, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new PrintJobClaimResult(null, false);
        }

        const string updateSql = """
            UPDATE print_jobs
            SET state='Submitting', claimed_by_station_id=$1, claimed_by_operator_id=$2,
                claim_idempotency_key=$3, claim_request_hash=$4, version=version+1, updated_at_utc=$5
            WHERE job_id=$6
            """;
        await using var updateCommand = new NpgsqlCommand(updateSql, connection, transaction);
        Add(updateCommand, stationId, operatorId, idempotencyKey.Value, requestHash, claimedAtUtc, jobId);
        await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        await InsertClaimRequestAsync(connection, transaction, idempotencyKey.Value, requestHash, jobId,
            claimedAtUtc, cancellationToken);
        var claimed = await ReadPrintJobAsync(connection, transaction, jobId, cancellationToken)
            ?? throw new InvalidOperationException("已领取打印作业无法读取。");
        await TransactionalAudit.AppendAsync(connection, transaction, auditFactory?.Invoke(claimed), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new PrintJobClaimResult(claimed, false);
    }

    public async Task<PrintJobReceiptResult> RecordReceiptAsync(string jobId, string stationId,
        IdempotencyKey idempotencyKey, string requestHash, string state, string resultJson,
        DateTimeOffset receivedAtUtc, CancellationToken cancellationToken = default,
        Func<PrintJobSnapshot, AuditEventSnapshot>? auditFactory = null)
    {
        ValidateUtc(receivedAtUtc, nameof(receivedAtUtc));
        jobId = Required(jobId, nameof(jobId));
        stationId = Required(stationId, nameof(stationId));
        requestHash = Required(requestHash, nameof(requestHash));
        state = Required(state, nameof(state));
        resultJson = Required(resultJson, nameof(resultJson));
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var lockCommand = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended($1, 0))", connection, transaction))
        {
            Add(lockCommand, idempotencyKey.Value);
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var receiptCommand = new NpgsqlCommand(
            "SELECT job_id FROM print_jobs WHERE receipt_idempotency_key=$1", connection, transaction))
        {
            Add(receiptCommand, idempotencyKey.Value);
            if (await receiptCommand.ExecuteScalarAsync(cancellationToken) is string receiptJobId &&
                !string.Equals(receiptJobId, jobId, StringComparison.Ordinal))
                throw new PersistenceBusinessException("IDEMPOTENCY_CONFLICT", "回执幂等键已用于其他打印作业。");
        }
        var job = await ReadPrintJobForUpdateAsync(connection, transaction, jobId, cancellationToken)
            ?? throw new KeyNotFoundException("打印作业不存在。");

        if (!string.IsNullOrEmpty(job.ReceiptIdempotencyKey))
        {
            var receiptHash = await ReadReceiptHashAsync(connection, transaction, jobId, cancellationToken);
            if (!string.Equals(job.ReceiptIdempotencyKey, idempotencyKey.Value, StringComparison.Ordinal) ||
                !string.Equals(receiptHash, requestHash, StringComparison.Ordinal))
                throw new PersistenceBusinessException("IDEMPOTENCY_CONFLICT", "打印作业已记录其他回执。");
            await transaction.CommitAsync(cancellationToken);
            return new PrintJobReceiptResult(job, true);
        }
        if (!string.Equals(job.State, "Submitting", StringComparison.Ordinal))
            throw new PersistenceBusinessException("PRINT_JOB_STATE_CONFLICT", "打印作业当前状态不接受回执。");
        if (!string.Equals(job.ClaimedByStationId, stationId, StringComparison.Ordinal))
            throw new PersistenceBusinessException("PRINT_JOB_STATION_MISMATCH", "打印作业必须由领取工位提交回执。");

        const string updateSql = """
            UPDATE print_jobs
            SET state=$1, result_json=$2, receipt_idempotency_key=$3, receipt_request_hash=$4,
                version=version+1, updated_at_utc=$5
            WHERE job_id=$6 AND version=$7
            """;
        await using var updateCommand = new NpgsqlCommand(updateSql, connection, transaction);
        Add(updateCommand, state);
        updateCommand.Parameters.AddWithValue(NpgsqlDbType.Jsonb, resultJson);
        Add(updateCommand, idempotencyKey.Value, requestHash, receivedAtUtc, jobId, job.Version);
        if (await updateCommand.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new PersistenceConcurrencyException("PrintJob", jobId);
        var updated = await ReadPrintJobAsync(connection, transaction, jobId, cancellationToken)
            ?? throw new InvalidOperationException("已更新打印作业无法读取。");
        await TransactionalAudit.AppendAsync(connection, transaction, auditFactory?.Invoke(updated), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new PrintJobReceiptResult(updated, false);
    }

    public async Task UpdateStateAsync(string jobId, string state, string? resultJson, long expectedVersion,
        DateTimeOffset updatedAtUtc, CancellationToken cancellationToken = default)
    {
        ValidateUtc(updatedAtUtc, nameof(updatedAtUtc));
        const string sql = """
            UPDATE print_jobs
            SET state=$1, result_json=$2, version=version+1, updated_at_utc=$3
            WHERE job_id=$4 AND version=$5
            """;
        await using var command = dataSource.CreateCommand(sql);
        Add(command, Required(state, nameof(state)));
        command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, DbValue(resultJson));
        Add(command, updatedAtUtc, Required(jobId, nameof(jobId)), expectedVersion);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new PersistenceConcurrencyException("PrintJob", jobId);
    }

    private static PrintJobSnapshot ReadPrintJob(NpgsqlDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
        reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetString(8),
        reader.GetInt64(9), ReadUtc(reader, 10), ReadUtc(reader, 11),
        reader.IsDBNull(12) ? "" : reader.GetString(12), reader.IsDBNull(13) ? "" : reader.GetString(13),
        reader.IsDBNull(14) ? "" : reader.GetString(14), reader.IsDBNull(15) ? null : reader.GetString(15),
        reader.IsDBNull(16) ? null : reader.GetString(16), reader.IsDBNull(17) ? null : reader.GetString(17),
        reader.IsDBNull(18) ? null : reader.GetString(18));

    private static async Task<(PrintJobSnapshot Job, string RequestHash)?> ReadClaimAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string key, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT j.job_id, j.idempotency_key, j.label_type, j.template_id, j.template_version, j.state,
                   j.request_hash, j.request_json::text, j.result_json::text, j.version, j.created_at_utc, j.updated_at_utc,
                   j.claimed_by_station_id, j.claimed_by_operator_id, j.claim_idempotency_key, j.receipt_idempotency_key,
                   j.trace_order_id, j.trace_unit_id, j.trace_packaging_unit_id, c.request_hash
            FROM print_job_claim_requests c
            JOIN print_jobs j ON j.job_id=c.job_id
            WHERE c.idempotency_key=$1
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        Add(command, key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? (ReadPrintJob(reader), reader.GetString(19)) : null;
    }

    private static async Task<(string RequestHash, bool HasJob)?> ReadClaimRequestAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string key, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT request_hash, job_id IS NOT NULL FROM print_job_claim_requests WHERE idempotency_key=$1",
            connection, transaction);
        Add(command, key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? (reader.GetString(0), reader.GetBoolean(1)) : null;
    }

    private static async Task InsertClaimRequestAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string key, string requestHash, string? jobId, DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO print_job_claim_requests(idempotency_key, request_hash, job_id, created_at_utc)
            VALUES ($1,$2,$3,$4)
            """, connection, transaction);
        Add(command, key, requestHash);
        command.Parameters.AddWithValue(DbValue(jobId));
        Add(command, createdAtUtc);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<PrintJobSnapshot?> ReadPrintJobForUpdateAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, string jobId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT job_id, idempotency_key, label_type, template_id, template_version, state,
                   request_hash, request_json::text, result_json::text, version, created_at_utc, updated_at_utc,
                   claimed_by_station_id, claimed_by_operator_id, claim_idempotency_key, receipt_idempotency_key,
                   trace_order_id, trace_unit_id, trace_packaging_unit_id
            FROM print_jobs WHERE job_id=$1 FOR UPDATE
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        Add(command, jobId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadPrintJob(reader) : null;
    }

    private static async Task<PrintJobSnapshot?> ReadPrintJobAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, string jobId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT job_id, idempotency_key, label_type, template_id, template_version, state,
                   request_hash, request_json::text, result_json::text, version, created_at_utc, updated_at_utc,
                   claimed_by_station_id, claimed_by_operator_id, claim_idempotency_key, receipt_idempotency_key,
                   trace_order_id, trace_unit_id, trace_packaging_unit_id
            FROM print_jobs WHERE job_id=$1
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        Add(command, jobId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadPrintJob(reader) : null;
    }

    private static async Task<string?> ReadReceiptHashAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, string jobId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT receipt_request_hash FROM print_jobs WHERE job_id=$1", connection, transaction);
        Add(command, jobId);
        return (string?)await command.ExecuteScalarAsync(cancellationToken);
    }

    private static DateTimeOffset ReadUtc(NpgsqlDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));
}

public sealed class AuditEventRepository(NpgsqlDataSource dataSource)
{
    public async Task AppendAsync(AuditEventSnapshot auditEvent, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await TransactionalAudit.AppendAsync(connection, transaction, auditEvent, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}

internal static class TransactionalAudit
{
    internal static async Task AppendAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        AuditEventSnapshot? auditEvent, CancellationToken cancellationToken)
    {
        if (auditEvent == null) return;
        const string sql = """
            INSERT INTO audit_events
                (id, actor_id, station_id, shift_id, correlation_id, action, entity_type, entity_id, before_json, after_json, occurred_at_utc)
            VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11)
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        Add(command, auditEvent.Id, auditEvent.ActorId, auditEvent.StationId, auditEvent.ShiftId,
            auditEvent.CorrelationId, auditEvent.Action, auditEvent.EntityType, auditEvent.EntityId);
        command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, DbValue(auditEvent.BeforeJson));
        command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, DbValue(auditEvent.AfterJson));
        Add(command, auditEvent.OccurredAtUtc);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

public sealed class TraceabilityRepository(NpgsqlDataSource dataSource)
{
    public async Task<TraceabilitySnapshot?> QueryAsync(TraceabilityQueryType queryType, string queryValue,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await QueryAsync(connection, queryType, queryValue, cancellationToken);
    }

    internal async Task<TraceabilitySnapshot?> QueryAsync(NpgsqlConnection connection,
        TraceabilityQueryType queryType, string queryValue, CancellationToken cancellationToken)
    {
        queryValue = Required(queryValue, nameof(queryValue));
        var resolved = await ResolveAsync(connection, queryType, queryValue, cancellationToken);
        if (resolved == null) return null;

        var order = await ReadOrderAsync(connection, resolved.Value.OrderId, cancellationToken)
            ?? throw new InvalidOperationException("追溯订单无法读取。");
        var packagingUnitIds = await ReadPackagingUnitIdsAsync(connection, resolved.Value.OrderId,
            resolved.Value.ProductionUnitId, resolved.Value.PackagingUnitId, queryType, cancellationToken);
        var productionUnitIds = await ReadProductionUnitIdsAsync(connection, resolved.Value.OrderId,
            resolved.Value.ProductionUnitId, packagingUnitIds, queryType, cancellationToken);
        var productionUnits = await ReadProductionUnitsAsync(connection, productionUnitIds, cancellationToken);
        var stationPasses = await ReadStationPassesAsync(connection, productionUnitIds, cancellationToken);
        var packagingUnits = await ReadPackagingUnitsAsync(connection, packagingUnitIds, cancellationToken);
        var bindings = await ReadPackagingBindingsAsync(connection, packagingUnitIds, cancellationToken);
        var printIntents = await ReadPrintIntentsAsync(connection, packagingUnitIds, cancellationToken);
        var printJobs = await ReadPrintJobsAsync(connection, resolved.Value.OrderId, productionUnitIds,
            packagingUnitIds, cancellationToken);
        var entityIds = new[] { resolved.Value.OrderId }
            .Concat(productionUnitIds)
            .Concat(packagingUnitIds)
            .Concat(printJobs.Select(job => job.JobId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var auditEvents = await ReadAuditEventsAsync(connection, entityIds, cancellationToken);
        return new TraceabilitySnapshot(queryType, queryValue, order, productionUnits, stationPasses,
            packagingUnits, bindings, printIntents, printJobs, auditEvents);
    }

    private static async Task<(string OrderId, string? ProductionUnitId, string? PackagingUnitId)?> ResolveAsync(
        NpgsqlConnection connection, TraceabilityQueryType queryType, string value, CancellationToken cancellationToken)
    {
        var sql = queryType switch
        {
            TraceabilityQueryType.Order =>
                "SELECT id, NULL::text, NULL::text FROM production_orders WHERE id=$1 OR order_number=$1 LIMIT 1",
            TraceabilityQueryType.Imei =>
                "SELECT order_id, id, NULL::text FROM production_units WHERE identifiers_json->>'Imei'=$1 LIMIT 1",
            TraceabilityQueryType.SerialNumber =>
                "SELECT order_id, id, NULL::text FROM production_units WHERE identifiers_json->>'SerialNumber'=$1 LIMIT 1",
            TraceabilityQueryType.Carton =>
                "SELECT order_id, NULL::text, id FROM packaging_units WHERE unit_type='Carton' AND code=$1 LIMIT 1",
            TraceabilityQueryType.Pallet =>
                "SELECT order_id, NULL::text, id FROM packaging_units WHERE unit_type='Pallet' AND code=$1 LIMIT 1",
            _ => throw new ArgumentOutOfRangeException(nameof(queryType))
        };
        await using var command = new NpgsqlCommand(sql, connection);
        Add(command, value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2));
    }

    private static async Task<ProductionOrderSnapshot?> ReadOrderAsync(NpgsqlConnection connection, string orderId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT o.id, o.order_number, o.customer, o.product_model, o.color, o.planned_quantity,
                   o.valid_from_utc, o.valid_to_utc, o.status, o.version,
                   count(u.id) FILTER (WHERE u.status='Completed'),
                   count(u.id) FILTER (WHERE u.status IN ('Frozen','Scrapped'))
            FROM production_orders o LEFT JOIN production_units u ON u.order_id=o.id
            WHERE o.id=$1 GROUP BY o.id
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        Add(command, orderId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new ProductionOrderSnapshot(reader.GetString(0), reader.GetString(1), reader.GetString(2),
            reader.GetString(3), reader.GetString(4), reader.GetInt32(5), ReadNullableUtc(reader, 6),
            ReadNullableUtc(reader, 7), reader.GetString(8), reader.GetInt64(9),
            Convert.ToInt32(reader.GetInt64(10), CultureInfo.InvariantCulture),
            Convert.ToInt32(reader.GetInt64(11), CultureInfo.InvariantCulture));
    }

    private static async Task<string[]> ReadPackagingUnitIdsAsync(NpgsqlConnection connection, string orderId,
        string? productionUnitId, string? packagingUnitId, TraceabilityQueryType queryType,
        CancellationToken cancellationToken)
    {
        if (queryType == TraceabilityQueryType.Order)
            return await ReadStringArrayAsync(connection,
                "SELECT id FROM packaging_units WHERE order_id=$1 ORDER BY id", orderId, cancellationToken);
        const string sql = """
            WITH RECURSIVE related(id) AS (
                SELECT id FROM packaging_units
                WHERE ($1::text IS NOT NULL AND id=$1) OR ($2::text IS NOT NULL AND production_unit_id=$2)
                UNION
                SELECT CASE WHEN b.parent_id=r.id THEN b.child_id ELSE b.parent_id END
                FROM related r
                JOIN packaging_bindings b ON b.is_active AND (b.parent_id=r.id OR b.child_id=r.id)
            )
            SELECT id FROM related ORDER BY id
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(DbValue(packagingUnitId));
        command.Parameters.AddWithValue(DbValue(productionUnitId));
        return await ReadStringArrayAsync(command, cancellationToken);
    }

    private static async Task<string[]> ReadProductionUnitIdsAsync(NpgsqlConnection connection, string orderId,
        string? productionUnitId, string[] packagingUnitIds, TraceabilityQueryType queryType,
        CancellationToken cancellationToken)
    {
        if (queryType == TraceabilityQueryType.Order)
            return await ReadStringArrayAsync(connection,
                "SELECT id FROM production_units WHERE order_id=$1 ORDER BY id", orderId, cancellationToken);
        const string sql = """
            SELECT id FROM production_units
            WHERE id=$1::text OR id IN (
                SELECT production_unit_id FROM packaging_units
                WHERE id=ANY($2) AND production_unit_id IS NOT NULL)
            ORDER BY id
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(DbValue(productionUnitId));
        command.Parameters.AddWithValue(packagingUnitIds);
        return await ReadStringArrayAsync(command, cancellationToken);
    }

    private static async Task<List<TraceProductionUnitSnapshot>> ReadProductionUnitsAsync(
        NpgsqlConnection connection, string[] ids, CancellationToken cancellationToken)
    {
        if (ids.Length == 0) return [];
        await using var command = new NpgsqlCommand("""
            SELECT id, order_id, status, current_operation_id, identifiers_json::text, version
            FROM production_units WHERE id=ANY($1) ORDER BY id
            """, connection);
        command.Parameters.AddWithValue(ids);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<TraceProductionUnitSnapshot>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new TraceProductionUnitSnapshot(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), reader.GetInt64(5)));
        return result;
    }

    private static async Task<List<StationPassSnapshot>> ReadStationPassesAsync(NpgsqlConnection connection,
        string[] unitIds, CancellationToken cancellationToken)
    {
        if (unitIds.Length == 0) return [];
        await using var command = new NpgsqlCommand("""
            SELECT id, unit_id, order_id, route_id, operation_id, station_id, operator_id, occurred_at_utc,
                   idempotency_key, rework_order_id, rework_sequence
            FROM station_pass_records WHERE unit_id=ANY($1) ORDER BY occurred_at_utc, id
            """, connection);
        command.Parameters.AddWithValue(unitIds);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<StationPassSnapshot>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new StationPassSnapshot(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), ReadUtc(reader, 7),
                reader.GetString(8), reader.IsDBNull(9) ? "" : reader.GetString(9), reader.GetInt32(10), false));
        return result;
    }

    private static async Task<List<TracePackagingUnitSnapshot>> ReadPackagingUnitsAsync(NpgsqlConnection connection,
        string[] ids, CancellationToken cancellationToken)
    {
        if (ids.Length == 0) return [];
        await using var command = new NpgsqlCommand("""
            SELECT id, order_id, unit_type, code, product_model, color, capacity, status, version, production_unit_id
            FROM packaging_units WHERE id=ANY($1) ORDER BY unit_type, code
            """, connection);
        command.Parameters.AddWithValue(ids);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<TracePackagingUnitSnapshot>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new TracePackagingUnitSnapshot(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetInt32(6), reader.GetString(7),
                reader.GetInt64(8), reader.IsDBNull(9) ? null : reader.GetString(9)));
        return result;
    }

    private static async Task<List<TracePackagingBindingSnapshot>> ReadPackagingBindingsAsync(
        NpgsqlConnection connection, string[] ids, CancellationToken cancellationToken)
    {
        if (ids.Length == 0) return [];
        await using var command = new NpgsqlCommand("""
            SELECT parent_id, child_id, bound_at_utc, operator_id, is_active
            FROM packaging_bindings WHERE parent_id=ANY($1) AND child_id=ANY($1)
            ORDER BY bound_at_utc, parent_id, child_id
            """, connection);
        command.Parameters.AddWithValue(ids);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<TracePackagingBindingSnapshot>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new TracePackagingBindingSnapshot(reader.GetString(0), reader.GetString(1), ReadUtc(reader, 2),
                reader.GetString(3), reader.GetBoolean(4)));
        return result;
    }

    private static async Task<List<PackagingPrintIntentSnapshot>> ReadPrintIntentsAsync(NpgsqlConnection connection,
        string[] ids, CancellationToken cancellationToken)
    {
        if (ids.Length == 0) return [];
        await using var command = new NpgsqlCommand("""
            SELECT id, packaging_unit_id, label_type, fields_json::text, created_at_utc
            FROM packaging_print_intents WHERE packaging_unit_id=ANY($1) ORDER BY created_at_utc, id
            """, connection);
        command.Parameters.AddWithValue(ids);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<PackagingPrintIntentSnapshot>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new PackagingPrintIntentSnapshot(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), ReadUtc(reader, 4)));
        return result;
    }

    private static async Task<List<PrintJobSnapshot>> ReadPrintJobsAsync(NpgsqlConnection connection, string orderId,
        string[] unitIds, string[] packagingUnitIds, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT job_id, idempotency_key, label_type, template_id, template_version, state,
                   request_hash, request_json::text, result_json::text, version, created_at_utc, updated_at_utc,
                   claimed_by_station_id, claimed_by_operator_id, claim_idempotency_key, receipt_idempotency_key,
                   trace_order_id, trace_unit_id, trace_packaging_unit_id
            FROM print_jobs
            WHERE trace_order_id=$1 OR trace_unit_id=ANY($2) OR trace_packaging_unit_id=ANY($3)
            ORDER BY created_at_utc, job_id
            """, connection);
        Add(command, orderId);
        command.Parameters.AddWithValue(unitIds);
        command.Parameters.AddWithValue(packagingUnitIds);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<PrintJobSnapshot>();
        while (await reader.ReadAsync(cancellationToken)) result.Add(ReadPrintJob(reader));
        return result;
    }

    private static async Task<List<AuditEventSnapshot>> ReadAuditEventsAsync(NpgsqlConnection connection,
        string[] entityIds, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT id, actor_id, station_id, shift_id, correlation_id, action, entity_type, entity_id,
                   before_json::text, after_json::text, occurred_at_utc
            FROM audit_events WHERE entity_id=ANY($1) ORDER BY occurred_at_utc, id
            """, connection);
        command.Parameters.AddWithValue(entityIds);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<AuditEventSnapshot>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new AuditEventSnapshot(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetString(9),
                ReadUtc(reader, 10)));
        return result;
    }

    private static async Task<string[]> ReadStringArrayAsync(NpgsqlConnection connection, string sql, string value,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        Add(command, value);
        return await ReadStringArrayAsync(command, cancellationToken);
    }

    private static async Task<string[]> ReadStringArrayAsync(NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<string>();
        while (await reader.ReadAsync(cancellationToken)) result.Add(reader.GetString(0));
        return result.ToArray();
    }

    private static DateTimeOffset ReadUtc(NpgsqlDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    private static DateTimeOffset? ReadNullableUtc(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : ReadUtc(reader, ordinal);

    private static PrintJobSnapshot ReadPrintJob(NpgsqlDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
        reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetString(8),
        reader.GetInt64(9), ReadUtc(reader, 10), ReadUtc(reader, 11),
        reader.IsDBNull(12) ? "" : reader.GetString(12), reader.IsDBNull(13) ? "" : reader.GetString(13),
        reader.IsDBNull(14) ? "" : reader.GetString(14), reader.IsDBNull(15) ? null : reader.GetString(15),
        reader.IsDBNull(16) ? null : reader.GetString(16), reader.IsDBNull(17) ? null : reader.GetString(17),
        reader.IsDBNull(18) ? null : reader.GetString(18));
}

internal static class PostgresRepositoryHelpers
{
    public static void Add(NpgsqlCommand command, params object[] values)
    {
        foreach (var value in values) command.Parameters.AddWithValue(value ?? DBNull.Value);
    }

    public static object DbValue(object? value) => value ?? DBNull.Value;

    public static string Required(string value, string parameterName)
    {
        var normalized = value?.Trim() ?? "";
        if (normalized.Length == 0) throw new ArgumentException("值不能为空。", parameterName);
        return normalized;
    }

    public static void ValidateUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero) throw new ArgumentException("时间必须使用 UTC。", parameterName);
    }
}
