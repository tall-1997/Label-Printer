using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BarTenderPrinter.Domain.Archiving;
using BarTenderPrinter.Domain.Common;
using BarTenderPrinter.Domain.Quality;
using BarTenderPrinter.Domain.Rework;
using BarTenderPrinter.Domain.Shipping;
using Npgsql;
using NpgsqlTypes;
using static BarTenderPrinter.Persistence.PostgresRepositoryHelpers;

namespace BarTenderPrinter.Persistence;

public sealed class InspectionRepository(NpgsqlDataSource dataSource)
{
    public async Task<InspectionLotSnapshot> CreateLotAsync(InspectionLot lot, DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken = default,
        Func<InspectionLotSnapshot, AuditEventSnapshot>? auditFactory = null)
    {
        ValidateUtc(createdAtUtc, nameof(createdAtUtc));
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        const string sql = """
            INSERT INTO inspection_lots
                (id, order_id, inspection_type, sample_rule, sample_unit_ids_json, status, version, created_at_utc)
            SELECT $1,$2,$3,$4,$5,$6,$7,$8
            WHERE NOT EXISTS (
                SELECT 1 FROM unnest($9::text[]) sample_id
                LEFT JOIN production_units u ON u.id=sample_id AND u.order_id=$2
                WHERE u.id IS NULL)
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        Add(command, lot.Id.Value, lot.OrderId.Value, lot.InspectionType, lot.SampleRule);
        command.Parameters.AddWithValue(NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(lot.SampleUnitIds.Select(id => id.Value)));
        Add(command, lot.Status.ToString(), lot.Version, createdAtUtc);
        var sampleIds = lot.SampleUnitIds.Select(id => id.Value).ToArray();
        command.Parameters.AddWithValue(sampleIds);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new PersistenceBusinessException("INSPECTION_SAMPLE_ORDER_MISMATCH", "抽检样本必须全部属于抽检订单。");
        await using var samples = new NpgsqlCommand("""
            INSERT INTO inspection_lot_samples(lot_id, order_id, unit_id)
            SELECT $1,$2,unnest($3::text[])
            """, connection, transaction);
        Add(samples, lot.Id.Value, lot.OrderId.Value);
        samples.Parameters.AddWithValue(sampleIds);
        await samples.ExecuteNonQueryAsync(cancellationToken);
        var result = new InspectionLotSnapshot(lot.Id.Value, lot.OrderId.Value, lot.InspectionType, lot.SampleRule,
            JsonSerializer.Serialize(lot.SampleUnitIds.Select(id => id.Value)), lot.Status.ToString(), lot.Version, createdAtUtc);
        await TransactionalAudit.AppendAsync(connection, transaction, auditFactory?.Invoke(result), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<InspectionResultSnapshot> AddResultAsync(string lotId, string unitId, string itemCode,
        InspectionOutcome outcome, string defectCode, string responsibleOperationId, string remarks,
        IdempotencyKey idempotencyKey, string requestHash, DateTimeOffset inspectedAtUtc,
        CancellationToken cancellationToken = default,
        Func<InspectionResultSnapshot, AuditEventSnapshot>? auditFactory = null)
    {
        ValidateUtc(inspectedAtUtc, nameof(inspectedAtUtc));
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await AcquireIdempotencyLockAsync(connection, transaction, idempotencyKey.Value, cancellationToken);
        var existing = await ReadResultAsync(connection, transaction, idempotencyKey.Value, cancellationToken);
        if (existing != null)
        {
            if (existing.Value.Hash != requestHash) throw IdempotencyConflict("检验结果");
            await transaction.CommitAsync(cancellationToken);
            return existing.Value.Result with { IsReplay = true };
        }

        await using var lotCommand = new NpgsqlCommand(
            "SELECT status, sample_unit_ids_json ? $2 FROM inspection_lots WHERE id=$1 FOR UPDATE", connection, transaction);
        Add(lotCommand, Required(lotId, nameof(lotId)), Required(unitId, nameof(unitId)));
        await using var lotReader = await lotCommand.ExecuteReaderAsync(cancellationToken);
        if (!await lotReader.ReadAsync(cancellationToken)) throw new KeyNotFoundException("抽检单不存在。");
        var status = lotReader.GetString(0);
        var isSample = lotReader.GetBoolean(1);
        await lotReader.CloseAsync();
        if (status != InspectionLotStatus.Open.ToString())
            throw new PersistenceBusinessException("INSPECTION_LOT_CLOSED", "抽检单已完成判定。");
        if (!isSample) throw new PersistenceBusinessException("INSPECTION_SAMPLE_MISMATCH", "生产单元不属于抽检样本。");
        if (outcome == InspectionOutcome.Failed && string.IsNullOrWhiteSpace(defectCode))
            throw new PersistenceBusinessException("DEFECT_CODE_REQUIRED", "失败检验结果必须填写不良代码。");

        var result = new InspectionResultSnapshot(EntityId.New().Value, lotId, unitId,
            Required(itemCode, nameof(itemCode)), outcome.ToString(), defectCode.Trim(),
            Required(responsibleOperationId, nameof(responsibleOperationId)), remarks.Trim(), inspectedAtUtc, false);
        await using var insert = new NpgsqlCommand("""
            INSERT INTO inspection_results
                (id, lot_id, unit_id, item_code, outcome, defect_code, responsible_operation_id, remarks,
                 inspected_at_utc, idempotency_key, request_hash)
            VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11)
            """, connection, transaction);
        Add(insert, result.Id, result.LotId, result.UnitId, result.ItemCode, result.Outcome, result.DefectCode,
            result.ResponsibleOperationId, result.Remarks, result.InspectedAtUtc, idempotencyKey.Value, requestHash);
        await insert.ExecuteNonQueryAsync(cancellationToken);
        await TransactionalAudit.AppendAsync(connection, transaction, auditFactory?.Invoke(result), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<InspectionLotSnapshot> CompleteLotAsync(string lotId, long expectedVersion,
        CancellationToken cancellationToken = default) =>
        await CompleteLotAsync(lotId, expectedVersion, new IdempotencyKey(Guid.NewGuid().ToString("N")),
            Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, cancellationToken);

    public async Task<InspectionLotSnapshot> CompleteLotAsync(string lotId, long expectedVersion,
        IdempotencyKey key, string requestHash, DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken = default,
        Func<InspectionLotSnapshot, AuditEventSnapshot>? auditFactory = null)
    {
        ValidateUtc(completedAtUtc, nameof(completedAtUtc));
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await AcquireIdempotencyLockAsync(connection, transaction, key.Value, cancellationToken);
        await using (var replayCommand = new NpgsqlCommand("""
            SELECT c.request_hash, c.lot_id
            FROM inspection_lot_commands c WHERE c.idempotency_key=$1
            """, connection, transaction))
        {
            Add(replayCommand, key.Value);
            await using var replayReader = await replayCommand.ExecuteReaderAsync(cancellationToken);
            if (await replayReader.ReadAsync(cancellationToken))
            {
                if (replayReader.GetString(0) != requestHash || replayReader.GetString(1) != lotId)
                    throw IdempotencyConflict("抽检完成");
                await replayReader.CloseAsync();
                var replay = await ReadLotAsync(connection, transaction, lotId, false, cancellationToken)
                    ?? throw new KeyNotFoundException("抽检单不存在。");
                await transaction.CommitAsync(cancellationToken);
                return replay with { IsReplay = true };
            }
        }
        var lot = await ReadLotAsync(connection, transaction, lotId, true, cancellationToken)
            ?? throw new KeyNotFoundException("抽检单不存在。");
        if (lot.Version != expectedVersion) throw new PersistenceConcurrencyException("InspectionLot", lotId);
        if (lot.Status != InspectionLotStatus.Open.ToString())
            throw new PersistenceBusinessException("INSPECTION_LOT_CLOSED", "抽检单已完成判定。");
        await using var outcome = new NpgsqlCommand(
            "SELECT count(*), bool_or(outcome='Failed') FROM inspection_results WHERE lot_id=$1", connection, transaction);
        Add(outcome, lotId);
        await using var reader = await outcome.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var count = reader.GetInt64(0);
        var failed = !reader.IsDBNull(1) && reader.GetBoolean(1);
        await reader.CloseAsync();
        if (count == 0) throw new PersistenceBusinessException("INSPECTION_RESULTS_REQUIRED", "抽检单尚无检验结果。");
        var next = failed ? InspectionLotStatus.Failed : InspectionLotStatus.Passed;
        await using var update = new NpgsqlCommand(
            "UPDATE inspection_lots SET status=$1, version=version+1 WHERE id=$2 AND version=$3", connection, transaction);
        Add(update, next.ToString(), lotId, expectedVersion);
        if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new PersistenceConcurrencyException("InspectionLot", lotId);
        if (failed)
        {
            await using var hold = new NpgsqlCommand("""
                WITH RECURSIVE related(id) AS (
                    SELECT id FROM packaging_units
                    WHERE production_unit_id IN (SELECT unit_id FROM inspection_results WHERE lot_id=$1)
                    UNION
                    SELECT b.parent_id FROM packaging_bindings b JOIN related r ON b.child_id=r.id WHERE b.is_active
                )
                INSERT INTO packaging_quality_holds(lot_id, packaging_unit_id, previous_status)
                SELECT $1, p.id, COALESCE(existing.previous_status, p.status)
                FROM packaging_units p JOIN related r ON r.id=p.id
                LEFT JOIN LATERAL (
                    SELECT h.previous_status FROM packaging_quality_holds h
                    WHERE h.packaging_unit_id=p.id ORDER BY h.lot_id LIMIT 1
                ) existing ON true
                ON CONFLICT (lot_id, packaging_unit_id) DO NOTHING
                """, connection, transaction);
            Add(hold, lotId);
            await hold.ExecuteNonQueryAsync(cancellationToken);
            await using var freeze = new NpgsqlCommand("""
                UPDATE packaging_units SET status='Frozen', version=version+1
                WHERE id IN (SELECT packaging_unit_id FROM packaging_quality_holds WHERE lot_id=$1)
                  AND status <> 'Frozen'
                """, connection, transaction);
            Add(freeze, lotId);
            await freeze.ExecuteNonQueryAsync(cancellationToken);
        }
        await using var command = new NpgsqlCommand("""
            INSERT INTO inspection_lot_commands(idempotency_key, request_hash, lot_id, result_status, created_at_utc)
            VALUES ($1,$2,$3,$4,$5)
            """, connection, transaction);
        Add(command, key.Value, requestHash, lotId, next.ToString(), completedAtUtc);
        await command.ExecuteNonQueryAsync(cancellationToken);
        var result = lot with { Status = next.ToString(), Version = expectedVersion + 1 };
        await TransactionalAudit.AppendAsync(connection, transaction, auditFactory?.Invoke(result), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<DispositionSnapshot> ApplyDispositionAsync(string lotId, DispositionDecision decision,
        string reasonCode, string approvedBy, IdempotencyKey idempotencyKey, string requestHash,
        DateTimeOffset approvedAtUtc, CancellationToken cancellationToken = default,
        Func<DispositionSnapshot, AuditEventSnapshot>? auditFactory = null)
    {
        ValidateUtc(approvedAtUtc, nameof(approvedAtUtc));
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await AcquireIdempotencyLockAsync(connection, transaction, idempotencyKey.Value, cancellationToken);
        var existing = await ReadDispositionAsync(connection, transaction, idempotencyKey.Value, cancellationToken);
        if (existing != null)
        {
            if (existing.Value.Hash != requestHash) throw IdempotencyConflict("质量处置");
            await transaction.CommitAsync(cancellationToken);
            return existing.Value.Result with { IsReplay = true };
        }
        var lot = await ReadLotAsync(connection, transaction, lotId, true, cancellationToken)
            ?? throw new KeyNotFoundException("抽检单不存在。");
        if (lot.Status != InspectionLotStatus.Failed.ToString())
            throw new PersistenceBusinessException("INSPECTION_DISPOSITION_NOT_REQUIRED", "当前抽检单无需处置。");
        var result = new DispositionSnapshot(EntityId.New().Value, lotId, decision.ToString(),
            Required(reasonCode, nameof(reasonCode)), Required(approvedBy, nameof(approvedBy)), approvedAtUtc, false);
        await using var insert = new NpgsqlCommand("""
            INSERT INTO dispositions
                (id, lot_id, decision, reason_code, approved_by, approved_at_utc, idempotency_key, request_hash)
            VALUES ($1,$2,$3,$4,$5,$6,$7,$8)
            """, connection, transaction);
        Add(insert, result.Id, result.LotId, result.Decision, result.ReasonCode, result.ApprovedBy,
            result.ApprovedAtUtc, idempotencyKey.Value, requestHash);
        await insert.ExecuteNonQueryAsync(cancellationToken);
        await using var update = new NpgsqlCommand(
            "UPDATE inspection_lots SET status='Disposed', version=version+1 WHERE id=$1", connection, transaction);
        Add(update, lotId);
        await update.ExecuteNonQueryAsync(cancellationToken);
        if (decision == DispositionDecision.Release)
        {
            await using var release = new NpgsqlCommand("""
                UPDATE packaging_units p SET status=h.previous_status, version=version+1
                FROM packaging_quality_holds h
                WHERE h.lot_id=$1 AND h.packaging_unit_id=p.id AND p.status='Frozen'
                  AND NOT EXISTS (
                      SELECT 1 FROM packaging_quality_holds other
                      LEFT JOIN dispositions d ON d.lot_id=other.lot_id
                      WHERE other.packaging_unit_id=p.id AND other.lot_id<>$1
                        AND (d.id IS NULL OR d.decision<>'Release'))
                """, connection, transaction);
            Add(release, lotId);
            await release.ExecuteNonQueryAsync(cancellationToken);
        }
        await TransactionalAudit.AppendAsync(connection, transaction, auditFactory?.Invoke(result), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private static async Task<InspectionLotSnapshot?> ReadLotAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, string id, bool forUpdate, CancellationToken cancellationToken)
    {
        var sql = """
            SELECT id, order_id, inspection_type, sample_rule, sample_unit_ids_json::text, status, version, created_at_utc
            FROM inspection_lots WHERE id=$1
            """ + (forUpdate ? " FOR UPDATE" : "");
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        Add(command, Required(id, nameof(id)));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new InspectionLotSnapshot(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetInt64(6), ReadUtc(reader, 7))
            : null;
    }

    private static async Task<(InspectionResultSnapshot Result, string Hash)?> ReadResultAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, string key, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT id, lot_id, unit_id, item_code, outcome, defect_code, responsible_operation_id, remarks,
                   inspected_at_utc, request_hash FROM inspection_results WHERE idempotency_key=$1
            """, connection, transaction);
        Add(command, key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return (new InspectionResultSnapshot(reader.GetString(0), reader.GetString(1), reader.GetString(2),
            reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
            ReadUtc(reader, 8), false), reader.GetString(9));
    }

    private static async Task<(DispositionSnapshot Result, string Hash)?> ReadDispositionAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, string key, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT id, lot_id, decision, reason_code, approved_by, approved_at_utc, request_hash
            FROM dispositions WHERE idempotency_key=$1
            """, connection, transaction);
        Add(command, key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return (new DispositionSnapshot(reader.GetString(0), reader.GetString(1), reader.GetString(2),
            reader.GetString(3), reader.GetString(4), ReadUtc(reader, 5), false), reader.GetString(6));
    }

    private static PersistenceBusinessException IdempotencyConflict(string operation) =>
        new("IDEMPOTENCY_CONFLICT", $"幂等键已用于其他{operation}请求。");
    private static async Task AcquireIdempotencyLockAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string key, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended($1, 12))", connection, transaction);
        Add(command, key);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
    private static DateTimeOffset ReadUtc(NpgsqlDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));
}

public sealed class ReworkOrderRepository(NpgsqlDataSource dataSource)
{
    public async Task<ReworkOrderSnapshot> CreateAsync(ReworkOrder order, CancellationToken cancellationToken = default,
        Func<ReworkOrderSnapshot, AuditEventSnapshot>? auditFactory = null)
    {
        const string sql = """
            INSERT INTO rework_orders
                (id, production_unit_id, route_id, reason_code, start_operation_id, status, sequence, version, order_id)
            SELECT $1,$2,$3,$4,$5,$6,$7,$8,u.order_id
            FROM production_units u
            JOIN manufacturing_routes r ON r.id=$3 AND r.order_id=u.order_id AND r.route_type='Rework'
            JOIN manufacturing_operations op ON op.route_id=r.id AND op.operation_id=$5
            WHERE u.id=$2
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        Add(command, order.Id.Value, order.ProductionUnitId.Value, order.RouteId.Value, order.ReasonCode,
            order.StartOperationId, order.Status.ToString(), order.Sequence, order.Version);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new PersistenceBusinessException("REWORK_CONTEXT_MISMATCH", "返工生产单元、返工路线和起始工序必须存在且属于同一订单。");
        var result = Map(order, null);
        await TransactionalAudit.AppendAsync(connection, transaction, auditFactory?.Invoke(result), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public Task<ReworkOrderSnapshot> ApproveAsync(string id, string actorId, IdempotencyKey key, string hash,
        DateTimeOffset utcNow, CancellationToken cancellationToken = default,
        Func<ReworkOrderSnapshot, AuditEventSnapshot>? auditFactory = null) =>
        ChangeStateAsync(id, ReworkOrderStatus.Draft, ReworkOrderStatus.Approved, actorId, key, hash, utcNow, false, cancellationToken, auditFactory);

    public Task<ReworkOrderSnapshot> ActivateAsync(string id, string actorId, IdempotencyKey key, string hash,
        DateTimeOffset utcNow, CancellationToken cancellationToken = default,
        Func<ReworkOrderSnapshot, AuditEventSnapshot>? auditFactory = null) =>
        ChangeStateAsync(id, ReworkOrderStatus.Approved, ReworkOrderStatus.Active, actorId, key, hash, utcNow, false, cancellationToken, auditFactory);

    public Task<ReworkOrderSnapshot> CompleteAsync(string id, string actorId, IdempotencyKey key, string hash,
        DateTimeOffset utcNow, CancellationToken cancellationToken = default,
        Func<ReworkOrderSnapshot, AuditEventSnapshot>? auditFactory = null) =>
        ChangeStateAsync(id, ReworkOrderStatus.Active, ReworkOrderStatus.Completed, actorId, key, hash, utcNow, true, cancellationToken, auditFactory);

    private async Task<ReworkOrderSnapshot> ChangeStateAsync(string id, ReworkOrderStatus expected,
        ReworkOrderStatus next, string actorId, IdempotencyKey key, string hash, DateTimeOffset utcNow,
        bool validateRoute, CancellationToken cancellationToken,
        Func<ReworkOrderSnapshot, AuditEventSnapshot>? auditFactory)
    {
        ValidateUtc(utcNow, nameof(utcNow));
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await AcquireIdempotencyLockAsync(connection, transaction, key.Value, cancellationToken);
        var replay = await ReadCommandAsync(connection, transaction, key.Value, cancellationToken);
        if (replay != null)
        {
            if (replay.Value.Hash != hash) throw new PersistenceBusinessException("IDEMPOTENCY_CONFLICT", "幂等键已用于其他返工请求。");
            var replayOrder = await ReadAsync(connection, transaction, replay.Value.OrderId, false, cancellationToken)
                ?? throw new KeyNotFoundException("返工任务不存在。");
            await transaction.CommitAsync(cancellationToken);
            return replayOrder with { IsReplay = true };
        }
        var order = await ReadAsync(connection, transaction, id, true, cancellationToken)
            ?? throw new KeyNotFoundException("返工任务不存在。");
        if (order.Status != expected.ToString())
            throw new PersistenceBusinessException("REWORK_STATE_CONFLICT", "返工任务状态不允许当前操作。");
        if (validateRoute)
        {
            await using var check = new NpgsqlCommand("""
                SELECT NOT EXISTS (
                    SELECT 1 FROM manufacturing_operations op
                    WHERE op.route_id=$1
                      AND op.sequence >= (SELECT sequence FROM manufacturing_operations
                                          WHERE route_id=$1 AND operation_id=$4)
                      AND NOT EXISTS (
                        SELECT 1 FROM station_pass_records p
                        WHERE p.unit_id=$2 AND p.route_id=$1 AND p.operation_id=op.operation_id AND p.rework_order_id=$3))
                """, connection, transaction);
            Add(check, order.RouteId, order.ProductionUnitId, order.Id, order.StartOperationId);
            if (!(bool)(await check.ExecuteScalarAsync(cancellationToken) ?? false))
                throw new PersistenceBusinessException("REWORK_ROUTE_INCOMPLETE", "返工路线仍有必需工序未通过。");
        }
        var approval = next == ReworkOrderStatus.Approved;
        var completion = next == ReworkOrderStatus.Completed;
        await using var update = new NpgsqlCommand("""
            UPDATE rework_orders SET status=$1, version=version+1,
                approved_by=CASE WHEN $2 THEN $3 ELSE approved_by END,
                approved_at_utc=CASE WHEN $2 THEN $4 ELSE approved_at_utc END,
                closed_by=CASE WHEN $5 THEN $3 ELSE closed_by END,
                closed_at_utc=CASE WHEN $5 THEN $4 ELSE closed_at_utc END
            WHERE id=$6 AND version=$7
            """, connection, transaction);
        Add(update, next.ToString(), approval, Required(actorId, nameof(actorId)), utcNow, completion, id, order.Version);
        if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new PersistenceConcurrencyException("ReworkOrder", id);
        if (next == ReworkOrderStatus.Active)
        {
            await using var unit = new NpgsqlCommand(
                "UPDATE production_units SET status='Active', version=version+1 WHERE id=$1", connection, transaction);
            Add(unit, order.ProductionUnitId);
            await unit.ExecuteNonQueryAsync(cancellationToken);
        }
        await using var command = new NpgsqlCommand("""
            INSERT INTO rework_order_commands(idempotency_key, request_hash, rework_order_id, result_status, created_at_utc)
            VALUES ($1,$2,$3,$4,$5)
            """, connection, transaction);
        Add(command, key.Value, hash, id, next.ToString(), utcNow);
        await command.ExecuteNonQueryAsync(cancellationToken);
        var result = order with
        {
            Status = next.ToString(), Version = order.Version + 1,
            ApprovedBy = approval ? actorId : order.ApprovedBy,
            ApprovedAtUtc = approval ? utcNow : order.ApprovedAtUtc,
            ClosedBy = completion ? actorId : order.ClosedBy,
            ClosedAtUtc = completion ? utcNow : order.ClosedAtUtc
        };
        await TransactionalAudit.AppendAsync(connection, transaction, auditFactory?.Invoke(result), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private static async Task<ReworkOrderSnapshot?> ReadAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string id, bool forUpdate, CancellationToken cancellationToken)
    {
        var sql = """
            SELECT id, production_unit_id, route_id, reason_code, start_operation_id, status, sequence,
                   approved_by, approved_at_utc, closed_by, closed_at_utc, version
            FROM rework_orders WHERE id=$1
            """ + (forUpdate ? " FOR UPDATE" : "");
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        Add(command, Required(id, nameof(id)));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new ReworkOrderSnapshot(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.GetString(5), reader.GetInt32(6), reader.GetString(7), ReadNullableUtc(reader, 8),
            reader.GetString(9), ReadNullableUtc(reader, 10), reader.GetInt64(11));
    }

    private static async Task<(string Hash, string OrderId)?> ReadCommandAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, string key, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT request_hash, rework_order_id FROM rework_order_commands WHERE idempotency_key=$1", connection, transaction);
        Add(command, key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? (reader.GetString(0), reader.GetString(1)) : null;
    }

    private static ReworkOrderSnapshot Map(ReworkOrder order, DateTimeOffset? _) => new(order.Id.Value,
        order.ProductionUnitId.Value, order.RouteId.Value, order.ReasonCode, order.StartOperationId,
        order.Status.ToString(), order.Sequence, order.ApprovedBy, order.ApprovedAtUtc, order.ClosedBy,
        order.ClosedAtUtc, order.Version);
    private static DateTimeOffset? ReadNullableUtc(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal)
        ? null : new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));
    private static async Task AcquireIdempotencyLockAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string key, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended($1, 12))", connection, transaction);
        Add(command, key);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

public sealed class ShipmentRepository(NpgsqlDataSource dataSource)
{
    public async Task<ShipmentSnapshot> CreateAsync(Shipment shipment, DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken = default,
        Func<ShipmentSnapshot, AuditEventSnapshot>? auditFactory = null)
    {
        ValidateUtc(createdAtUtc, nameof(createdAtUtc));
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            INSERT INTO shipments
                (id, order_id, customer, planned_quantity, delivery_reference, status, version, created_at_utc)
            SELECT $1,$2,$3,$4,$5,$6,$7,$8 FROM production_orders o
            WHERE o.id=$2 AND o.customer=$3
            """, connection, transaction);
        Add(command, shipment.Id.Value, shipment.OrderId.Value, shipment.Customer, shipment.PlannedQuantity,
            shipment.DeliveryReference, shipment.Status.ToString(), shipment.Version, createdAtUtc);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new PersistenceBusinessException("SHIPMENT_ORDER_MISMATCH", "出库单客户与生产订单不匹配。");
        var result = new ShipmentSnapshot(shipment.Id.Value, shipment.OrderId.Value, shipment.Customer,
            shipment.PlannedQuantity, shipment.DeliveryReference, shipment.Status.ToString(), shipment.Version, 0, createdAtUtc);
        await TransactionalAudit.AppendAsync(connection, transaction, auditFactory?.Invoke(result), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<ShipmentItemSnapshot> AddCartonAsync(string shipmentId, string cartonId, string operatorId,
        IdempotencyKey key, string hash, DateTimeOffset scannedAtUtc, CancellationToken cancellationToken = default,
        Func<ShipmentItemSnapshot, AuditEventSnapshot>? auditFactory = null)
    {
        ValidateUtc(scannedAtUtc, nameof(scannedAtUtc));
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await AcquireIdempotencyLockAsync(connection, transaction, key.Value, cancellationToken);
        var existing = await ReadItemAsync(connection, transaction, key.Value, cancellationToken);
        if (existing != null)
        {
            if (existing.Value.Hash != hash) throw new PersistenceBusinessException("IDEMPOTENCY_CONFLICT", "幂等键已用于其他出库扫描请求。");
            await transaction.CommitAsync(cancellationToken);
            return existing.Value.Item with { IsReplay = true };
        }
        await using var shipmentCommand = new NpgsqlCommand(
            "SELECT order_id, status, version FROM shipments WHERE id=$1 FOR UPDATE", connection, transaction);
        Add(shipmentCommand, Required(shipmentId, nameof(shipmentId)));
        await using var shipmentReader = await shipmentCommand.ExecuteReaderAsync(cancellationToken);
        if (!await shipmentReader.ReadAsync(cancellationToken)) throw new KeyNotFoundException("出库单不存在。");
        var orderId = shipmentReader.GetString(0);
        var status = shipmentReader.GetString(1);
        var version = shipmentReader.GetInt64(2);
        await shipmentReader.CloseAsync();
        if (status == ShipmentStatus.Confirmed.ToString())
            throw new PersistenceBusinessException("SHIPMENT_STATE_CONFLICT", "已确认出库单不可变更。");
        await using var carton = new NpgsqlCommand("""
            WITH RECURSIVE descendants(id) AS (
                SELECT id FROM packaging_units WHERE id=$1 AND unit_type='Carton' AND status='Closed' AND order_id=$2
                UNION ALL
                SELECT b.child_id FROM packaging_bindings b JOIN descendants d ON b.parent_id=d.id WHERE b.is_active
            )
            SELECT EXISTS(SELECT 1 FROM descendants WHERE id=$1),
                   count(*) FILTER (WHERE u.unit_type='Body'),
                   EXISTS(
                       SELECT 1 FROM descendants d
                       JOIN packaging_units p ON p.id=d.id
                       JOIN inspection_results r ON r.unit_id=p.production_unit_id
                       JOIN inspection_lots l ON l.id=r.lot_id
                       WHERE l.status='Failed')
            FROM descendants d JOIN packaging_units u ON u.id=d.id
            """, connection, transaction);
        Add(carton, Required(cartonId, nameof(cartonId)), orderId);
        await using var cartonReader = await carton.ExecuteReaderAsync(cancellationToken);
        await cartonReader.ReadAsync(cancellationToken);
        var valid = cartonReader.GetBoolean(0);
        var quantity = Convert.ToInt32(cartonReader.GetInt64(1), CultureInfo.InvariantCulture);
        var qualityHold = cartonReader.GetBoolean(2);
        await cartonReader.CloseAsync();
        if (!valid || quantity == 0) throw new PersistenceBusinessException("SHIPMENT_CARTON_INVALID", "卡通箱状态、订单或包装明细无效。");
        if (qualityHold) throw new PersistenceBusinessException("QUALITY_HOLD", "卡通箱存在待处置质量冻结。");
        var item = new ShipmentItemSnapshot(shipmentId, cartonId, quantity, scannedAtUtc,
            Required(operatorId, nameof(operatorId)), false);
        await using var insert = new NpgsqlCommand("""
            INSERT INTO shipment_items
                (shipment_id, carton_id, quantity, scanned_at_utc, operator_id, idempotency_key, request_hash)
            VALUES ($1,$2,$3,$4,$5,$6,$7)
            """, connection, transaction);
        Add(insert, shipmentId, cartonId, quantity, scannedAtUtc, item.OperatorId, key.Value, hash);
        try { await insert.ExecuteNonQueryAsync(cancellationToken); }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        { throw new PersistenceBusinessException("CARTON_ALREADY_SHIPPED", "卡通箱已加入其他出库单。"); }
        await using var update = new NpgsqlCommand(
            "UPDATE shipments SET status='PendingConfirmation', version=version+1 WHERE id=$1 AND version=$2", connection, transaction);
        Add(update, shipmentId, version);
        if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new PersistenceConcurrencyException("Shipment", shipmentId);
        await TransactionalAudit.AppendAsync(connection, transaction, auditFactory?.Invoke(item), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return item;
    }

    public async Task<ShipmentSnapshot> ConfirmAsync(string shipmentId, string actorId, IdempotencyKey key,
        string hash, DateTimeOffset utcNow, CancellationToken cancellationToken = default,
        Func<ShipmentSnapshot, AuditEventSnapshot>? auditFactory = null)
    {
        ValidateUtc(utcNow, nameof(utcNow));
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await AcquireIdempotencyLockAsync(connection, transaction, key.Value, cancellationToken);
        var replay = await ReadCommandAsync(connection, transaction, key.Value, cancellationToken);
        if (replay != null)
        {
            if (replay.Value.Hash != hash) throw new PersistenceBusinessException("IDEMPOTENCY_CONFLICT", "幂等键已用于其他出库确认请求。");
            var replayShipment = await ReadAsync(connection, transaction, replay.Value.ShipmentId, false, cancellationToken)
                ?? throw new KeyNotFoundException("出库单不存在。");
            await transaction.CommitAsync(cancellationToken);
            return replayShipment with { IsReplay = true };
        }
        var shipment = await ReadAsync(connection, transaction, shipmentId, true, cancellationToken)
            ?? throw new KeyNotFoundException("出库单不存在。");
        if (shipment.Status != ShipmentStatus.PendingConfirmation.ToString())
            throw new PersistenceBusinessException("SHIPMENT_STATE_CONFLICT", "出库单当前不可确认。");
        if (shipment.ActualQuantity != shipment.PlannedQuantity)
            throw new PersistenceBusinessException("SHIPMENT_QUANTITY_MISMATCH", "实际数量与计划数量不一致。",
                new { shipment.PlannedQuantity, shipment.ActualQuantity, Difference = shipment.ActualQuantity - shipment.PlannedQuantity });
        await using var validateCartons = new NpgsqlCommand("""
            WITH RECURSIVE descendants(root_id, id) AS (
                SELECT i.carton_id, p.id
                FROM shipment_items i JOIN packaging_units p ON p.id=i.carton_id
                WHERE i.shipment_id=$1 AND p.unit_type='Carton' AND p.status='Closed' AND p.order_id=$2
                UNION ALL
                SELECT d.root_id, b.child_id
                FROM descendants d JOIN packaging_bindings b ON b.parent_id=d.id AND b.is_active
            ), invalid AS (
                SELECT DISTINCT i.carton_id
                FROM shipment_items i
                LEFT JOIN descendants d ON d.root_id=i.carton_id
                LEFT JOIN packaging_units p ON p.id=d.id
                LEFT JOIN inspection_results r ON r.unit_id=p.production_unit_id
                LEFT JOIN inspection_lots l ON l.id=r.lot_id AND l.status='Failed'
                WHERE i.shipment_id=$1
                GROUP BY i.carton_id
                HAVING count(*) FILTER (WHERE p.unit_type='Body')=0 OR bool_or(l.id IS NOT NULL)
            )
            SELECT count(*) FROM invalid
            """, connection, transaction);
        Add(validateCartons, shipmentId, shipment.OrderId);
        if (Convert.ToInt64(await validateCartons.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) != 0)
            throw new PersistenceBusinessException("SHIPMENT_CARTON_INVALID", "出库确认时存在状态、订单、包装明细或质量状态已失效的卡通箱。");
        await using var update = new NpgsqlCommand(
            "UPDATE shipments SET status='Confirmed', version=version+1 WHERE id=$1 AND version=$2", connection, transaction);
        Add(update, shipmentId, shipment.Version);
        if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new PersistenceConcurrencyException("Shipment", shipmentId);
        await using var cartons = new NpgsqlCommand("""
            UPDATE packaging_units SET status='Shipped', version=version+1
            WHERE id IN (SELECT carton_id FROM shipment_items WHERE shipment_id=$1) AND status='Closed'
            """, connection, transaction);
        Add(cartons, shipmentId);
        var updatedCartons = await cartons.ExecuteNonQueryAsync(cancellationToken);
        await using var expectedCartons = new NpgsqlCommand(
            "SELECT count(*) FROM shipment_items WHERE shipment_id=$1", connection, transaction);
        Add(expectedCartons, shipmentId);
        var cartonCount = Convert.ToInt32(await expectedCartons.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        if (updatedCartons != cartonCount)
            throw new PersistenceConcurrencyException("ShipmentCartons", shipmentId);
        await using var command = new NpgsqlCommand("""
            INSERT INTO shipment_commands(idempotency_key, request_hash, shipment_id, result_status, created_at_utc)
            VALUES ($1,$2,$3,'Confirmed',$4)
            """, connection, transaction);
        Add(command, key.Value, hash, shipmentId, utcNow);
        await command.ExecuteNonQueryAsync(cancellationToken);
        var result = shipment with { Status = ShipmentStatus.Confirmed.ToString(), Version = shipment.Version + 1 };
        await TransactionalAudit.AppendAsync(connection, transaction, auditFactory?.Invoke(result), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private static async Task<ShipmentSnapshot?> ReadAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string id, bool forUpdate, CancellationToken cancellationToken)
    {
        id = Required(id, nameof(id));
        if (forUpdate)
        {
            await using var lockCommand = new NpgsqlCommand("SELECT id FROM shipments WHERE id=$1 FOR UPDATE", connection, transaction);
            Add(lockCommand, id);
            if (await lockCommand.ExecuteScalarAsync(cancellationToken) == null) return null;
        }
        const string sql = """
            SELECT s.id, s.order_id, s.customer, s.planned_quantity, s.delivery_reference, s.status, s.version,
                   COALESCE(sum(i.quantity),0), s.created_at_utc
            FROM shipments s LEFT JOIN shipment_items i ON i.shipment_id=s.id WHERE s.id=$1
            GROUP BY s.id
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        Add(command, id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new ShipmentSnapshot(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3),
            reader.GetString(4), reader.GetString(5), reader.GetInt64(6),
            Convert.ToInt32(reader.GetInt64(7), CultureInfo.InvariantCulture), ReadUtc(reader, 8));
    }

    private static async Task<(ShipmentItemSnapshot Item, string Hash)?> ReadItemAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, string key, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT shipment_id, carton_id, quantity, scanned_at_utc, operator_id, request_hash
            FROM shipment_items WHERE idempotency_key=$1
            """, connection, transaction);
        Add(command, key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return (new ShipmentItemSnapshot(reader.GetString(0), reader.GetString(1), reader.GetInt32(2),
            ReadUtc(reader, 3), reader.GetString(4), false), reader.GetString(5));
    }

    private static async Task<(string Hash, string ShipmentId)?> ReadCommandAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, string key, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT request_hash, shipment_id FROM shipment_commands WHERE idempotency_key=$1", connection, transaction);
        Add(command, key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? (reader.GetString(0), reader.GetString(1)) : null;
    }

    private static DateTimeOffset ReadUtc(NpgsqlDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));
    private static async Task AcquireIdempotencyLockAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string key, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended($1, 12))", connection, transaction);
        Add(command, key);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

public sealed class ExtendedTraceabilityRepository(NpgsqlDataSource dataSource, TraceabilityRepository coreRepository)
{
    public async Task<ExtendedTraceabilitySnapshot?> QueryAsync(TraceabilityQueryType queryType, string queryValue,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await QueryAsync(connection, queryType, queryValue, cancellationToken);
    }

    internal async Task<ExtendedTraceabilitySnapshot?> QueryAsync(NpgsqlConnection connection,
        TraceabilityQueryType queryType, string queryValue, CancellationToken cancellationToken)
    {
        var core = await coreRepository.QueryAsync(connection, queryType, queryValue, cancellationToken);
        if (core == null) return null;
        var lots = await ReadByValueAsync(connection, """
            SELECT id, order_id, inspection_type, sample_rule, sample_unit_ids_json::text, status, version, created_at_utc
            FROM inspection_lots WHERE order_id=$1 ORDER BY created_at_utc, id
            """, core.Order.Id, reader => new InspectionLotSnapshot(reader.GetString(0), reader.GetString(1),
            reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetInt64(6), ReadUtc(reader, 7)), cancellationToken);
        var lotIds = lots.Select(lot => lot.Id).ToArray();
        var results = lotIds.Length == 0 ? [] : await ReadByArrayAsync(connection, """
            SELECT id, lot_id, unit_id, item_code, outcome, defect_code, responsible_operation_id, remarks, inspected_at_utc
            FROM inspection_results WHERE lot_id=ANY($1) ORDER BY inspected_at_utc, id
            """, lotIds, reader => new InspectionResultSnapshot(reader.GetString(0), reader.GetString(1), reader.GetString(2),
            reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
            ReadUtc(reader, 8), false), cancellationToken);
        var dispositions = lotIds.Length == 0 ? [] : await ReadByArrayAsync(connection, """
            SELECT id, lot_id, decision, reason_code, approved_by, approved_at_utc
            FROM dispositions WHERE lot_id=ANY($1) ORDER BY approved_at_utc, id
            """, lotIds, reader => new DispositionSnapshot(reader.GetString(0), reader.GetString(1), reader.GetString(2),
            reader.GetString(3), reader.GetString(4), ReadUtc(reader, 5), false), cancellationToken);
        var reworks = await ReadByValueAsync(connection, """
            SELECT r.id, r.production_unit_id, r.route_id, r.reason_code, r.start_operation_id, r.status, r.sequence,
                   r.approved_by, r.approved_at_utc, r.closed_by, r.closed_at_utc, r.version
            FROM rework_orders r JOIN production_units u ON u.id=r.production_unit_id
            WHERE u.order_id=$1 ORDER BY r.id
            """, core.Order.Id, reader => new ReworkOrderSnapshot(reader.GetString(0), reader.GetString(1), reader.GetString(2),
            reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetInt32(6), reader.GetString(7),
            ReadNullableUtc(reader, 8), reader.GetString(9), ReadNullableUtc(reader, 10), reader.GetInt64(11)), cancellationToken);
        var shipments = await ReadByValueAsync(connection, """
            SELECT s.id, s.order_id, s.customer, s.planned_quantity, s.delivery_reference, s.status, s.version,
                   COALESCE(sum(i.quantity),0), s.created_at_utc
            FROM shipments s LEFT JOIN shipment_items i ON i.shipment_id=s.id WHERE s.order_id=$1
            GROUP BY s.id ORDER BY s.created_at_utc, s.id
            """, core.Order.Id, reader => new ShipmentSnapshot(reader.GetString(0), reader.GetString(1), reader.GetString(2),
            reader.GetInt32(3), reader.GetString(4), reader.GetString(5), reader.GetInt64(6),
            Convert.ToInt32(reader.GetInt64(7), CultureInfo.InvariantCulture), ReadUtc(reader, 8)), cancellationToken);
        var shipmentIds = shipments.Select(shipment => shipment.Id).ToArray();
        var items = shipmentIds.Length == 0 ? [] : await ReadByArrayAsync(connection, """
            SELECT shipment_id, carton_id, quantity, scanned_at_utc, operator_id
            FROM shipment_items WHERE shipment_id=ANY($1) ORDER BY scanned_at_utc, carton_id
            """, shipmentIds, reader => new ShipmentItemSnapshot(reader.GetString(0), reader.GetString(1), reader.GetInt32(2),
            ReadUtc(reader, 3), reader.GetString(4), false), cancellationToken);
        var archives = await ReadByValueAsync(connection, """
            SELECT id, order_id, payload_json::text, payload_hash, archived_at_utc, archived_by
            FROM order_archive_snapshots WHERE order_id=$1 ORDER BY archived_at_utc
            """, core.Order.Id, reader => new OrderArchiveSnapshotRecord(reader.GetString(0), reader.GetString(1),
            reader.GetString(2), reader.GetString(3), ReadUtc(reader, 4), reader.GetString(5), false), cancellationToken);
        var extendedEntityIds = lotIds.Concat(reworks.Select(value => value.Id))
            .Concat(shipmentIds).Distinct(StringComparer.Ordinal).ToArray();
        var extendedAudits = extendedEntityIds.Length == 0 ? [] : await ReadByArrayAsync(connection, """
            SELECT id, actor_id, station_id, shift_id, correlation_id, action, entity_type, entity_id,
                   before_json::text, after_json::text, occurred_at_utc
            FROM audit_events WHERE entity_id=ANY($1) ORDER BY occurred_at_utc, id
            """, extendedEntityIds, reader => new AuditEventSnapshot(reader.GetString(0), reader.GetString(1),
            reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6),
            reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetString(9), ReadUtc(reader, 10)), cancellationToken);
        return new ExtendedTraceabilitySnapshot(core.QueryType, core.QueryValue, core.Order, core.ProductionUnits,
            core.StationPasses, core.PackagingUnits, core.PackagingBindings, core.PrintIntents, core.PrintJobs,
            core.AuditEvents.Concat(extendedAudits).DistinctBy(value => value.Id)
                .OrderBy(value => value.OccurredAtUtc).ThenBy(value => value.Id).ToArray(),
            lots, results, dispositions, reworks, shipments, items, archives);
    }

    private static async Task<List<T>> ReadByValueAsync<T>(NpgsqlConnection connection, string sql, string value,
        Func<NpgsqlDataReader, T> map, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        Add(command, value);
        return await ReadAsync(command, map, cancellationToken);
    }

    private static async Task<List<T>> ReadByArrayAsync<T>(NpgsqlConnection connection, string sql, string[] values,
        Func<NpgsqlDataReader, T> map, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(values);
        return await ReadAsync(command, map, cancellationToken);
    }

    private static async Task<List<T>> ReadAsync<T>(NpgsqlCommand command, Func<NpgsqlDataReader, T> map,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var values = new List<T>();
        while (await reader.ReadAsync(cancellationToken)) values.Add(map(reader));
        return values;
    }

    private static DateTimeOffset ReadUtc(NpgsqlDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));
    private static DateTimeOffset? ReadNullableUtc(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : ReadUtc(reader, ordinal);
}

public sealed class OrderArchiveRepository(NpgsqlDataSource dataSource, ExtendedTraceabilityRepository traceabilityRepository)
{
    public async Task<OrderArchiveSnapshotRecord> ArchiveAsync(string orderId, string archivedBy,
        IdempotencyKey key, string requestHash, DateTimeOffset archivedAtUtc,
        CancellationToken cancellationToken = default,
        Func<OrderArchiveSnapshotRecord, AuditEventSnapshot>? auditFactory = null)
    {
        ValidateUtc(archivedAtUtc, nameof(archivedAtUtc));
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead,
            cancellationToken);
        await using (var idempotencyLock = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended($1, 12))", connection, transaction))
        {
            Add(idempotencyLock, key.Value);
            await idempotencyLock.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var orderLock = new NpgsqlCommand(
            "SELECT id FROM production_orders WHERE id=$1 FOR UPDATE", connection, transaction))
        {
            Add(orderLock, Required(orderId, nameof(orderId)));
            if (await orderLock.ExecuteScalarAsync(cancellationToken) == null)
                throw new KeyNotFoundException("生产订单不存在。");
        }
        await using var existingCommand = new NpgsqlCommand("""
            SELECT id, order_id, payload_json::text, payload_hash, archived_at_utc, archived_by, request_hash
            FROM order_archive_snapshots WHERE idempotency_key=$1 OR order_id=$2 LIMIT 1
            """, connection, transaction);
        Add(existingCommand, key.Value, Required(orderId, nameof(orderId)));
        await using (var reader = await existingCommand.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                if (reader.GetString(6) != requestHash)
                    throw new PersistenceBusinessException("IDEMPOTENCY_CONFLICT", "订单已使用其他归档请求创建快照。");
                var replay = CreateVerifiedRecord(reader, true);
                await reader.CloseAsync();
                await transaction.CommitAsync(cancellationToken);
                return replay;
            }
        }
        var trace = await traceabilityRepository.QueryAsync(connection, TraceabilityQueryType.Order, orderId, cancellationToken)
            ?? throw new KeyNotFoundException("生产订单不存在。");
        if (!string.Equals(trace.Order.Status, "Closed", StringComparison.Ordinal))
            throw new PersistenceBusinessException("ORDER_NOT_ARCHIVABLE", "生产订单关闭后才能归档。");
        var payload = await CanonicalizeAsync(connection, transaction, JsonSerializer.Serialize(trace), cancellationToken);
        var snapshot = new OrderArchiveSnapshot(EntityId.New(), new EntityId(orderId), payload, archivedAtUtc, archivedBy);
        await using var command = new NpgsqlCommand("""
            INSERT INTO order_archive_snapshots
                (id, order_id, payload_json, payload_hash, archived_at_utc, archived_by, idempotency_key, request_hash)
            VALUES ($1,$2,$3,$4,$5,$6,$7,$8)
            """, connection, transaction);
        Add(command, snapshot.Id.Value, snapshot.OrderId.Value);
        command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, snapshot.PayloadJson);
        Add(command, snapshot.PayloadHash, snapshot.ArchivedAtUtc, snapshot.ArchivedBy, key.Value, requestHash);
        await command.ExecuteNonQueryAsync(cancellationToken);
        var result = new OrderArchiveSnapshotRecord(snapshot.Id.Value, orderId, snapshot.PayloadJson,
            snapshot.PayloadHash, archivedAtUtc, snapshot.ArchivedBy, false);
        await TransactionalAudit.AppendAsync(connection, transaction, auditFactory?.Invoke(result), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<OrderArchiveSnapshotRecord?> GetByOrderIdAsync(string orderId,
        CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT id, order_id, payload_json::text, payload_hash, archived_at_utc, archived_by
            FROM order_archive_snapshots WHERE order_id=$1
            """);
        Add(command, Required(orderId, nameof(orderId)));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? CreateVerifiedRecord(reader, false) : null;
    }

    private static OrderArchiveSnapshotRecord CreateVerifiedRecord(NpgsqlDataReader reader, bool isReplay)
    {
        var payload = reader.GetString(2);
        var storedHash = reader.GetString(3);
        var actualHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        if (!string.Equals(storedHash, actualHash, StringComparison.Ordinal))
            throw new PersistenceBusinessException("ARCHIVE_HASH_MISMATCH", "订单归档快照完整性校验失败。");
        return new OrderArchiveSnapshotRecord(reader.GetString(0), reader.GetString(1), payload, storedHash,
            ReadUtc(reader, 4), reader.GetString(5), isReplay);
    }

    private static DateTimeOffset ReadUtc(NpgsqlDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    private static async Task<string> CanonicalizeAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string json, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT $1::jsonb::text", connection, transaction);
        command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, json);
        return (string)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("归档 JSON 规范化失败。"));
    }
}
