using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace BarTenderPrinter
{
    internal sealed class SyncSnapshotPayload
    {
        public string Kind { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public string ContentBase64 { get; set; } = "";
        public Dictionary<string, string> TemplatePaths { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    internal sealed class SyncSpaceDescriptor
    {
        public int SchemaVersion { get; set; }
        public string SpaceId { get; set; } = "";
        public string WorkspaceName { get; set; } = "";
    }

    internal sealed class SyncEventObjectCodec : ISyncObjectCodec
    {
        public SyncEvent DecodeEvent(SyncConnectionProfile profile, string objectPath, byte[] encryptedBlob)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            var parts = (objectPath ?? "").Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            var fileName = parts.Length == 0 ? "" : Path.GetFileNameWithoutExtension(parts[^1]);
            if (parts.Length < 2 || !parts[^1].EndsWith(".evt", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(parts[^2]) || !long.TryParse(fileName, NumberStyles.None, CultureInfo.InvariantCulture, out var sequence) || sequence <= 0)
                throw new InvalidDataException("同步事件对象路径无效。");
            var deviceId = parts[^2];
            var eventId = $"{deviceId}:{sequence}";
            var plaintext = SyncCrypto.DecryptObject(encryptedBlob, profile.DataKey, profile.SpaceId, "event", eventId);
            try
            {
                var syncEvent = JsonSerializer.Deserialize<SyncEvent>(plaintext) ?? throw new InvalidDataException("同步事件内容为空。");
                if (!string.Equals(syncEvent.DeviceId, deviceId, StringComparison.Ordinal) || syncEvent.Sequence != sequence ||
                    !string.Equals(syncEvent.EventId, eventId, StringComparison.Ordinal))
                    throw new InvalidDataException("同步事件路径身份与内容身份不一致。");
                return syncEvent;
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("同步事件 JSON 无效。", ex);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
    }

    internal sealed class FileSnapshotSyncEventApplier : ISyncEventApplier
    {
        private readonly string _ordersPath;
        private readonly string _settingsPath;
        private readonly string _templateCacheDirectory;
        private readonly string _historyDatabasePath;
        private readonly Action<string> _sharedDataChanged;

        public FileSnapshotSyncEventApplier(string ordersPath, string settingsPath, string incomingDirectory, string templateCacheDirectory,
            string historyDatabasePath = null, Action<string> sharedDataChanged = null)
        {
            _ordersPath = Path.GetFullPath(ordersPath);
            _settingsPath = Path.GetFullPath(settingsPath);
            _ = Path.GetFullPath(incomingDirectory);
            _templateCacheDirectory = Path.GetFullPath(templateCacheDirectory);
            _historyDatabasePath = Path.GetFullPath(historyDatabasePath ?? AppPaths.RecordsSqliteFile);
            _sharedDataChanged = sharedDataChanged;
        }

        public Task<IReadOnlyDictionary<string, long>> GetCursorsAsync(SyncStore store, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(store.GetCursors());
        }

        public Task<SyncApplyOutcome> ApplyAtomicallyAsync(SyncEvent syncEvent, SyncStore store, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (store.IsEventApplied(syncEvent.EventId))
                return Task.FromResult(new SyncApplyOutcome { Result = SyncEventApplyResult.AlreadyApplied });

            var payload = DeserializePayload(syncEvent.PayloadJson);
            var content = DecodeContent(payload);
            var actualHash = SyncCrypto.ComputeSha256Hex(content);
            if (!string.Equals(actualHash, payload.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("同步快照摘要不匹配。");

            var localState = store.GetEntityState(syncEvent.EntityType, syncEvent.EntityId);
            if (IsAppendOnly(syncEvent.EntityType))
                return Task.FromResult(ApplyAppendOnly(syncEvent, store, payload, content));
            var appliesResolution = !string.IsNullOrWhiteSpace(syncEvent.ResolvesConflictId) && localState.Version <= syncEvent.BaseVersion;
            if (!appliesResolution && localState.Version != syncEvent.BaseVersion &&
                !string.Equals(localState.ContentHash, payload.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                var conflict = new SyncConflict
                {
                    ConflictId = syncEvent.EventId,
                    EntityType = syncEvent.EntityType,
                    EntityId = syncEvent.EntityId,
                    LocalJson = JsonSerializer.Serialize(localState),
                    RemoteJson = JsonSerializer.Serialize(syncEvent),
                    CreatedAtUtc = DateTimeOffset.UtcNow
                };
                store.AddConflict(conflict);
                store.RecordAppliedEvent(syncEvent.EventId, syncEvent.DeviceId, syncEvent.Sequence);
                return Task.FromResult(new SyncApplyOutcome { Result = SyncEventApplyResult.Conflict, Conflict = conflict });
            }

            ApplyPayload(syncEvent.EntityType, syncEvent.EntityId, payload, content);
            store.UpsertEntityState(syncEvent.EntityType, syncEvent.EntityId, syncEvent.NewVersion, payload.Sha256);
            store.RecordAppliedEvent(syncEvent.EventId, syncEvent.DeviceId, syncEvent.Sequence);
            _sharedDataChanged?.Invoke(syncEvent.EntityType);
            return Task.FromResult(new SyncApplyOutcome { Result = SyncEventApplyResult.Applied });
        }

        internal void ApplyResolvedRemote(SyncConflict conflict, SyncEvent remoteEvent, SyncStore store, long version)
        {
            if (IsAppendOnly(conflict.EntityType))
                throw new InvalidOperationException("仅追加打印实体发生内容冲突时不能覆盖已有记录。");
            var payload = DeserializePayload(remoteEvent.PayloadJson);
            var content = DecodeContent(payload);
            ApplyPayload(conflict.EntityType, conflict.EntityId, payload, content);
            store.UpsertEntityState(conflict.EntityType, conflict.EntityId, version, payload.Sha256);
            _sharedDataChanged?.Invoke(conflict.EntityType);
        }

        internal void ApplySnapshotEntry(EncryptedSnapshotEntry entry, SyncStore store)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.EntityType) || string.IsNullOrWhiteSpace(entry.EntityId) || entry.Version < 1)
                throw new InvalidDataException("快照实体身份无效。");
            var payload = DeserializePayload(entry.PayloadJson);
            var content = DecodeContent(payload);
            if (!string.Equals(SyncCrypto.ComputeSha256Hex(content), payload.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("快照实体摘要无效。");
            if (IsAppendOnly(entry.EntityType))
            {
                var syncEvent = new SyncEvent
                {
                    EventId = $"snapshot:{Guid.NewGuid():N}", DeviceId = "snapshot", Sequence = 1,
                    EntityType = entry.EntityType, EntityId = entry.EntityId, BaseVersion = 0, NewVersion = entry.Version,
                    PayloadJson = entry.PayloadJson
                };
                if (string.Equals(entry.EntityType, "PrintRecord", StringComparison.Ordinal)) MergePrintRecord(syncEvent, payload, content);
                else if (string.Equals(entry.EntityType, "PrintJobEvent", StringComparison.Ordinal)) MergePrintJobEvent(syncEvent, payload, content);
                else throw new InvalidDataException("快照仅追加实体类型无效。");
            }
            else ApplyPayload(entry.EntityType, entry.EntityId, payload, content);
            store.UpsertEntityState(entry.EntityType, entry.EntityId, entry.Version, payload.Sha256);
            _sharedDataChanged?.Invoke(entry.EntityType);
        }

        private void ApplyPayload(string entityType, string entityId, SyncSnapshotPayload payload, byte[] content)
        {
            if (string.Equals(entityType, "Orders", StringComparison.Ordinal))
            {
                var rewritten = RewriteTemplatePaths(content, payload.TemplatePaths);
                ValidateJsonArray(rewritten, "订单");
                WriteAtomicBytes(_ordersPath, rewritten);
                return;
            }
            if (string.Equals(entityType, "TemplateSettings", StringComparison.Ordinal))
            {
                var rewritten = RewriteTemplatePaths(content, payload.TemplatePaths);
                ValidateJsonArray(rewritten, "模板设置");
                WriteAtomicBytes(_settingsPath, rewritten);
                return;
            }

            throw new InvalidDataException($"不支持的同步实体类型: {entityType}");
        }

        private SyncApplyOutcome ApplyAppendOnly(SyncEvent syncEvent, SyncStore store, SyncSnapshotPayload payload, byte[] content)
        {
            var expectedKind = string.Equals(syncEvent.EntityType, "PrintRecord", StringComparison.Ordinal)
                ? SyncSnapshotKind.PrintRecord.ToString()
                : SyncSnapshotKind.PrintJobEvent.ToString();
            if (!string.Equals(payload.Kind, expectedKind, StringComparison.Ordinal))
                throw new InvalidDataException("打印同步事件类型与载荷类型不一致。");
            var result = string.Equals(syncEvent.EntityType, "PrintRecord", StringComparison.Ordinal)
                ? MergePrintRecord(syncEvent, payload, content)
                : MergePrintJobEvent(syncEvent, payload, content);
            if (result == SyncEventApplyResult.Conflict)
            {
                var conflict = new SyncConflict
                {
                    ConflictId = syncEvent.EventId,
                    EntityType = syncEvent.EntityType,
                    EntityId = syncEvent.EntityId,
                    LocalJson = ReadAppendOnlyLocalJson(syncEvent.EntityType, syncEvent.EntityId),
                    RemoteJson = JsonSerializer.Serialize(syncEvent),
                    CreatedAtUtc = DateTimeOffset.UtcNow
                };
                store.AddConflict(conflict);
                store.RecordAppliedEvent(syncEvent.EventId, syncEvent.DeviceId, syncEvent.Sequence);
                return new SyncApplyOutcome { Result = SyncEventApplyResult.Conflict, Conflict = conflict };
            }
            store.UpsertEntityState(syncEvent.EntityType, syncEvent.EntityId, 1, payload.Sha256);
            store.RecordAppliedEvent(syncEvent.EventId, syncEvent.DeviceId, syncEvent.Sequence);
            return new SyncApplyOutcome { Result = result };
        }

        private SyncEventApplyResult MergePrintRecord(SyncEvent syncEvent, SyncSnapshotPayload payload, byte[] content)
        {
            PrintRecord record;
            try { record = JsonSerializer.Deserialize<PrintRecord>(content) ?? throw new InvalidDataException("打印历史事件内容为空。"); }
            catch (JsonException ex) { throw new InvalidDataException("打印历史事件 JSON 无效。", ex); }
            if (string.IsNullOrWhiteSpace(record.RecordId) || !string.Equals(record.RecordId, syncEvent.EntityId, StringComparison.Ordinal))
                throw new InvalidDataException("打印历史事件身份不一致。");

            EnsureHistoryDirectory();
            using var connection = OpenHistoryConnection();
            using var transaction = connection.BeginTransaction(deferred: false);
            EnsureSharedTables(connection, transaction);
            var existing = ReadScalar(connection, transaction, "SELECT Json FROM PrintRecords WHERE RecordId=$id LIMIT 1", ("$id", record.RecordId));
            if (existing != null)
            {
                var existingRecord = JsonSerializer.Deserialize<PrintRecord>(existing) ?? throw new InvalidDataException("本地打印历史记录为空。");
                existingRecord.RecordChecksum = "";
                var existingHash = SyncCrypto.ComputeSha256Hex(JsonSerializer.SerializeToUtf8Bytes(existingRecord));
                transaction.Commit();
                return string.Equals(existingHash, payload.Sha256, StringComparison.OrdinalIgnoreCase)
                    ? SyncEventApplyResult.AlreadyApplied : SyncEventApplyResult.Conflict;
            }

            HistoryManager.StampChecksum(record);
            var localJson = JsonSerializer.Serialize(record);
            Execute(connection, transaction, "INSERT INTO PrintRecords (RecordId, OrderId, TemplateId, TemplateName, TemplatePath, PrintTime, Status, Printer, Copies, OperatorName, ReprintReason, TemplateVersion, DiagnosticDetails, OrderName, RecordChecksum, Json) VALUES ($recordId,$orderId,$templateId,$templateName,$templatePath,$printTime,$status,$printer,$copies,$operator,$reason,$templateVersion,$diagnostic,$orderName,$checksum,$json)",
                ("$recordId", record.RecordId), ("$orderId", record.OrderId), ("$templateId", record.TemplateId), ("$templateName", record.TemplateName),
                ("$templatePath", record.TemplatePath), ("$printTime", record.PrintTime), ("$status", record.Status), ("$printer", record.Printer),
                ("$copies", record.Copies), ("$operator", record.OperatorName), ("$reason", record.ReprintReason), ("$templateVersion", record.TemplateVersion),
                ("$diagnostic", record.DiagnosticDetails), ("$orderName", record.OrderName), ("$checksum", record.RecordChecksum), ("$json", localJson));
            foreach (var field in record.FieldValues ?? new Dictionary<string, string>())
                Execute(connection, transaction, "INSERT INTO FieldValues (RecordId, FieldName, FieldValue, TemplateId, OrderId) VALUES ($recordId,$name,$value,$templateId,$orderId)",
                    ("$recordId", record.RecordId), ("$name", field.Key), ("$value", field.Value), ("$templateId", record.TemplateId), ("$orderId", record.OrderId));
            foreach (var field in record.TemplateFields ?? new List<string>())
                Execute(connection, transaction, "INSERT INTO TemplateSnapshots (TemplateId, RecordId, FieldName) VALUES ($templateId,$recordId,$name)",
                    ("$templateId", record.TemplateId), ("$recordId", record.RecordId), ("$name", field));
            Execute(connection, transaction, "INSERT OR IGNORE INTO Orders (OrderId, OrderName) VALUES ($orderId,$orderName)", ("$orderId", record.OrderId), ("$orderName", record.OrderName));
            Execute(connection, transaction, "INSERT INTO RemotePrintRecordSources (RecordId, SourceDeviceId, SourceEventId) VALUES ($recordId,$deviceId,$eventId)",
                ("$recordId", record.RecordId), ("$deviceId", syncEvent.DeviceId), ("$eventId", syncEvent.EventId));
            transaction.Commit();
            return SyncEventApplyResult.Applied;
        }

        private SyncEventApplyResult MergePrintJobEvent(SyncEvent syncEvent, SyncSnapshotPayload payload, byte[] content)
        {
            SyncDataAdapter.SyncPrintJobEvent item;
            try { item = JsonSerializer.Deserialize<SyncDataAdapter.SyncPrintJobEvent>(content) ?? throw new InvalidDataException("打印作业事件内容为空。"); }
            catch (JsonException ex) { throw new InvalidDataException("打印作业事件 JSON 无效。", ex); }
            var identityHash = SyncDataAdapter.ComputeSha256(Encoding.UTF8.GetBytes(string.Join("\n", item.JobId, item.State, item.UpdatedAtUtc)));
            if (!string.Equals(syncEvent.EntityId, $"{item.JobId}:{identityHash}", StringComparison.Ordinal))
                throw new InvalidDataException("打印作业事件身份不一致。");

            EnsureHistoryDirectory();
            using var connection = OpenHistoryConnection();
            using var transaction = connection.BeginTransaction(deferred: false);
            EnsureSharedTables(connection, transaction);
            var existing = ReadScalar(connection, transaction, "SELECT PayloadHash FROM RemotePrintJobEvents WHERE EventIdentity=$identity LIMIT 1", ("$identity", syncEvent.EntityId));
            if (existing != null)
            {
                transaction.Commit();
                return string.Equals(existing, payload.Sha256, StringComparison.OrdinalIgnoreCase)
                    ? SyncEventApplyResult.AlreadyApplied : SyncEventApplyResult.Conflict;
            }
            Execute(connection, transaction, "INSERT INTO RemotePrintJobEvents (EventIdentity, SourceDeviceId, SourceEventId, JobId, IdempotencyKey, RequestHash, State, RequestJson, CompletionJson, CreatedAtUtc, UpdatedAtUtc, PayloadHash) VALUES ($identity,$deviceId,$eventId,$jobId,$key,$requestHash,$state,$requestJson,$completionJson,$createdAt,$updatedAt,$payloadHash)",
                ("$identity", syncEvent.EntityId), ("$deviceId", syncEvent.DeviceId), ("$eventId", syncEvent.EventId), ("$jobId", item.JobId),
                ("$key", item.IdempotencyKey), ("$requestHash", item.RequestHash), ("$state", item.State), ("$requestJson", item.RequestJson),
                ("$completionJson", item.CompletionJson), ("$createdAt", item.CreatedAtUtc), ("$updatedAt", item.UpdatedAtUtc), ("$payloadHash", payload.Sha256));
            transaction.Commit();
            return SyncEventApplyResult.Applied;
        }

        private string ReadAppendOnlyLocalJson(string entityType, string entityId)
        {
            using var connection = OpenHistoryConnection();
            return string.Equals(entityType, "PrintRecord", StringComparison.Ordinal)
                ? ReadScalar(connection, null, "SELECT Json FROM PrintRecords WHERE RecordId=$id LIMIT 1", ("$id", entityId)) ?? ""
                : ReadScalar(connection, null, "SELECT PayloadHash FROM RemotePrintJobEvents WHERE EventIdentity=$id LIMIT 1", ("$id", entityId)) ?? "";
        }

        private static bool IsAppendOnly(string entityType) =>
            string.Equals(entityType, "PrintRecord", StringComparison.Ordinal) || string.Equals(entityType, "PrintJobEvent", StringComparison.Ordinal);

        private void EnsureHistoryDirectory()
        {
            var directory = Path.GetDirectoryName(_historyDatabasePath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        }

        private SqliteConnection OpenHistoryConnection()
        {
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _historyDatabasePath }.ToString());
            connection.Open();
            return connection;
        }

        private static void EnsureSharedTables(SqliteConnection connection, SqliteTransaction transaction)
        {
            Execute(connection, transaction, "CREATE TABLE IF NOT EXISTS PrintRecords (RecordId TEXT PRIMARY KEY, OrderId TEXT, TemplateId TEXT, TemplateName TEXT, TemplatePath TEXT, PrintTime TEXT, Status TEXT, Printer TEXT, Copies INTEGER, OperatorName TEXT, ReprintReason TEXT, TemplateVersion TEXT, DiagnosticDetails TEXT, OrderName TEXT, RecordChecksum TEXT, Json TEXT NOT NULL)");
            Execute(connection, transaction, "CREATE TABLE IF NOT EXISTS FieldValues (RecordId TEXT NOT NULL, FieldName TEXT NOT NULL, FieldValue TEXT, TemplateId TEXT, OrderId TEXT)");
            Execute(connection, transaction, "CREATE TABLE IF NOT EXISTS TemplateSnapshots (TemplateId TEXT, RecordId TEXT, FieldName TEXT)");
            Execute(connection, transaction, "CREATE TABLE IF NOT EXISTS Orders (OrderId TEXT PRIMARY KEY, OrderName TEXT)");
            Execute(connection, transaction, "CREATE TABLE IF NOT EXISTS RemotePrintRecordSources (RecordId TEXT PRIMARY KEY, SourceDeviceId TEXT NOT NULL, SourceEventId TEXT NOT NULL)");
            Execute(connection, transaction, "CREATE TABLE IF NOT EXISTS RemotePrintJobEvents (EventIdentity TEXT PRIMARY KEY, SourceDeviceId TEXT NOT NULL, SourceEventId TEXT NOT NULL, JobId TEXT NOT NULL, IdempotencyKey TEXT NOT NULL, RequestHash TEXT NOT NULL, State TEXT NOT NULL, RequestJson TEXT NOT NULL, CompletionJson TEXT NOT NULL, CreatedAtUtc TEXT NOT NULL, UpdatedAtUtc TEXT NOT NULL, PayloadHash TEXT NOT NULL)");
            Execute(connection, transaction, "CREATE INDEX IF NOT EXISTS IX_RemotePrintJobEvents_JobId ON RemotePrintJobEvents(JobId, UpdatedAtUtc)");
        }

        private static string ReadScalar(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string Name, object Value)[] parameters)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? "");
            return command.ExecuteScalar()?.ToString();
        }

        private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string Name, object Value)[] parameters)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? "");
            command.ExecuteNonQuery();
        }

        private byte[] RewriteTemplatePaths(byte[] content, IReadOnlyDictionary<string, string> templatePaths)
        {
            if (templatePaths == null || templatePaths.Count == 0) return content;
            var root = JsonNode.Parse(content) ?? throw new InvalidDataException("同步 JSON 内容为空。");
            RewriteNode(root, templatePaths);
            return Encoding.UTF8.GetBytes(root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        private void RewriteNode(JsonNode node, IReadOnlyDictionary<string, string> templatePaths)
        {
            if (node is JsonObject jsonObject)
            {
                foreach (var property in jsonObject.ToArray())
                {
                    if (property.Value is JsonValue value && value.TryGetValue<string>(out var text) && templatePaths.TryGetValue(text ?? "", out var hash))
                    {
                        var cached = Path.Combine(_templateCacheDirectory, hash + ".btw");
                        if (File.Exists(cached)) jsonObject[property.Key] = cached;
                    }
                    else if (property.Value != null) RewriteNode(property.Value, templatePaths);
                }
            }
            else if (node is JsonArray array)
            {
                foreach (var item in array.Where(item => item != null)) RewriteNode(item, templatePaths);
            }
        }

        private static SyncSnapshotPayload DeserializePayload(string json)
        {
            try { return JsonSerializer.Deserialize<SyncSnapshotPayload>(json) ?? throw new InvalidDataException("同步快照内容为空。"); }
            catch (JsonException ex) { throw new InvalidDataException("同步快照格式无效。", ex); }
        }

        private static byte[] DecodeContent(SyncSnapshotPayload payload)
        {
            try { return Convert.FromBase64String(payload.ContentBase64 ?? ""); }
            catch (FormatException ex) { throw new InvalidDataException("同步快照编码无效。", ex); }
        }

        private static void ValidateJsonArray(byte[] content, string displayName)
        {
            try
            {
                using var document = JsonDocument.Parse(content);
                if (document.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException($"{displayName}快照根节点必须为数组。");
            }
            catch (JsonException ex) { throw new InvalidDataException($"{displayName}快照 JSON 无效。", ex); }
        }

        private static void WriteAtomicBytes(string path, byte[] content)
        {
            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var temporaryPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporaryPath, content);
                File.Move(temporaryPath, fullPath, true);
            }
            finally
            {
                try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch (IOException) { }
            }
        }
    }

    internal sealed class SyncApplicationService : ISyncPageService, ISyncLifecycleService, IDisposable
    {
        private const string Root = "BarTenderPrinterSync";
        private readonly ISyncConnectionProfileStore _profiles;
        private readonly SyncStore _store;
        private readonly ISyncDataAdapter _dataAdapter;
        private readonly Func<SyncConnectionProfile, ICloudObjectStore> _cloudFactory;
        private readonly FileSnapshotSyncEventApplier _applier;
        private readonly LocalEndpointCollector _endpointCollector;
        private readonly string _templateCacheDirectory;
        private readonly Func<string, bool> _urlPolicy;
        private readonly long _snapshotEventThreshold;
        private readonly long _snapshotByteThreshold;
        private readonly object _operationGate = new object();
        private CancellationTokenSource _activeOperation;
        private Task _currentOperation = Task.CompletedTask;
        private bool _disposed;
        private SyncConnectionProfile _profile;
        private DirectSyncHost _directHost;
        private readonly object _directHostGate = new object();

        public bool IsConfigured => _profile != null;
        public event EventHandler<SharedDataChangedEventArgs> SharedDataChanged;

        public SyncApplicationService()
            : this(new SyncConnectionProfileStore(AppPaths.SyncProfileFile), new SyncStore(AppPaths.SyncDatabaseFile),
                new SyncDataAdapter(), profile => new WebDavObjectStore(new Uri(profile.WebDavUrl), profile.UserName, profile.ApplicationPassword),
                AppPaths.OrdersFile, AppPaths.TemplateSettingsFile, AppPaths.SyncIncomingDirectory, AppPaths.SyncTemplateCacheDirectory)
        {
        }

        internal SyncApplicationService(ISyncConnectionProfileStore profiles, SyncStore store, ISyncDataAdapter dataAdapter,
            Func<SyncConnectionProfile, ICloudObjectStore> cloudFactory, string ordersPath, string settingsPath,
            string incomingDirectory, string templateCacheDirectory, LocalEndpointCollector endpointCollector = null,
            Func<string, bool> urlPolicy = null, long snapshotEventThreshold = 500, long snapshotByteThreshold = 20 * 1024 * 1024)
        {
            _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _dataAdapter = dataAdapter ?? throw new ArgumentNullException(nameof(dataAdapter));
            _cloudFactory = cloudFactory ?? throw new ArgumentNullException(nameof(cloudFactory));
            _applier = new FileSnapshotSyncEventApplier(ordersPath, settingsPath, incomingDirectory, templateCacheDirectory,
                sharedDataChanged: entityType => SharedDataChanged?.Invoke(this, new SharedDataChangedEventArgs(new[] { entityType })));
            _endpointCollector = endpointCollector ?? new LocalEndpointCollector();
            _templateCacheDirectory = Path.GetFullPath(templateCacheDirectory);
            _urlPolicy = urlPolicy ?? SyncWebDavUrlPolicy.IsAllowed;
            _snapshotEventThreshold = snapshotEventThreshold;
            _snapshotByteThreshold = snapshotByteThreshold;
            TryLoadProfile();
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try { await RefreshDirectHostAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { AddActivity($"直连监听启动失败：{SafeError(ex)}"); }
        }

        public Task<SyncPageState> GetStateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool isBusy;
            lock (_operationGate) isBusy = !_currentOperation.IsCompleted;
            var pending = _store.GetPendingOutboxSummary();
            var devices = _store.GetKnownDevices();
            var conflicts = _store.GetPendingConflicts();
            var usage = _store.GetUsage(DateTime.UtcNow.ToString("yyyy-MM", CultureInfo.InvariantCulture));
            var activities = _store.GetRecentActivities(20);
            var quarantinedCount = _store.GetQuarantinedObjectCount();
            var blockedCount = _store.GetPermanentOutboxFailureCount();
            return Task.FromResult(new SyncPageState
            {
                ConnectionState = isBusy ? SyncConnectionState.Running : _profile == null ? SyncConnectionState.NotConfigured : conflicts.Count > 0 || quarantinedCount > 0 || blockedCount > 0 ? SyncConnectionState.NeedsAttention : SyncConnectionState.Ready,
                WorkspaceName = _profile?.WorkspaceName ?? "尚未配置协作空间",
                DeviceName = Environment.MachineName,
                ActiveChannel = IsDirectHostListening ? "WebDAV + 专网直连" : "WebDAV",
                LastSuccessfulSyncUtc = _profile?.LastSuccessfulSyncUtc,
                PendingEventCount = pending.Count,
                PendingBytes = pending.Bytes,
                DeviceCount = devices.Count,
                DirectDeviceCount = devices.Select(ToDeviceState).Count(device => device.DirectSyncEnabled),
                ConflictCount = conflicts.Count,
                QuarantinedObjectCount = quarantinedCount,
                BlockedOutboxCount = blockedCount,
                IsBusy = isBusy,
                DirectSyncEnabled = _profile?.DirectSyncEnabled == true,
                DirectSyncPort = _profile?.DirectSyncPort ?? 45873,
                StatusText = isBusy ? "正在处理同步操作..." : _profile == null ? "配置连接后可开始加密同步。" : "同步服务已就绪。",
                Devices = devices.Select(ToDeviceState).ToArray(),
                Conflicts = conflicts.Select(conflict => new SyncConflictStateItem
                {
                    ConflictId = conflict.ConflictId, EntityType = conflict.EntityType,
                    EntityId = conflict.EntityId, CreatedAtUtc = conflict.CreatedAtUtc
                }).ToArray(),
                Usage = usage,
                RecentActivities = activities.Select(activity => new SyncActivityState
                {
                    Description = activity.Description, OccurredAtUtc = activity.OccurredAtUtc
                }).ToArray()
            });
        }

        public async Task<SyncOperationResult> SynchronizeAsync(CancellationToken cancellationToken)
        {
            if (_profile == null) return SyncOperationResult.Failure("请先创建或导入协作空间。");
            CancellationTokenSource localCancellation;
            Task<SyncOperationResult> currentTask;
            lock (_operationGate)
            {
                if (_disposed) return SyncOperationResult.Failure("同步服务已关闭。");
                if (!_currentOperation.IsCompleted) return SyncOperationResult.Failure("已有同步操作正在运行。");
                localCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _activeOperation = localCancellation;
                currentTask = SynchronizeCoreAsync(localCancellation.Token);
                _currentOperation = currentTask;
            }
            try
            {
                return await currentTask.ConfigureAwait(false);
            }
            finally
            {
                lock (_operationGate)
                {
                    if (ReferenceEquals(_currentOperation, currentTask))
                    {
                        _currentOperation = Task.CompletedTask;
                        _activeOperation = null;
                    }
                }
                localCancellation.Dispose();
            }
        }

        private async Task<SyncOperationResult> SynchronizeCoreAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var cloud = _cloudFactory(_profile);
                await EnsureRemoteLayoutAsync(cloud, _profile, cancellationToken).ConfigureAwait(false);
                await RefreshDirectHostAsync(cancellationToken).ConfigureAwait(false);
                var endpointRegistry = new DeviceEndpointRegistry(cloud, _profile, _store);
                if (IsDirectHostListening) await PublishDirectEndpointAsync(endpointRegistry, cancellationToken).ConfigureAwait(false);
                await DownloadTemplatesAsync(cloud, cancellationToken).ConfigureAwait(false);
                var snapshotManager = new SnapshotManager(_profile, _store, _applier, _snapshotEventThreshold, _snapshotByteThreshold);
                var requiresRemoteBaseline = _profile.RemoteBaselineEstablished == false;
                var restored = requiresRemoteBaseline && await snapshotManager.TryRestoreInitialAsync(cloud, cancellationToken).ConfigureAwait(false);
                var coordinator = new SyncCoordinator(_profile, _profile.DeviceId, cloud, _store, new SyncEventObjectCodec(), _applier,
                    new DirectSyncClient(endpointRegistry));
                var pullResult = requiresRemoteBaseline
                    ? await coordinator.SynchronizeAsync(_profile.DirectSyncEnabled, cancellationToken, uploadPending: false).ConfigureAwait(false)
                    : null;
                if (requiresRemoteBaseline)
                {
                    _profile.RemoteBaselineEstablished = true;
                    _profiles.SaveLocal(_profile);
                }
                var captureLocal = _profile.IsWorkspaceCreator || (!requiresRemoteBaseline && _profile.LocalCaptureEnabled != false);
                var snapshot = captureLocal
                    ? await _dataAdapter.CaptureAsync(cancellationToken).ConfigureAwait(false)
                    : new SyncDataSnapshot();
                var queued = QueueChangedSnapshots(snapshot);
                var templateUpload = await UploadTemplatesAsync(cloud, snapshot.Templates, cancellationToken).ConfigureAwait(false);
                var result = captureLocal
                    ? await coordinator.SynchronizeAsync(_profile.DirectSyncEnabled, cancellationToken).ConfigureAwait(false)
                    : pullResult ?? await coordinator.SynchronizeAsync(_profile.DirectSyncEnabled, cancellationToken, uploadPending: false).ConfigureAwait(false);
                var currentSnapshot = captureLocal
                    ? await _dataAdapter.CaptureAsync(cancellationToken).ConfigureAwait(false)
                    : new SyncDataSnapshot();
                var snapshotCreated = captureLocal && await snapshotManager.TryCreateAsync(cloud, currentSnapshot, cancellationToken).ConfigureAwait(false);
                _profile.LastSuccessfulSyncUtc = DateTimeOffset.UtcNow;
                _profiles.SaveLocal(_profile);
                _store.AddUsage(DateTime.UtcNow.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                    templateUpload + snapshot.Files.Sum(file => file.Content.LongLength), 0, 1);
                var applied = result.AppliedEvents + (captureLocal ? pullResult?.AppliedEvents ?? 0 : 0);
                var conflicts = result.Conflicts + (captureLocal ? pullResult?.Conflicts ?? 0 : 0);
                var quarantined = result.QuarantinedObjects + (captureLocal ? pullResult?.QuarantinedObjects ?? 0 : 0);
                var message = $"同步完成：新增 {queued} 个事件，上传 {result.UploadedEvents} 个事件，应用 {applied} 个远端事件，冲突 {conflicts} 个，隔离 {quarantined} 个，阻断 {result.BlockedUploads} 个{(restored ? "，已从快照恢复" : "")}{(snapshotCreated ? "，已生成快照" : "")}。";
                AddActivity(message);
                return SyncOperationResult.Success(message);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                var message = SafeError(ex);
                AddActivity(message);
                return SyncOperationResult.Failure(message);
            }
        }

        public Task<SyncOperationResult> CancelAsync(CancellationToken cancellationToken)
        {
            CancellationTokenSource operation;
            lock (_operationGate) operation = _activeOperation;
            try { operation?.Cancel(); } catch (ObjectDisposedException) { }
            return Task.FromResult(SyncOperationResult.Success("已请求取消当前同步操作。"));
        }

        public async Task<bool> CancelAndWaitAsync(TimeSpan timeout)
        {
            Task operation;
            CancellationTokenSource cancellation;
            lock (_operationGate) { operation = _currentOperation; cancellation = _activeOperation; }
            try { cancellation?.Cancel(); } catch (ObjectDisposedException) { }
            if (operation.IsCompleted) return true;
            return ReferenceEquals(await Task.WhenAny(operation, Task.Delay(timeout)).ConfigureAwait(false), operation);
        }

        public async Task QueueLocalChangesAsync(CancellationToken cancellationToken)
        {
            if (_profile == null) return;
            CancellationTokenSource localCancellation;
            Task currentTask;
            lock (_operationGate)
            {
                if (_disposed) return;
                if (!_currentOperation.IsCompleted) throw new InvalidOperationException("已有同步操作正在运行。");
                localCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                currentTask = QueueLocalChangesCoreAsync(localCancellation.Token);
                _activeOperation = localCancellation;
                _currentOperation = currentTask;
            }
            try { await currentTask.ConfigureAwait(false); }
            finally
            {
                lock (_operationGate)
                {
                    if (ReferenceEquals(_currentOperation, currentTask))
                    {
                        _currentOperation = Task.CompletedTask;
                        _activeOperation = null;
                    }
                }
                localCancellation.Dispose();
            }
        }

        private async Task QueueLocalChangesCoreAsync(CancellationToken cancellationToken)
        {
            var snapshot = await _dataAdapter.CaptureAsync(cancellationToken).ConfigureAwait(false);
            QueueChangedSnapshots(snapshot);
            if (_profile.LocalCaptureEnabled != true)
            {
                _profile.LocalCaptureEnabled = true;
                _profiles.SaveLocal(_profile);
            }
        }

        public async Task<bool> FlushAndStopAsync(TimeSpan timeout)
        {
            using var timeoutCancellation = new CancellationTokenSource(timeout);
            try
            {
                await CancelAndWaitAsync(timeout).ConfigureAwait(false);
                if (_profile != null && !timeoutCancellation.IsCancellationRequested)
                {
                    if (_profile.LocalCaptureEnabled != false)
                        await QueueLocalChangesAsync(timeoutCancellation.Token).ConfigureAwait(false);
                    await SynchronizeAsync(timeoutCancellation.Token).ConfigureAwait(false);
                }
            }
            finally { await StopDirectHostAsync().ConfigureAwait(false); }
            return !timeoutCancellation.IsCancellationRequested;
        }

        public async Task<SyncOperationResult> CreateWorkspaceAsync(SyncConnectionRequest request, CancellationToken cancellationToken)
        {
            var validation = ValidateRequest(request, true);
            if (validation != null) return SyncOperationResult.Failure(validation);
            var profile = CreateProfile(request);
            try
            {
                using var cloud = _cloudFactory(profile);
                await TestCloudAsync(cloud, cancellationToken).ConfigureAwait(false);
                await EnsureRemoteLayoutAsync(cloud, profile, cancellationToken).ConfigureAwait(false);
                var spaceDescriptor = JsonSerializer.SerializeToUtf8Bytes(new { SchemaVersion = 1, profile.SpaceId, profile.WorkspaceName, CreatedAtUtc = DateTime.UtcNow });
                var encryptedDescriptor = SyncCrypto.EncryptObject(spaceDescriptor, profile.DataKey, profile.SpaceId, "space", profile.SpaceId);
                await PutIdempotentlyAsync(cloud, $"{Root}/spaces/{profile.SpaceId}/space.enc", encryptedDescriptor, cancellationToken).ConfigureAwait(false);
                _profiles.SaveLocal(profile);
                _profile = profile;
                await RefreshDirectHostAsync(cancellationToken).ConfigureAwait(false);
                return SyncOperationResult.Success("协作空间已创建并保存到本机。");
            }
            catch (Exception ex) { return SyncOperationResult.Failure(SafeError(ex)); }
        }

        public async Task<SyncOperationResult> ImportConnectionAsync(string filePath, string sharedPassword, CancellationToken cancellationToken)
        {
            var profileSaved = false;
            try
            {
                if (!string.Equals(Path.GetExtension(filePath), ".btpsync", StringComparison.OrdinalIgnoreCase)) return SyncOperationResult.Failure("请选择 .btpsync 连接文件。");
                var imported = _profiles.Import(await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false), sharedPassword);
                if (!_urlPolicy(imported.WebDavUrl)) throw new ArgumentException("WebDAV 地址不受信任。");
                imported.DeviceId = Guid.NewGuid().ToString("N");
                using var cloud = _cloudFactory(imported);
                var descriptorObject = await cloud.GetAsync($"{Root}/spaces/{imported.SpaceId}/space.enc", cancellationToken).ConfigureAwait(false);
                var descriptorPlaintext = SyncCrypto.DecryptObject(descriptorObject.Content, imported.DataKey, imported.SpaceId, "space", imported.SpaceId);
                try
                {
                    var descriptor = JsonSerializer.Deserialize<SyncSpaceDescriptor>(descriptorPlaintext) ?? throw new InvalidDataException("协作空间描述为空。");
                    if (descriptor.SchemaVersion != 1 || string.IsNullOrWhiteSpace(descriptor.WorkspaceName) ||
                        !string.Equals(descriptor.SpaceId, imported.SpaceId, StringComparison.Ordinal) ||
                        !string.Equals(descriptor.WorkspaceName, imported.WorkspaceName, StringComparison.Ordinal))
                        throw new InvalidDataException("连接文件与远端协作空间不匹配。");
                }
                finally { CryptographicOperations.ZeroMemory(descriptorPlaintext); }
                imported.RemoteBaselineEstablished = false;
                imported.IsWorkspaceCreator = false;
                imported.LocalCaptureEnabled = false;
                _profiles.SaveLocal(imported);
                _profile = imported;
                profileSaved = true;
                await RefreshDirectHostAsync(cancellationToken).ConfigureAwait(false);
                var initialSync = await SynchronizeCoreAsync(cancellationToken).ConfigureAwait(false);
                return initialSync.Succeeded
                    ? SyncOperationResult.Success("连接文件已导入、验证并完成首次同步。")
                    : SyncOperationResult.Failure($"已加入协作空间，但首次同步失败：{initialSync.Message} 配置已保留，可直接重试同步。");
            }
            catch (Exception ex) when (profileSaved)
            {
                return SyncOperationResult.Failure($"已加入协作空间，但首次同步失败：{SafeError(ex)} 配置已保留，可直接重试同步。");
            }
            catch (Exception ex) { return SyncOperationResult.Failure(SafeError(ex)); }
        }

        public async Task<SyncOperationResult> ExportConnectionAsync(string filePath, string sharedPassword, CancellationToken cancellationToken)
        {
            if (_profile == null) return SyncOperationResult.Failure("当前没有可导出的连接配置。");
            try
            {
                var exported = CloneForExport(_profile);
                var content = _profiles.Export(exported, sharedPassword);
                await WriteAtomicAsync(filePath, content, cancellationToken).ConfigureAwait(false);
                return SyncOperationResult.Success("连接文件已加密导出。");
            }
            catch (Exception ex) { return SyncOperationResult.Failure(SafeError(ex)); }
        }

        public async Task<SyncOperationResult> TestWebDavAsync(SyncConnectionRequest request, CancellationToken cancellationToken)
        {
            try
            {
                var profile = HasCredentials(request) ? CreateProfile(request, false) : _profile;
                if (profile == null) return SyncOperationResult.Failure("请填写 WebDAV 地址、账号和应用密码。");
                using var cloud = _cloudFactory(profile);
                await TestCloudAsync(cloud, cancellationToken).ConfigureAwait(false);
                return SyncOperationResult.Success("WebDAV 连接和写入能力正常。");
            }
            catch (Exception ex) { return SyncOperationResult.Failure(SafeError(ex)); }
        }

        public async Task<SyncOperationResult> ConfigureDirectSyncAsync(bool enabled, int port, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_profile == null) return SyncOperationResult.Failure("请先配置协作空间。");
            if (port < 1024 || port > 65535) return SyncOperationResult.Failure("直连端口必须在 1024 到 65535 之间。");
            var changed = _profile.DirectSyncPort != port;
            _profile.DirectSyncEnabled = enabled;
            _profile.DirectSyncPort = port;
            _profiles.SaveLocal(_profile);
            try
            {
                await RefreshDirectHostAsync(cancellationToken, changed).ConfigureAwait(false);
                if (!enabled) return SyncOperationResult.Success("直连已关闭。");
                return SyncOperationResult.Success($"专网直连正在端口 {_directHost.Port} 监听。");
            }
            catch (Exception ex)
            {
                await StopDirectHostAsync().ConfigureAwait(false);
                return SyncOperationResult.Failure(SafeError(ex));
            }
        }

        public async Task<SyncOperationResult> PublishDirectEndpointAsync(CancellationToken cancellationToken)
        {
            if (_profile == null) return SyncOperationResult.Failure("请先配置协作空间。");
            try
            {
                await EnsureDirectHostAsync(cancellationToken).ConfigureAwait(false);
                using var cloud = _cloudFactory(_profile);
                await PublishDirectEndpointAsync(new DeviceEndpointRegistry(cloud, _profile, _store), cancellationToken).ConfigureAwait(false);
                return SyncOperationResult.Success("本机专网直连端点已加密发布。");
            }
            catch (Exception ex) { return SyncOperationResult.Failure(SafeError(ex)); }
        }

        public async Task<SyncOperationResult> TestDirectConnectionAsync(string deviceId, CancellationToken cancellationToken)
        {
            if (_profile == null) return SyncOperationResult.Failure("请先配置协作空间。");
            if (string.IsNullOrWhiteSpace(deviceId)) return SyncOperationResult.Failure("请选择目标设备。");
            try
            {
                using var cloud = _cloudFactory(_profile);
                var registry = new DeviceEndpointRegistry(cloud, _profile, _store);
                var source = new FilteredEndpointSource(registry, deviceId);
                var result = await new DirectSyncClient(source).TrySynchronizeAsync(_profile, _profile.DeviceId,
                    _store.GetCursors(), Array.Empty<SyncOutboxItem>(), cancellationToken).ConfigureAwait(false);
                return result.Succeeded ? SyncOperationResult.Success("目标设备专网直连正常。") :
                    SyncOperationResult.Failure(result.AuthenticationFailed ? "目标设备直连认证失败。" : "目标设备当前无法直连。");
            }
            catch (Exception ex) { return SyncOperationResult.Failure(SafeError(ex)); }
        }

        public async Task<SyncOperationResult> ResolveConflictAsync(string conflictId, string resolution, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(conflictId)) return SyncOperationResult.Failure("请选择要处理的冲突。");
            var pending = _store.GetConflict(conflictId);
            if (pending == null || pending.State != SyncConflictState.Pending) return SyncOperationResult.Failure("没有可处理的待定冲突。");
            if (string.Equals(pending.EntityType, "PrintRecord", StringComparison.Ordinal) || string.Equals(pending.EntityType, "PrintJobEvent", StringComparison.Ordinal))
                return SyncOperationResult.Failure("仅追加打印实体冲突需要人工审计，禁止覆盖历史内容。");
            if (!string.Equals(resolution, "采用远端版本", StringComparison.Ordinal) && !string.Equals(resolution, "保留本地版本", StringComparison.Ordinal))
                return SyncOperationResult.Failure("逐字段合并需要在差异编辑器中提供合并内容。");

            SyncEvent remoteEvent;
            try
            {
                remoteEvent = JsonSerializer.Deserialize<SyncEvent>(pending.RemoteJson) ?? throw new InvalidDataException("远端冲突事件为空。");
                if (string.IsNullOrWhiteSpace(remoteEvent.PayloadJson))
                {
                    remoteEvent = new SyncEvent
                    {
                        EventId = pending.ConflictId, EntityType = pending.EntityType, EntityId = pending.EntityId,
                        NewVersion = _store.GetEntityState(pending.EntityType, pending.EntityId).Version,
                        PayloadJson = pending.RemoteJson
                    };
                }
            }
            catch (JsonException ex) { throw new InvalidDataException("远端冲突事件无效。", ex); }
            var localState = _store.GetEntityState(pending.EntityType, pending.EntityId);
            var baseVersion = Math.Max(localState.Version, remoteEvent.NewVersion) + 1;
            string payloadJson;
            if (string.Equals(resolution, "采用远端版本", StringComparison.Ordinal))
            {
                payloadJson = remoteEvent.PayloadJson;
                _applier.ApplyResolvedRemote(pending, remoteEvent, _store, baseVersion + 1);
            }
            else
            {
                var snapshot = await _dataAdapter.CaptureAsync(cancellationToken).ConfigureAwait(false);
                payloadJson = CreatePayloadJson(snapshot, pending.EntityType, pending.EntityId);
            }
            EnqueueResolutionEvent(pending, payloadJson, baseVersion);
            _store.ResolveConflict(pending.ConflictId, JsonSerializer.Serialize(new { resolution }));
            return SyncOperationResult.Success("冲突已解决，决议事件已加入待上传队列。");
        }

        public async Task<SyncOperationResult> ExportDiagnosticsAsync(string filePath, CancellationToken cancellationToken)
        {
            try
            {
                var pending = _store.GetPendingOutboxSummary();
                var usage = _store.GetUsage(DateTime.UtcNow.ToString("yyyy-MM", CultureInfo.InvariantCulture));
                var text = string.Join(Environment.NewLine, new[]
                {
                    "BarTenderPrinter Sync Diagnostics", $"GeneratedUtc={DateTime.UtcNow:O}",
                    $"Configured={_profile != null}", $"SpaceId={Redact(_profile?.SpaceId)}", $"DeviceId={Redact(_profile?.DeviceId)}",
                    $"WebDavHost={GetSafeHost(_profile?.WebDavUrl)}", $"PendingEvents={pending.Count}", $"PendingBytes={pending.Bytes}",
                    $"Conflicts={_store.GetPendingConflicts().Count}", $"KnownDevices={_store.GetKnownDevices().Count}",
                    $"QuarantinedObjects={_store.GetQuarantinedObjectCount()}", $"BlockedOutbox={_store.GetPermanentOutboxFailureCount()}",
                    $"UploadedBytesThisMonth={usage.UploadedBytes}", $"DownloadedBytesThisMonth={usage.DownloadedBytes}", $"RequestsThisMonth={usage.RequestCount}",
                    $"DirectListenerEnabled={IsDirectHostListening}", $"TemplateCacheFiles={CountFiles(_templateCacheDirectory)}"
                });
                await WriteAtomicAsync(filePath, Encoding.UTF8.GetBytes(text), cancellationToken).ConfigureAwait(false);
                return SyncOperationResult.Success("脱敏诊断已导出。");
            }
            catch (Exception ex) { return SyncOperationResult.Failure(SafeError(ex)); }
        }

        private int QueueChangedSnapshots(SyncDataSnapshot snapshot)
        {
            var queued = 0;
            var templatePaths = snapshot.Templates.ToDictionary(template => template.SourcePath, template => template.Sha256, StringComparer.OrdinalIgnoreCase);
            foreach (var file in snapshot.Files)
            {
                var entityType = EntityType(file.Kind);
                var eventContent = file.Kind is SyncSnapshotKind.Orders or SyncSnapshotKind.TemplateSettings
                    ? CanonicalizeTemplatePaths(file.Content, templatePaths)
                    : file.Content;
                var contentHash = SyncCrypto.ComputeSha256Hex(eventContent);
                var state = _store.GetEntityState(entityType, file.ObjectId);
                if (string.Equals(state.ContentHash, contentHash, StringComparison.OrdinalIgnoreCase)) continue;
                var sequence = _store.ReserveNextSequence(_profile.DeviceId);
                var syncEvent = new SyncEvent
                {
                    EventId = $"{_profile.DeviceId}:{sequence}", DeviceId = _profile.DeviceId, Sequence = sequence,
                    EntityType = entityType, EntityId = file.ObjectId,
                    BaseVersion = file.Kind is SyncSnapshotKind.PrintRecord or SyncSnapshotKind.PrintJobEvent ? 0 : state.Version,
                    NewVersion = file.Kind is SyncSnapshotKind.PrintRecord or SyncSnapshotKind.PrintJobEvent ? 1 : state.Version + 1,
                    OccurredAtUtc = DateTime.UtcNow,
                    PayloadJson = JsonSerializer.Serialize(new SyncSnapshotPayload
                    {
                        Kind = file.Kind.ToString(), Sha256 = contentHash, ContentBase64 = Convert.ToBase64String(eventContent),
                        TemplatePaths = templatePaths.Concat(templatePaths.Select(item => new KeyValuePair<string, string>("btpsync-template:" + item.Value, item.Value)))
                            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First().Value, StringComparer.OrdinalIgnoreCase)
                    })
                };
                var plaintext = JsonSerializer.SerializeToUtf8Bytes(syncEvent);
                var encrypted = SyncCrypto.EncryptObject(plaintext, _profile.DataKey, _profile.SpaceId, "event", syncEvent.EventId);
                var path = $"{Root}/spaces/{_profile.SpaceId}/events/{_profile.DeviceId}/{sequence}.evt";
                if (_store.Enqueue(new SyncOutboxItem { EventId = syncEvent.EventId, DeviceId = _profile.DeviceId, Sequence = sequence, ObjectPath = path, EncryptedBlob = encrypted, CreatedAtUtc = DateTimeOffset.UtcNow }))
                {
                    _store.UpsertEntityState(entityType, file.ObjectId, syncEvent.NewVersion, contentHash);
                    queued++;
                }
            }
            return queued;
        }

        private string CreatePayloadJson(SyncDataSnapshot snapshot, string entityType, string entityId)
        {
            var file = snapshot.Files.FirstOrDefault(item => string.Equals(EntityType(item.Kind), entityType, StringComparison.Ordinal) &&
                string.Equals(item.ObjectId, entityId, StringComparison.Ordinal));
            if (file == null) throw new InvalidOperationException("无法捕获冲突实体的本地内容。");
            var templatePaths = snapshot.Templates.ToDictionary(template => template.SourcePath, template => template.Sha256, StringComparer.OrdinalIgnoreCase);
            var content = file.Kind is SyncSnapshotKind.Orders or SyncSnapshotKind.TemplateSettings
                ? CanonicalizeTemplatePaths(file.Content, templatePaths)
                : file.Content;
            return JsonSerializer.Serialize(new SyncSnapshotPayload
            {
                Kind = file.Kind.ToString(), Sha256 = SyncCrypto.ComputeSha256Hex(content), ContentBase64 = Convert.ToBase64String(content),
                TemplatePaths = templatePaths
            });
        }

        private void EnqueueResolutionEvent(SyncConflict conflict, string payloadJson, long baseVersion)
        {
            var sequence = _store.ReserveNextSequence(_profile.DeviceId);
            var syncEvent = new SyncEvent
            {
                EventId = $"{_profile.DeviceId}:{sequence}", DeviceId = _profile.DeviceId, Sequence = sequence,
                EntityType = conflict.EntityType, EntityId = conflict.EntityId, BaseVersion = baseVersion, NewVersion = baseVersion + 1,
                OccurredAtUtc = DateTime.UtcNow, PayloadJson = payloadJson, ResolvesConflictId = conflict.ConflictId
            };
            var plaintext = JsonSerializer.SerializeToUtf8Bytes(syncEvent);
            var encrypted = SyncCrypto.EncryptObject(plaintext, _profile.DataKey, _profile.SpaceId, "event", syncEvent.EventId);
            var path = $"{Root}/spaces/{_profile.SpaceId}/events/{_profile.DeviceId}/{sequence}.evt";
            if (!_store.Enqueue(new SyncOutboxItem { EventId = syncEvent.EventId, DeviceId = _profile.DeviceId, Sequence = sequence, ObjectPath = path, EncryptedBlob = encrypted, CreatedAtUtc = DateTimeOffset.UtcNow }))
                throw new InvalidOperationException("冲突决议事件无法加入待上传队列。");
            var payload = JsonSerializer.Deserialize<SyncSnapshotPayload>(payloadJson) ?? throw new InvalidDataException("冲突决议载荷为空。");
            _store.UpsertEntityState(conflict.EntityType, conflict.EntityId, syncEvent.NewVersion, payload.Sha256);
        }

        internal static byte[] CanonicalizeTemplatePaths(byte[] content, IReadOnlyDictionary<string, string> templatePaths)
        {
            if (templatePaths.Count == 0) return content;
            var root = JsonNode.Parse(content) ?? throw new InvalidDataException("同步 JSON 内容为空。");
            CanonicalizeNode(root, templatePaths);
            return Encoding.UTF8.GetBytes(root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        private static void CanonicalizeNode(JsonNode node, IReadOnlyDictionary<string, string> templatePaths)
        {
            if (node is JsonObject jsonObject)
            {
                foreach (var property in jsonObject.ToArray())
                {
                    if (property.Value is JsonValue value && value.TryGetValue<string>(out var text) && templatePaths.TryGetValue(text ?? "", out var hash))
                        jsonObject[property.Key] = "btpsync-template:" + hash;
                    else if (property.Value != null) CanonicalizeNode(property.Value, templatePaths);
                }
            }
            else if (node is JsonArray array)
            {
                foreach (var item in array.Where(item => item != null)) CanonicalizeNode(item, templatePaths);
            }
        }

        private async Task<long> UploadTemplatesAsync(ICloudObjectStore cloud, IReadOnlyList<SyncTemplateObject> templates, CancellationToken cancellationToken)
        {
            long bytes = 0;
            foreach (var template in templates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var encrypted = SyncCrypto.EncryptObject(template.Content, _profile.DataKey, _profile.SpaceId, "template", template.Sha256);
                var path = $"{Root}/spaces/{_profile.SpaceId}/templates/{template.Sha256}.enc";
                try
                {
                    await PutTemplateIdempotentlyAsync(cloud, path, encrypted, template.Sha256, cancellationToken).ConfigureAwait(false);
                    bytes += encrypted.LongLength;
                }
                catch (Exception ex) when (ex is SyncSecurityException || ex is InvalidDataException)
                {
                    var errorCode = ex is SyncSecurityException security ? security.ErrorCode : SyncErrorCodes.ObjectCorrupted;
                    _store.RecordQuarantinedObject(path, errorCode);
                }
            }
            return bytes;
        }

        private async Task DownloadTemplatesAsync(ICloudObjectStore cloud, CancellationToken cancellationToken)
        {
            var prefix = $"{Root}/spaces/{_profile.SpaceId}/templates/";
            var objects = await cloud.ListAsync(prefix, cancellationToken).ConfigureAwait(false);
            Directory.CreateDirectory(_templateCacheDirectory);
            foreach (var metadata in objects.Where(item => !item.IsCollection && item.Path.EndsWith(".enc", StringComparison.OrdinalIgnoreCase)))
            {
                var hash = Path.GetFileNameWithoutExtension(metadata.Path);
                if (hash.Length != 64 || hash.Any(character => !Uri.IsHexDigit(character))) continue;
                var cachePath = Path.Combine(_templateCacheDirectory, hash + ".btw");
                if (File.Exists(cachePath))
                {
                    var cached = await File.ReadAllBytesAsync(cachePath, cancellationToken).ConfigureAwait(false);
                    if (string.Equals(SyncCrypto.ComputeSha256Hex(cached), hash, StringComparison.OrdinalIgnoreCase)) continue;
                }
                var remote = await cloud.GetAsync(metadata.Path, cancellationToken).ConfigureAwait(false);
                try
                {
                    var plaintext = SyncCrypto.DecryptObject(remote.Content, _profile.DataKey, _profile.SpaceId, "template", hash);
                    try
                    {
                        if (!string.Equals(SyncCrypto.ComputeSha256Hex(plaintext), hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("远端模板摘要无效。");
                        await WriteAtomicAsync(cachePath, plaintext, cancellationToken).ConfigureAwait(false);
                    }
                    finally { CryptographicOperations.ZeroMemory(plaintext); }
                }
                catch (Exception ex) when (ex is SyncSecurityException || ex is InvalidDataException)
                {
                    var errorCode = ex is SyncSecurityException security ? security.ErrorCode : SyncErrorCodes.ObjectCorrupted;
                    _store.RecordQuarantinedObject(metadata.Path, errorCode);
                }
            }
        }

        private static async Task EnsureRemoteLayoutAsync(ICloudObjectStore cloud, SyncConnectionProfile profile, CancellationToken cancellationToken)
        {
            var paths = new[] { Root, $"{Root}/spaces", $"{Root}/spaces/{profile.SpaceId}", $"{Root}/spaces/{profile.SpaceId}/devices", $"{Root}/spaces/{profile.SpaceId}/events", $"{Root}/spaces/{profile.SpaceId}/events/{profile.DeviceId}", $"{Root}/spaces/{profile.SpaceId}/templates", $"{Root}/spaces/{profile.SpaceId}/snapshots" };
            foreach (var path in paths) await cloud.EnsureCollectionAsync(path, cancellationToken).ConfigureAwait(false);
        }

        private static async Task TestCloudAsync(ICloudObjectStore cloud, CancellationToken cancellationToken)
        {
            await cloud.EnsureCollectionAsync(Root, cancellationToken).ConfigureAwait(false);
            await cloud.ListAsync(Root, cancellationToken).ConfigureAwait(false);
            await cloud.PutAsync($"{Root}/health-check.bin", RandomNumberGenerator.GetBytes(16), cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        private static async Task PutIdempotentlyAsync(ICloudObjectStore cloud, string path, byte[] content, CancellationToken cancellationToken)
        {
            try { await cloud.PutAsync(path, content, createOnly: true, cancellationToken: cancellationToken).ConfigureAwait(false); }
            catch (WebDavPreconditionFailedException)
            {
                var existing = await cloud.GetAsync(path, cancellationToken).ConfigureAwait(false);
                if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(existing.Content), SHA256.HashData(content)))
                    throw new InvalidDataException("远端同名对象内容不一致。");
            }
        }

        private async Task PutTemplateIdempotentlyAsync(ICloudObjectStore cloud, string path, byte[] content, string hash, CancellationToken cancellationToken)
        {
            try { await cloud.PutAsync(path, content, createOnly: true, cancellationToken: cancellationToken).ConfigureAwait(false); }
            catch (WebDavPreconditionFailedException)
            {
                var existing = await cloud.GetAsync(path, cancellationToken).ConfigureAwait(false);
                var plaintext = SyncCrypto.DecryptObject(existing.Content, _profile.DataKey, _profile.SpaceId, "template", hash);
                try
                {
                    if (!string.Equals(SyncCrypto.ComputeSha256Hex(plaintext), hash, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("远端同名模板摘要不一致。");
                }
                finally { CryptographicOperations.ZeroMemory(plaintext); }
            }
        }

        private SyncConnectionProfile CreateProfile(SyncConnectionRequest request, bool requireWorkspace = true)
        {
            var validation = ValidateRequest(request, requireWorkspace);
            if (validation != null) throw new ArgumentException(validation);
            return new SyncConnectionProfile
            {
                WebDavUrl = request.WebDavUrl.Trim(), UserName = request.Account.Trim(), ApplicationPassword = request.ApplicationPassword,
                WorkspaceName = requireWorkspace ? request.WorkspaceName.Trim() : "连接测试", SpaceId = Guid.NewGuid().ToString("N"),
                DeviceId = Guid.NewGuid().ToString("N"), DataKey = SyncCrypto.GenerateDataKey(), IsWorkspaceCreator = requireWorkspace,
                RemoteBaselineEstablished = requireWorkspace ? false : null, LocalCaptureEnabled = requireWorkspace ? true : null
            };
        }

        private string ValidateRequest(SyncConnectionRequest request, bool requireWorkspace)
        {
            if (request == null) return "连接参数不能为空。";
            if (!_urlPolicy(request.WebDavUrl)) return "WebDAV 地址必须为受信任的坚果云 HTTPS DAV 地址。";
            if (string.IsNullOrWhiteSpace(request.Account) || string.IsNullOrWhiteSpace(request.ApplicationPassword)) return "WebDAV 账号和应用密码不能为空。";
            if (requireWorkspace && (string.IsNullOrWhiteSpace(request.WorkspaceName) || request.WorkspaceName.Trim().Length > 100)) return "空间名称不能为空且不能超过 100 个字符。";
            if (requireWorkspace && string.IsNullOrEmpty(request.SharedPassword)) return "共享密码不能为空。";
            return null;
        }

        private static bool HasCredentials(SyncConnectionRequest request) => request != null && !string.IsNullOrWhiteSpace(request.WebDavUrl);

        private static SyncConnectionProfile CloneForExport(SyncConnectionProfile profile) => new SyncConnectionProfile
        {
            SchemaVersion = profile.SchemaVersion, WebDavUrl = profile.WebDavUrl, UserName = profile.UserName,
            ApplicationPassword = profile.ApplicationPassword, SpaceId = profile.SpaceId, DataKey = profile.DataKey.ToArray(),
            WorkspaceName = profile.WorkspaceName, DeviceId = "", DirectSyncEnabled = false, DirectSyncPort = 45873,
            RemoteBaselineEstablished = false, IsWorkspaceCreator = false, LocalCaptureEnabled = false
        };

        private void TryLoadProfile()
        {
            try
            {
                _profile = _profiles.LoadLocal();
                if (string.IsNullOrWhiteSpace(_profile.DeviceId))
                {
                    _profile.DeviceId = Guid.NewGuid().ToString("N");
                    _profiles.SaveLocal(_profile);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is SyncSecurityException || ex is PlatformNotSupportedException) { _profile = null; }
        }

        private bool IsDirectHostListening
        {
            get { lock (_directHostGate) return _directHost?.IsListening == true; }
        }

        private async Task RefreshDirectHostAsync(CancellationToken cancellationToken, bool configurationChanged = false)
        {
            if (_profile?.DirectSyncEnabled != true)
            {
                await StopDirectHostAsync().ConfigureAwait(false);
                return;
            }
            DirectSyncHost host;
            lock (_directHostGate) host = _directHost;
            if (configurationChanged || host?.Port != _profile.DirectSyncPort) await StopDirectHostAsync().ConfigureAwait(false);
            await EnsureDirectHostAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task EnsureDirectHostAsync(CancellationToken cancellationToken)
        {
            if (_profile?.DirectSyncEnabled != true) return;
            lock (_directHostGate)
            {
                if (_directHost?.IsListening == true) return;
            }
            var addresses = _endpointCollector.Collect().Select(item => IPAddress.Parse(item.Value)).ToArray();
            if (addresses.Length == 0) throw new IOException("当前没有可用于专网直连的本机地址。");
            var certificate = new DirectSyncCertificateStore(AppPaths.GetDirectSyncCertificateFile(_profile.DeviceId)).LoadOrCreate(_profile.DeviceId);
            var host = new DirectSyncHost(_profile, _store, certificate, addresses);
            try { await host.StartAsync(_profile.DirectSyncPort, cancellationToken).ConfigureAwait(false); }
            catch { await host.DisposeAsync().ConfigureAwait(false); throw; }
            DirectSyncHost redundant = null;
            lock (_directHostGate)
            {
                if (_directHost == null) _directHost = host;
                else redundant = host;
            }
            if (redundant != null) await redundant.DisposeAsync().ConfigureAwait(false);
        }

        private async Task StopDirectHostAsync()
        {
            DirectSyncHost host;
            lock (_directHostGate) { host = _directHost; _directHost = null; }
            if (host != null) await host.DisposeAsync().ConfigureAwait(false);
        }

        private async Task PublishDirectEndpointAsync(DeviceEndpointRegistry registry, CancellationToken cancellationToken)
        {
            DirectSyncHost host;
            lock (_directHostGate) host = _directHost?.IsListening == true ? _directHost : null;
            if (host == null) throw new IOException("专网直连监听尚未启动。");
            var now = DateTimeOffset.UtcNow;
            await registry.PublishAsync(new DeviceEndpointRecord
            {
                SpaceId = _profile.SpaceId, DeviceId = _profile.DeviceId, DisplayName = Environment.MachineName,
                EndpointVersion = now.ToUnixTimeMilliseconds(), DirectSyncEnabled = true, Port = host.Port,
                CertificateSha256 = host.CertificateSha256, Addresses = _endpointCollector.Collect().ToArray(),
                PublishedAtUtc = now, ExpiresAtUtc = now.AddHours(24)
            }, cancellationToken).ConfigureAwait(false);
        }

        private sealed class FilteredEndpointSource : IPublishedEndpointSource
        {
            private readonly IPublishedEndpointSource _inner;
            private readonly string _deviceId;
            public FilteredEndpointSource(IPublishedEndpointSource inner, string deviceId) { _inner = inner; _deviceId = deviceId; }
            public async Task<IReadOnlyList<PublishedDirectEndpoint>> GetPublishedEndpointsAsync(string spaceId, string localDeviceId, CancellationToken cancellationToken) =>
                (await _inner.GetPublishedEndpointsAsync(spaceId, localDeviceId, cancellationToken).ConfigureAwait(false))
                    .Where(item => string.Equals(item.DeviceId, _deviceId, StringComparison.Ordinal)).ToArray();
        }

        private static SyncDeviceState ToDeviceState(KnownSyncDevice device)
        {
            var displayName = device.DeviceId;
            var enabled = false;
            var addressCount = 0;
            try
            {
                using var document = JsonDocument.Parse(device.EndpointJson);
                var root = document.RootElement;
                if (root.TryGetProperty("DisplayName", out var name) && !string.IsNullOrWhiteSpace(name.GetString())) displayName = name.GetString();
                if (root.TryGetProperty("DirectSyncEnabled", out var direct)) enabled = direct.GetBoolean();
                else if (root.TryGetProperty("Enabled", out var legacy)) enabled = legacy.GetBoolean();
                if (root.TryGetProperty("Addresses", out var addresses) && addresses.ValueKind == JsonValueKind.Array) addressCount = addresses.GetArrayLength();
            }
            catch (JsonException) { }
            return new SyncDeviceState
            {
                DeviceId = device.DeviceId, DisplayName = displayName, DirectSyncEnabled = enabled,
                AddressCount = addressCount, LastResult = string.IsNullOrWhiteSpace(device.LastResult) ? "尚未测试" : device.LastResult,
                UpdatedAtUtc = device.UpdatedAtUtc
            };
        }

        private void AddActivity(string description)
        {
            _store.AddActivity(description);
        }

        private static string EntityType(SyncSnapshotKind kind) => kind switch
        {
            SyncSnapshotKind.Orders => "Orders",
            SyncSnapshotKind.TemplateSettings => "TemplateSettings",
            SyncSnapshotKind.PrintRecord => "PrintRecord",
            SyncSnapshotKind.PrintJobEvent => "PrintJobEvent",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        private static string SafeError(Exception exception) => exception switch
        {
            SyncException sync => sync.Message,
            ArgumentException argument => argument.Message,
            IOException => "同步文件读写失败，本地数据和待上传队列已保留。",
            UnauthorizedAccessException => "同步文件访问被拒绝，请检查文件权限。",
            _ => "同步操作失败，本地数据和待上传队列已保留。"
        };

        private static async Task WriteAtomicAsync(string path, byte[] content, CancellationToken cancellationToken)
        {
            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var temporaryPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporaryPath, content, cancellationToken).ConfigureAwait(false);
                File.Move(temporaryPath, fullPath, true);
            }
            finally
            {
                try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch (IOException) { }
            }
        }

        private static string Redact(string value) => string.IsNullOrWhiteSpace(value) ? "" : value.Length <= 8 ? "***" : value.Substring(0, 4) + "..." + value.Substring(value.Length - 4);
        private static string GetSafeHost(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "";
        private static int CountFiles(string directory) => Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly).Count() : 0;

        public void Dispose()
        {
            CancellationTokenSource cancellation;
            lock (_operationGate)
            {
                if (_disposed) return;
                _disposed = true;
                cancellation = _activeOperation;
            }
            try { cancellation?.Cancel(); } catch (ObjectDisposedException) { }
        }
    }
}
