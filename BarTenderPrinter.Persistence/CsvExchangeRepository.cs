using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BarTenderPrinter.Domain.Common;
using BarTenderPrinter.Domain.Numbering;
using BarTenderPrinter.Domain.Orders;
using Npgsql;
using NpgsqlTypes;
using static BarTenderPrinter.Persistence.PostgresRepositoryHelpers;

namespace BarTenderPrinter.Persistence;

public sealed class CsvExchangeRepository(NpgsqlDataSource dataSource)
{
    private static readonly string[] OrderHeaders =
        ["orderNumber", "customer", "productModel", "color", "plannedQuantity", "validFromUtc", "validToUtc"];
    private static readonly string[] RangeHeaders =
        ["orderId", "numberType", "prefix", "datePattern", "start", "end", "step", "numericWidth", "validationPattern"];

    public async Task<CsvImportBatchSnapshot> StageAsync(string importType, byte[] source, string createdBy,
        IdempotencyKey key, string requestHash, DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken = default,
        Func<CsvImportBatchSnapshot, AuditEventSnapshot>? auditFactory = null)
    {
        ValidateUtc(createdAtUtc, nameof(createdAtUtc));
        importType = NormalizeType(importType);
        var sourceHash = Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant();
        var parsed = Parse(source, importType);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ValidateDatabaseAsync(connection, transaction, parsed, importType, cancellationToken);
        await using (var idempotencyLock = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended($1, 12))", connection, transaction))
        {
            Add(idempotencyLock, key.Value);
            await idempotencyLock.ExecuteNonQueryAsync(cancellationToken);
        }
        var existing = await ReadBatchAsync(connection, transaction, key.Value, cancellationToken);
        if (existing != null)
        {
            if (existing.Value.Hash != requestHash)
                throw new PersistenceBusinessException("IDEMPOTENCY_CONFLICT", "幂等键已用于其他 CSV 导入请求。");
            await transaction.CommitAsync(cancellationToken);
            return existing.Value.Batch with { IsReplay = true };
        }
        await using var duplicate = new NpgsqlCommand("""
            SELECT id FROM csv_import_batches WHERE import_type=$1 AND source_sha256=$2
            """, connection, transaction);
        Add(duplicate, importType, sourceHash);
        if (await duplicate.ExecuteScalarAsync(cancellationToken) != null)
            throw new PersistenceBusinessException("CSV_SOURCE_ALREADY_STAGED", "相同来源文件已经创建导入批次。");
        var id = EntityId.New().Value;
        var errors = parsed.SelectMany(row => row.Errors).ToArray();
        var status = errors.Length == 0 ? "Ready" : "Invalid";
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO csv_import_batches
                (id, import_type, source_sha256, status, total_rows, valid_rows, errors_json, created_at_utc,
                 created_by, idempotency_key, request_hash)
            VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11)
            """, connection, transaction))
        {
            Add(insert, id, importType, sourceHash, status, parsed.Count, parsed.Count(row => row.Errors.Count == 0));
            insert.Parameters.AddWithValue(NpgsqlDbType.Jsonb, JsonSerializer.Serialize(errors));
            Add(insert, createdAtUtc, Required(createdBy, nameof(createdBy)), key.Value, requestHash);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (var row in parsed)
        {
            await using var insertRow = new NpgsqlCommand("""
                INSERT INTO csv_import_rows(batch_id, row_number, values_json, is_valid, errors_json)
                VALUES ($1,$2,$3,$4,$5)
                """, connection, transaction);
            Add(insertRow, id, row.RowNumber);
            insertRow.Parameters.AddWithValue(NpgsqlDbType.Jsonb, JsonSerializer.Serialize(row.Values));
            Add(insertRow, row.Errors.Count == 0);
            insertRow.Parameters.AddWithValue(NpgsqlDbType.Jsonb, JsonSerializer.Serialize(row.Errors));
            await insertRow.ExecuteNonQueryAsync(cancellationToken);
        }
        var result = new CsvImportBatchSnapshot(id, importType, sourceHash, status, parsed.Count,
            parsed.Count(row => row.Errors.Count == 0), errors, createdAtUtc, createdBy.Trim());
        await TransactionalAudit.AppendAsync(connection, transaction, auditFactory?.Invoke(result), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<CsvImportBatchSnapshot?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        return (await ReadBatchByIdAsync(connection, transaction, Required(id, nameof(id)), false, cancellationToken))?.Batch;
    }

    public async Task<CsvImportBatchSnapshot> ConfirmAsync(string id, IdempotencyKey key, string requestHash,
        DateTimeOffset confirmedAtUtc, CancellationToken cancellationToken = default,
        Func<CsvImportBatchSnapshot, AuditEventSnapshot>? auditFactory = null)
    {
        ValidateUtc(confirmedAtUtc, nameof(confirmedAtUtc));
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var lockCommand = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended($1, 12))", connection, transaction);
        Add(lockCommand, key.Value);
        await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        await using var replay = new NpgsqlCommand("""
            SELECT request_hash, batch_id FROM csv_import_confirmations WHERE idempotency_key=$1
            """, connection, transaction);
        Add(replay, key.Value);
        await using (var replayReader = await replay.ExecuteReaderAsync(cancellationToken))
        {
            if (await replayReader.ReadAsync(cancellationToken))
            {
                if (replayReader.GetString(0) != requestHash || replayReader.GetString(1) != id)
                    throw new PersistenceBusinessException("IDEMPOTENCY_CONFLICT", "幂等键已用于其他 CSV 确认请求。");
                await replayReader.CloseAsync();
                var replayBatch = await ReadBatchByIdAsync(connection, transaction, id, false, cancellationToken)
                    ?? throw new KeyNotFoundException("CSV 导入批次不存在。");
                await transaction.CommitAsync(cancellationToken);
                return replayBatch.Batch with { IsReplay = true };
            }
        }
        var batch = await ReadBatchByIdAsync(connection, transaction, id, true, cancellationToken)
            ?? throw new KeyNotFoundException("CSV 导入批次不存在。");
        if (batch.Batch.Status == "Invalid")
            throw new PersistenceBusinessException("CSV_BATCH_INVALID", "存在逐行验证错误，当前批次无法确认。");
        if (batch.Batch.Status != "Ready")
            throw new PersistenceBusinessException("CSV_BATCH_STATE_CONFLICT", "CSV 导入批次当前不可确认。");
        var rows = await ReadRowsAsync(connection, transaction, id, cancellationToken);
        foreach (var values in rows)
        {
            if (batch.Batch.ImportType == "orders") await InsertOrderAsync(connection, transaction, values, cancellationToken);
            else await InsertRangeAsync(connection, transaction, values, cancellationToken);
        }
        await using (var confirm = new NpgsqlCommand("""
            INSERT INTO csv_import_confirmations(idempotency_key, request_hash, batch_id, confirmed_at_utc)
            VALUES ($1,$2,$3,$4)
            """, connection, transaction))
        {
            Add(confirm, key.Value, requestHash, id, confirmedAtUtc);
            await confirm.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var update = new NpgsqlCommand(
            "UPDATE csv_import_batches SET status='Committed', confirmed_at_utc=$1 WHERE id=$2", connection, transaction))
        {
            Add(update, confirmedAtUtc, id);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }
        var result = batch.Batch with { Status = "Committed", ConfirmedAtUtc = confirmedAtUtc };
        await TransactionalAudit.AppendAsync(connection, transaction, auditFactory?.Invoke(result), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<string> ExportOrdersAsync(bool revealSensitive, CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT id, order_number, customer, product_model, color, planned_quantity, status
            FROM production_orders ORDER BY order_number, id
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var csv = new StringBuilder("id,orderNumber,customer,productModel,color,plannedQuantity,status\r\n");
        while (await reader.ReadAsync(cancellationToken))
            Append(csv, reader.GetString(0), reader.GetString(1), Mask(reader.GetString(2), revealSensitive),
                reader.GetString(3), reader.GetString(4), reader.GetInt32(5).ToString(CultureInfo.InvariantCulture),
                reader.GetString(6));
        return csv.ToString();
    }

    public async Task<string> ExportRangesAsync(CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT id, order_id, number_type, prefix, date_pattern, start_value, end_value, next_value, step,
                   numeric_width, validation_pattern, is_exhausted FROM number_ranges ORDER BY order_id, id
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var csv = new StringBuilder("id,orderId,numberType,prefix,datePattern,start,end,next,step,numericWidth,validationPattern,isExhausted\r\n");
        while (await reader.ReadAsync(cancellationToken))
            Append(csv, Enumerable.Range(0, 12).Select(index => Convert.ToString(reader.GetValue(index),
                CultureInfo.InvariantCulture) ?? "").ToArray());
        return csv.ToString();
    }

    public async Task<string> ExportTraceabilityAsync(bool revealSensitive,
        CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT u.id, u.order_id, o.order_number, o.customer, u.status, u.current_operation_id,
                   u.identifiers_json::text FROM production_units u JOIN production_orders o ON o.id=u.order_id
            ORDER BY o.order_number, u.id
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var csv = new StringBuilder("unitId,orderId,orderNumber,customer,status,currentOperationId,identifiers\r\n");
        while (await reader.ReadAsync(cancellationToken))
            Append(csv, reader.GetString(0), reader.GetString(1), reader.GetString(2),
                Mask(reader.GetString(3), revealSensitive), reader.GetString(4), reader.GetString(5),
                Mask(reader.GetString(6), revealSensitive));
        return csv.ToString();
    }

    private static List<ParsedRow> Parse(byte[] source, string importType)
    {
        string text;
        try { text = new UTF8Encoding(false, true).GetString(source); }
        catch (DecoderFallbackException)
        { throw new PersistenceBusinessException("CSV_ENCODING_INVALID", "CSV 文件必须使用 UTF-8 编码。"); }
        var records = ParseRecords(text);
        if (records.Count == 0) throw new PersistenceBusinessException("CSV_EMPTY", "CSV 文件为空。");
        var expected = importType == "orders" ? OrderHeaders : RangeHeaders;
        if (!records[0].SequenceEqual(expected, StringComparer.OrdinalIgnoreCase))
            throw new PersistenceBusinessException("CSV_HEADER_INVALID", $"CSV 表头必须为：{string.Join(',', expected)}。");
        if (records.Count == 1) throw new PersistenceBusinessException("CSV_ROWS_REQUIRED", "CSV 文件至少需要一行数据。");
        var result = new List<ParsedRow>();
        for (var index = 1; index < records.Count; index++)
        {
            var record = records[index];
            var errors = new List<CsvImportErrorSnapshot>();
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (record.Count != expected.Length)
                errors.Add(new CsvImportErrorSnapshot(index + 1, "COLUMN_COUNT", "列数与表头不一致。"));
            for (var column = 0; column < expected.Length; column++)
                values[expected[column]] = column < record.Count ? record[column].Trim() : "";
            try { Validate(values, importType); }
            catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException)
            { errors.Add(new CsvImportErrorSnapshot(index + 1, "VALIDATION_FAILED", exception.Message)); }
            result.Add(new ParsedRow(index + 1, values, errors));
        }
        return result;
    }

    private static async Task ValidateDatabaseAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        IReadOnlyList<ParsedRow> rows, string importType, CancellationToken cancellationToken)
    {
        var keyName = importType == "orders" ? "orderNumber" : "orderId/numberType";
        foreach (var group in rows.GroupBy(row => importType == "orders" ? row.Values["orderNumber"]
                         : $"{row.Values["orderId"]}\u001f{row.Values["numberType"]}", StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Key.Length > 0 && group.Count() > 1))
            foreach (var row in group)
                row.Errors.Add(new CsvImportErrorSnapshot(row.RowNumber, "CSV_DUPLICATE", $"{keyName} 在文件中重复。"));
        if (importType == "orders")
        {
            var orderNumbers = rows.Select(row => row.Values["orderNumber"]).Where(value => value.Length > 0).Distinct().ToArray();
            await using var command = new NpgsqlCommand(
                "SELECT order_number FROM production_orders WHERE order_number=ANY($1)", connection, transaction);
            command.Parameters.AddWithValue(orderNumbers);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (await reader.ReadAsync(cancellationToken)) existing.Add(reader.GetString(0));
            foreach (var row in rows.Where(row => existing.Contains(row.Values["orderNumber"])))
                row.Errors.Add(new CsvImportErrorSnapshot(row.RowNumber, "ORDER_EXISTS", "订单编号已经存在。"));
            return;
        }
        var orderIds = rows.Select(row => row.Values["orderId"]).Where(value => value.Length > 0).Distinct().ToArray();
        await using var orders = new NpgsqlCommand("SELECT id FROM production_orders WHERE id=ANY($1)", connection, transaction);
        orders.Parameters.AddWithValue(orderIds);
        await using var orderReader = await orders.ExecuteReaderAsync(cancellationToken);
        var found = new HashSet<string>(StringComparer.Ordinal);
        while (await orderReader.ReadAsync(cancellationToken)) found.Add(orderReader.GetString(0));
        foreach (var row in rows.Where(row => !found.Contains(row.Values["orderId"])))
            row.Errors.Add(new CsvImportErrorSnapshot(row.RowNumber, "ORDER_NOT_FOUND", "关联订单不存在。"));
        await using var ranges = new NpgsqlCommand("""
            SELECT order_id, number_type FROM number_ranges WHERE order_id=ANY($1)
            """, connection, transaction);
        ranges.Parameters.AddWithValue(orderIds);
        await using var rangeReader = await ranges.ExecuteReaderAsync(cancellationToken);
        var existingRanges = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await rangeReader.ReadAsync(cancellationToken))
            existingRanges.Add($"{rangeReader.GetString(0)}\u001f{rangeReader.GetString(1)}");
        foreach (var row in rows.Where(row => existingRanges.Contains(
                     $"{row.Values["orderId"]}\u001f{row.Values["numberType"]}")))
            row.Errors.Add(new CsvImportErrorSnapshot(row.RowNumber, "NUMBER_RANGE_EXISTS", "订单同类型号段已经存在。"));
    }

    private static void Validate(IReadOnlyDictionary<string, string> values, string importType)
    {
        if (importType == "orders")
        {
            _ = new ProductionOrder(EntityId.New(), values["orderNumber"], values["customer"], values["productModel"],
                values["color"], int.Parse(values["plannedQuantity"], CultureInfo.InvariantCulture),
                ParseNullableUtc(values["validFromUtc"]), ParseNullableUtc(values["validToUtc"]));
            return;
        }
        _ = new NumberRange(EntityId.New(), new EntityId(values["orderId"]),
            Enum.Parse<NumberType>(values["numberType"], true), values["prefix"],
            Enum.Parse<NumberDatePattern>(values["datePattern"], true),
            long.Parse(values["start"], CultureInfo.InvariantCulture), long.Parse(values["end"], CultureInfo.InvariantCulture),
            long.Parse(values["step"], CultureInfo.InvariantCulture), int.Parse(values["numericWidth"], CultureInfo.InvariantCulture),
            values["validationPattern"]);
        if (values["validationPattern"].Length > 0)
            _ = new Regex(values["validationPattern"], RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(250));
    }

    private static List<List<string>> ParseRecords(string text)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < text.Length; index++)
        {
            var value = text[index];
            if (value == '"')
            {
                if (quoted && index + 1 < text.Length && text[index + 1] == '"') { field.Append('"'); index++; }
                else quoted = !quoted;
            }
            else if (value == ',' && !quoted) { record.Add(field.ToString()); field.Clear(); }
            else if ((value == '\r' || value == '\n') && !quoted)
            {
                if (value == '\r' && index + 1 < text.Length && text[index + 1] == '\n') index++;
                record.Add(field.ToString()); field.Clear();
                if (record.Any(item => item.Length > 0)) records.Add(record);
                record = [];
            }
            else field.Append(value);
        }
        if (quoted) throw new PersistenceBusinessException("CSV_QUOTE_INVALID", "CSV 引号未闭合。");
        record.Add(field.ToString());
        if (record.Any(item => item.Length > 0)) records.Add(record);
        if (records.Count > 0 && records[0].Count > 0) records[0][0] = records[0][0].TrimStart('\uFEFF');
        return records;
    }

    private static async Task InsertOrderAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken)
    {
        var order = new ProductionOrder(EntityId.New(), values["orderNumber"], values["customer"], values["productModel"],
            values["color"], int.Parse(values["plannedQuantity"], CultureInfo.InvariantCulture),
            ParseNullableUtc(values["validFromUtc"]), ParseNullableUtc(values["validToUtc"]));
        await using var command = new NpgsqlCommand("""
            INSERT INTO production_orders(id, order_number, customer, product_model, color, planned_quantity,
                valid_from_utc, valid_to_utc, status, version) VALUES ($1,$2,$3,$4,$5,$6,$7,$8,'Draft',0)
            """, connection, transaction);
        Add(command, order.Id.Value, order.OrderNumber, order.Customer, order.ProductModel, order.Color,
            order.PlannedQuantity, DbValue(order.ValidFromUtc), DbValue(order.ValidToUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertRangeAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken)
    {
        var range = new NumberRange(EntityId.New(), new EntityId(values["orderId"]),
            Enum.Parse<NumberType>(values["numberType"], true), values["prefix"],
            Enum.Parse<NumberDatePattern>(values["datePattern"], true),
            long.Parse(values["start"], CultureInfo.InvariantCulture), long.Parse(values["end"], CultureInfo.InvariantCulture),
            long.Parse(values["step"], CultureInfo.InvariantCulture), int.Parse(values["numericWidth"], CultureInfo.InvariantCulture),
            values["validationPattern"]);
        await using var command = new NpgsqlCommand("""
            INSERT INTO number_ranges(id, order_id, number_type, prefix, date_pattern, start_value, end_value,
                next_value, step, numeric_width, validation_pattern, is_exhausted, version)
            VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,false,0)
            """, connection, transaction);
        Add(command, range.Id.Value, range.OrderId.Value, range.Type.ToString(), range.Prefix, range.DatePattern.ToString(),
            range.Start, range.End, range.NextValue, range.Step, range.NumericWidth, range.ValidationPattern);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<(CsvImportBatchSnapshot Batch, string Hash)?> ReadBatchAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, string key, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(BatchSelect + " WHERE idempotency_key=$1", connection, transaction);
        Add(command, key);
        return await ReadBatchResultAsync(command, cancellationToken);
    }

    private static async Task<(CsvImportBatchSnapshot Batch, string Hash)?> ReadBatchByIdAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, string id, bool forUpdate, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(BatchSelect + " WHERE id=$1" + (forUpdate ? " FOR UPDATE" : ""),
            connection, transaction);
        Add(command, id);
        return await ReadBatchResultAsync(command, cancellationToken);
    }

    private static async Task<(CsvImportBatchSnapshot Batch, string Hash)?> ReadBatchResultAsync(NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var errors = JsonSerializer.Deserialize<CsvImportErrorSnapshot[]>(reader.GetString(6)) ?? [];
        return (new CsvImportBatchSnapshot(reader.GetString(0), reader.GetString(1), reader.GetString(2),
            reader.GetString(3), reader.GetInt32(4), reader.GetInt32(5), errors, ReadUtc(reader, 7), reader.GetString(8),
            reader.IsDBNull(9) ? null : ReadUtc(reader, 9)), reader.GetString(10));
    }

    private static async Task<List<Dictionary<string, string>>> ReadRowsAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, string id, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT values_json::text FROM csv_import_rows WHERE batch_id=$1 AND is_valid ORDER BY row_number",
            connection, transaction);
        Add(command, id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<Dictionary<string, string>>();
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(0))!);
        return rows;
    }

    private const string BatchSelect = """
        SELECT id, import_type, source_sha256, status, total_rows, valid_rows, errors_json::text, created_at_utc,
               created_by, confirmed_at_utc, request_hash FROM csv_import_batches
        """;
    private static string NormalizeType(string value) => value.Trim().ToLowerInvariant() switch
    { "orders" => "orders", "number-ranges" => "number-ranges", _ => throw new ArgumentException("导入类型无效。") };
    private static DateTimeOffset? ParseNullableUtc(string value) => string.IsNullOrWhiteSpace(value) ? null
        : DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    private static DateTimeOffset ReadUtc(NpgsqlDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));
    private static string Mask(string value, bool reveal) => reveal ? value : "***";
    private static void Append(StringBuilder target, params string[] values) => target.AppendJoin(',', values.Select(Escape)).Append("\r\n");
    private static string Escape(string value)
    {
        value ??= "";
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0])) value = "'" + value;
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
    private sealed record ParsedRow(int RowNumber, Dictionary<string, string> Values,
        List<CsvImportErrorSnapshot> Errors);
}
