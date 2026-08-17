using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace BarTenderPrinter
{
    public enum SyncSnapshotKind
    {
        Orders,
        TemplateSettings,
        PrintRecord,
        PrintJobEvent
    }

    public sealed class SyncFileSnapshot
    {
        public SyncSnapshotKind Kind { get; init; }
        public string ObjectId { get; init; } = "";
        public string Sha256 { get; init; } = "";
        public byte[] Content { get; init; } = Array.Empty<byte>();
    }

    public sealed class SyncTemplateObject
    {
        public string Sha256 { get; init; } = "";
        public long Length { get; init; }
        public string SourcePath { get; init; } = "";
        public byte[] Content { get; init; } = Array.Empty<byte>();
    }

    public sealed class SyncDataSnapshot
    {
        public IReadOnlyList<SyncFileSnapshot> Files { get; init; } = Array.Empty<SyncFileSnapshot>();
        public IReadOnlyList<SyncTemplateObject> Templates { get; init; } = Array.Empty<SyncTemplateObject>();
        public DateTime CreatedAtUtc { get; init; }
    }

    public interface ISyncDataAdapter
    {
        Task<SyncDataSnapshot> CaptureAsync(CancellationToken cancellationToken);
    }

    public sealed class SyncDataAdapter : ISyncDataAdapter
    {
        private readonly string _ordersPath;
        private readonly string _settingsPath;
        private readonly string _historyDatabasePath;
        private readonly string _jobsDatabasePath;

        public SyncDataAdapter(
            string ordersPath,
            string settingsPath,
            string historyDatabasePath,
            string jobsDatabasePath,
            string stagingDirectory)
        {
            _ordersPath = RequirePath(ordersPath, nameof(ordersPath));
            _settingsPath = RequirePath(settingsPath, nameof(settingsPath));
            _historyDatabasePath = RequirePath(historyDatabasePath, nameof(historyDatabasePath));
            _jobsDatabasePath = RequirePath(jobsDatabasePath, nameof(jobsDatabasePath));
            _ = RequirePath(stagingDirectory, nameof(stagingDirectory));
        }

        public SyncDataAdapter()
            : this(
                AppPaths.OrdersFile,
                AppPaths.TemplateSettingsFile,
                AppPaths.RecordsSqliteFile,
                AppPaths.PrintJobLedgerFile,
                Path.Combine(AppPaths.DataDirectory, "sync-staging"))
        {
        }

        public async Task<SyncDataSnapshot> CaptureAsync(CancellationToken cancellationToken)
        {
            var files = new List<SyncFileSnapshot>();
            var orders = await CaptureRegularFileAsync(_ordersPath, SyncSnapshotKind.Orders, cancellationToken).ConfigureAwait(false);
            var settings = await CaptureRegularFileAsync(_settingsPath, SyncSnapshotKind.TemplateSettings, cancellationToken).ConfigureAwait(false);
            if (orders != null) files.Add(orders);
            if (settings != null) files.Add(settings);

            files.AddRange(CapturePrintRecords(cancellationToken));
            files.AddRange(CapturePrintJobEvents(cancellationToken));

            var templatePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectOrderTemplatePaths(orders?.Content, templatePaths);
            CollectSettingsTemplatePaths(settings?.Content, templatePaths);
            var templates = new Dictionary<string, SyncTemplateObject>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in templatePaths.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var template = await CaptureTemplateAsync(path, cancellationToken).ConfigureAwait(false);
                if (template != null && !templates.ContainsKey(template.Sha256)) templates.Add(template.Sha256, template);
            }

            return new SyncDataSnapshot
            {
                Files = files,
                Templates = templates.Values.ToArray(),
                CreatedAtUtc = DateTime.UtcNow
            };
        }

        private static async Task<SyncFileSnapshot> CaptureRegularFileAsync(
            string path,
            SyncSnapshotKind kind,
            CancellationToken cancellationToken)
        {
            if (!File.Exists(path)) return null;
            byte[] content = null;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var before = new FileInfo(path);
                var beforeLength = before.Length;
                var beforeWrite = before.LastWriteTimeUtc;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536, true))
                {
                    if (stream.Length > int.MaxValue) throw new IOException($"同步文件过大: {Path.GetFileName(path)}");
                    content = new byte[(int)stream.Length];
                    var offset = 0;
                    while (offset < content.Length)
                    {
                        var read = await stream.ReadAsync(content.AsMemory(offset), cancellationToken).ConfigureAwait(false);
                        if (read == 0) throw new EndOfStreamException($"读取同步文件时内容发生变化: {Path.GetFileName(path)}");
                        offset += read;
                    }
                }

                var after = new FileInfo(path);
                if (beforeLength == after.Length && beforeWrite == after.LastWriteTimeUtc) break;
                content = null;
            }

            if (content == null) throw new IOException($"无法获得一致的文件快照: {Path.GetFileName(path)}");
            return CreateFileSnapshot(kind, content);
        }

        private IReadOnlyList<SyncFileSnapshot> CapturePrintRecords(CancellationToken cancellationToken)
        {
            var snapshots = new List<SyncFileSnapshot>();
            if (!File.Exists(_historyDatabasePath)) return snapshots;
            using var connection = OpenReadOnly(_historyDatabasePath);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT RecordId, Json FROM PrintRecords ORDER BY PrintTime, RecordId";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var recordId = reader.GetString(0);
                var record = JsonSerializer.Deserialize<PrintRecord>(reader.GetString(1))
                    ?? throw new InvalidDataException($"打印历史记录为空: {recordId}");
                if (string.IsNullOrWhiteSpace(recordId) || !string.Equals(record.RecordId, recordId, StringComparison.Ordinal))
                    throw new InvalidDataException($"打印历史记录身份不一致: {recordId}");
                record.RecordChecksum = "";
                snapshots.Add(CreateFileSnapshot(SyncSnapshotKind.PrintRecord, recordId, JsonSerializer.SerializeToUtf8Bytes(record)));
            }
            return snapshots;
        }

        private IReadOnlyList<SyncFileSnapshot> CapturePrintJobEvents(CancellationToken cancellationToken)
        {
            var snapshots = new List<SyncFileSnapshot>();
            if (!File.Exists(_jobsDatabasePath)) return snapshots;
            using var connection = OpenReadOnly(_jobsDatabasePath);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT JobId, IdempotencyKey, RequestHash, State, RequestJson, CompletionJson, CreatedAtUtc, UpdatedAtUtc FROM PrintJobs ORDER BY CreatedAtUtc, JobId";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = new SyncPrintJobEvent
                {
                    JobId = reader.GetString(0), IdempotencyKey = reader.GetString(1), RequestHash = reader.GetString(2),
                    State = reader.GetString(3), RequestJson = reader.GetString(4), CompletionJson = reader.GetString(5),
                    CreatedAtUtc = reader.GetString(6), UpdatedAtUtc = reader.GetString(7)
                };
                if (string.IsNullOrWhiteSpace(item.JobId) || string.IsNullOrWhiteSpace(item.State) || string.IsNullOrWhiteSpace(item.UpdatedAtUtc))
                    throw new InvalidDataException("打印作业事件缺少稳定身份字段。");
                var identityHash = ComputeSha256(Encoding.UTF8.GetBytes(string.Join("\n", item.JobId, item.State, item.UpdatedAtUtc)));
                snapshots.Add(CreateFileSnapshot(SyncSnapshotKind.PrintJobEvent, $"{item.JobId}:{identityHash}", JsonSerializer.SerializeToUtf8Bytes(item)));
            }
            return snapshots;
        }

        private static SqliteConnection OpenReadOnly(string path)
        {
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Private
            }.ToString());
            connection.Open();
            return connection;
        }

        private static async Task<SyncTemplateObject> CaptureTemplateAsync(string path, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) ||
                !string.Equals(Path.GetExtension(path), ".btw", StringComparison.OrdinalIgnoreCase)) return null;
            var fullPath = Path.GetFullPath(path);
            var content = await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false);
            return new SyncTemplateObject
            {
                Sha256 = ComputeSha256(content),
                Length = content.LongLength,
                SourcePath = fullPath,
                Content = content
            };
        }

        private static SyncFileSnapshot CreateFileSnapshot(SyncSnapshotKind kind, byte[] content)
            => CreateFileSnapshot(kind, GetObjectId(kind), content);

        private static SyncFileSnapshot CreateFileSnapshot(SyncSnapshotKind kind, string objectId, byte[] content)
        {
            var hash = ComputeSha256(content);
            return new SyncFileSnapshot
            {
                Kind = kind,
                ObjectId = objectId,
                Sha256 = hash,
                Content = content
            };
        }

        internal static string GetObjectId(SyncSnapshotKind kind) => kind switch
        {
            SyncSnapshotKind.Orders => "orders",
            SyncSnapshotKind.TemplateSettings => "template-settings",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        internal sealed class SyncPrintJobEvent
        {
            public string JobId { get; set; } = "";
            public string IdempotencyKey { get; set; } = "";
            public string RequestHash { get; set; } = "";
            public string State { get; set; } = "";
            public string RequestJson { get; set; } = "";
            public string CompletionJson { get; set; } = "";
            public string CreatedAtUtc { get; set; } = "";
            public string UpdatedAtUtc { get; set; } = "";
        }

        private static void CollectOrderTemplatePaths(byte[] content, ISet<string> paths)
        {
            if (content == null || content.Length == 0) return;
            try
            {
                var orders = JsonSerializer.Deserialize<List<PackagingOrder>>(content) ?? new List<PackagingOrder>();
                foreach (var order in orders.Where(item => item != null))
                {
                    AddPath(paths, order.TemplatePath);
                    AddPath(paths, order.Settings?.TemplatePath);
                    foreach (var template in order.Templates ?? new List<OrderTemplate>())
                    {
                        AddPath(paths, template?.SourcePath);
                        AddPath(paths, template?.ArchivedPath);
                        AddPath(paths, template?.Settings?.TemplatePath);
                    }
                }
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("订单快照不是有效 JSON。", ex);
            }
        }

        private static void CollectSettingsTemplatePaths(byte[] content, ISet<string> paths)
        {
            if (content == null || content.Length == 0) return;
            try
            {
                var settings = JsonSerializer.Deserialize<List<TemplateSettings>>(content) ?? new List<TemplateSettings>();
                foreach (var item in settings) AddPath(paths, item?.TemplatePath);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("模板设置快照不是有效 JSON。", ex);
            }
        }

        private static void AddPath(ISet<string> paths, string path)
        {
            if (!string.IsNullOrWhiteSpace(path)) paths.Add(path);
        }

        internal static string ComputeSha256(byte[] content)
        {
            using var sha256 = SHA256.Create();
            return Convert.ToHexString(sha256.ComputeHash(content));
        }

        private static string RequirePath(string path, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("路径不能为空。", parameterName);
            return path;
        }
    }
}
