using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BarTenderPrinter;
using Microsoft.Data.Sqlite;
using Xunit;

namespace BarTenderPrinter.Tests
{
    public sealed class EndpointSyncDataAdapterTests
    {
        [Fact]
        public async Task CaptureIncludesJsonAndStableAppendOnlyPrintEntities()
        {
            var directory = CreateTempDirectory();
            var template = Path.Combine(directory, "label.btw");
            await File.WriteAllBytesAsync(template, new byte[] { 1, 2, 3, 4 });
            var orders = Path.Combine(directory, "orders.json");
            var settings = Path.Combine(directory, "template_settings.json");
            await File.WriteAllTextAsync(orders, JsonSerializer.Serialize(new[]
            {
                new PackagingOrder
                {
                    Id = "order-1",
                    Templates = new List<OrderTemplate> { new OrderTemplate { Id = "template-1", SourcePath = template } }
                }
            }));
            await File.WriteAllTextAsync(settings, JsonSerializer.Serialize(new[]
            {
                new TemplateSettings { TemplateName = "label.btw", TemplatePath = template }
            }));
            var history = Path.Combine(directory, "history.db");
            var jobs = Path.Combine(directory, "jobs.db");
            var record = CreateReprintRecord("record-1", "job-2");
            CreateHistoryDatabase(history, record);
            CreateJobsDatabase(jobs, "job-2", "Submitted", "2026-08-17T10:01:00.0000000Z");

            var snapshot = await new SyncDataAdapter(orders, settings, history, jobs, Path.Combine(directory, "staging"))
                .CaptureAsync(CancellationToken.None);

            Assert.Equal(4, snapshot.Files.Count);
            Assert.Contains(snapshot.Files, item => item.Kind == SyncSnapshotKind.Orders && item.ObjectId == "orders");
            Assert.Contains(snapshot.Files, item => item.Kind == SyncSnapshotKind.TemplateSettings && item.ObjectId == "template-settings");
            var historySnapshot = Assert.Single(snapshot.Files, item => item.Kind == SyncSnapshotKind.PrintRecord);
            var jobSnapshot = Assert.Single(snapshot.Files, item => item.Kind == SyncSnapshotKind.PrintJobEvent);
            Assert.Equal("record-1", historySnapshot.ObjectId);
            Assert.StartsWith("job-2:", jobSnapshot.ObjectId);
            Assert.Single(snapshot.Templates);
            Assert.Equal(4, snapshot.Templates[0].Length);
            var capturedRecord = JsonSerializer.Deserialize<PrintRecord>(historySnapshot.Content);
            Assert.Equal("original-job", capturedRecord.OriginalJobId);
            Assert.Equal("approval-7", capturedRecord.ApprovalId);
            Assert.Equal("批准补打", capturedRecord.ReprintReason);
            Assert.Equal(2, capturedRecord.ReprintSequence);
            Assert.Equal("", capturedRecord.RecordChecksum);
        }

        [Fact]
        public async Task AppendOnlyApplierMergesHistoryIdempotentlyAndConflictsOnChangedContent()
        {
            var directory = CreateTempDirectory();
            var history = Path.Combine(directory, "history.db");
            var store = new SyncStore(Path.Combine(directory, "sync.db"));
            var applier = CreateApplier(directory, history);
            var record = CreateReprintRecord("record-shared", "job-reprint");

            var first = await applier.ApplyAtomicallyAsync(CreateRecordEvent("device-a", 1, record), store, CancellationToken.None);
            var duplicate = await applier.ApplyAtomicallyAsync(CreateRecordEvent("device-b", 1, record), store, CancellationToken.None);
            record.ReprintReason = "不同内容";
            var changed = await applier.ApplyAtomicallyAsync(CreateRecordEvent("device-c", 1, record), store, CancellationToken.None);

            Assert.Equal(SyncEventApplyResult.Applied, first.Result);
            Assert.Equal(SyncEventApplyResult.AlreadyApplied, duplicate.Result);
            Assert.Equal(SyncEventApplyResult.Conflict, changed.Result);
            using var connection = new SqliteConnection($"Data Source={history}");
            connection.Open();
            Assert.Equal(1L, ScalarInt64(connection, "SELECT COUNT(*) FROM PrintRecords"));
            Assert.Equal(1L, ScalarInt64(connection, "SELECT COUNT(*) FROM FieldValues"));
            Assert.Equal(1L, ScalarInt64(connection, "SELECT COUNT(*) FROM TemplateSnapshots"));
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Json FROM PrintRecords WHERE RecordId='record-shared'";
            var merged = JsonSerializer.Deserialize<PrintRecord>((string)command.ExecuteScalar());
            Assert.Equal("original-job", merged.OriginalJobId);
            Assert.Equal("approval-7", merged.ApprovalId);
            Assert.Equal("批准补打", merged.ReprintReason);
            Assert.Equal(2, merged.ReprintSequence);
        }

        [Fact]
        public async Task AppendOnlyApplierStoresStatusEventsWithoutChangingLocalExecutionLedger()
        {
            var directory = CreateTempDirectory();
            var history = Path.Combine(directory, "history.db");
            var localJobs = Path.Combine(directory, "jobs.db");
            CreateJobsDatabase(localJobs, "job-shared", "Submitted", "2026-08-17T10:05:00.0000000Z");
            var store = new SyncStore(Path.Combine(directory, "sync.db"));
            var applier = CreateApplier(directory, history);

            var received = CreateJobEvent("job-shared", "Received", "2026-08-17T09:59:00.0000000Z");
            var uncertain = CreateJobEvent("job-shared", "Uncertain", "2026-08-17T10:00:00.0000000Z");
            Assert.Equal(SyncEventApplyResult.Applied,
                (await applier.ApplyAtomicallyAsync(CreateJobSyncEvent("device-a", 1, received), store, CancellationToken.None)).Result);
            Assert.Equal(SyncEventApplyResult.Applied,
                (await applier.ApplyAtomicallyAsync(CreateJobSyncEvent("device-a", 2, uncertain), store, CancellationToken.None)).Result);
            Assert.Equal(SyncEventApplyResult.AlreadyApplied,
                (await applier.ApplyAtomicallyAsync(CreateJobSyncEvent("device-b", 1, uncertain), store, CancellationToken.None)).Result);

            using (var historyConnection = new SqliteConnection($"Data Source={history}"))
            {
                historyConnection.Open();
                Assert.Equal(2L, ScalarInt64(historyConnection, "SELECT COUNT(*) FROM RemotePrintJobEvents WHERE JobId='job-shared'"));
            }
            using var jobsConnection = new SqliteConnection($"Data Source={localJobs}");
            jobsConnection.Open();
            using var state = jobsConnection.CreateCommand();
            state.CommandText = "SELECT State FROM PrintJobs WHERE JobId='job-shared'";
            Assert.Equal("Submitted", state.ExecuteScalar());
        }

        private static FileSnapshotSyncEventApplier CreateApplier(string directory, string history) =>
            new FileSnapshotSyncEventApplier(Path.Combine(directory, "orders.json"), Path.Combine(directory, "settings.json"),
                Path.Combine(directory, "incoming"), Path.Combine(directory, "templates"), history);

        private static SyncEvent CreateRecordEvent(string deviceId, long sequence, PrintRecord record)
        {
            record.RecordChecksum = "";
            var content = JsonSerializer.SerializeToUtf8Bytes(record);
            return CreateEvent(deviceId, sequence, "PrintRecord", record.RecordId, SyncSnapshotKind.PrintRecord, content);
        }

        private static SyncDataAdapter.SyncPrintJobEvent CreateJobEvent(string jobId, string state, string updatedAt) => new()
        {
            JobId = jobId, IdempotencyKey = "key-" + jobId, RequestHash = "request-hash", State = state,
            RequestJson = JsonSerializer.Serialize(new PrintJobRequest { JobId = jobId, OriginalJobId = "original-job", ApprovalId = "approval-7", ReprintReason = "批准补打", ReprintSequence = 2 }),
            CompletionJson = "{}", CreatedAtUtc = "2026-08-17T09:59:00.0000000Z", UpdatedAtUtc = updatedAt
        };

        private static SyncEvent CreateJobSyncEvent(string deviceId, long sequence, SyncDataAdapter.SyncPrintJobEvent item)
        {
            var identityHash = SyncDataAdapter.ComputeSha256(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", item.JobId, item.State, item.UpdatedAtUtc)));
            return CreateEvent(deviceId, sequence, "PrintJobEvent", $"{item.JobId}:{identityHash}", SyncSnapshotKind.PrintJobEvent,
                JsonSerializer.SerializeToUtf8Bytes(item));
        }

        private static SyncEvent CreateEvent(string deviceId, long sequence, string entityType, string entityId, SyncSnapshotKind kind, byte[] content)
        {
            var hash = SyncCrypto.ComputeSha256Hex(content);
            return new SyncEvent
            {
                EventId = $"{deviceId}:{sequence}", DeviceId = deviceId, Sequence = sequence, EntityType = entityType,
                EntityId = entityId, BaseVersion = 0, NewVersion = 1, OccurredAtUtc = DateTime.UtcNow,
                PayloadJson = JsonSerializer.Serialize(new SyncSnapshotPayload { Kind = kind.ToString(), Sha256 = hash, ContentBase64 = Convert.ToBase64String(content) })
            };
        }

        private static PrintRecord CreateReprintRecord(string recordId, string jobId) => new()
        {
            RecordId = recordId, JobId = jobId, IdempotencyKey = "key-" + jobId, OriginalJobId = "original-job",
            ApprovalId = "approval-7", ReprintReason = "批准补打", ReprintSequence = 2, TemplateId = "template-1",
            TemplateName = "label.btw", TemplatePath = "label.btw", PrintTime = "2026-08-17 10:00:00", Status = "SUCCESS",
            Printer = "Printer", Copies = 1, FieldValues = new Dictionary<string, string> { ["SN"] = "001" },
            TemplateFields = new List<string> { "SN" }
        };

        private static void CreateHistoryDatabase(string path, PrintRecord record)
        {
            using var connection = new SqliteConnection($"Data Source={path}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE PrintRecords (RecordId TEXT PRIMARY KEY, PrintTime TEXT, Json TEXT NOT NULL); INSERT INTO PrintRecords (RecordId, PrintTime, Json) VALUES ($id,$time,$json)";
            command.Parameters.AddWithValue("$id", record.RecordId);
            command.Parameters.AddWithValue("$time", record.PrintTime);
            command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(record));
            command.ExecuteNonQuery();
        }

        private static void CreateJobsDatabase(string path, string jobId, string state, string updatedAt)
        {
            using var connection = new SqliteConnection($"Data Source={path}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE PrintJobs (IdempotencyKey TEXT PRIMARY KEY, JobId TEXT NOT NULL, RequestHash TEXT NOT NULL, State TEXT NOT NULL, RequestJson TEXT NOT NULL, CompletionJson TEXT NOT NULL, CreatedAtUtc TEXT NOT NULL, UpdatedAtUtc TEXT NOT NULL); INSERT INTO PrintJobs VALUES ($key,$job,$hash,$state,$request,$completion,$created,$updated)";
            command.Parameters.AddWithValue("$key", "key-" + jobId);
            command.Parameters.AddWithValue("$job", jobId);
            command.Parameters.AddWithValue("$hash", "request-hash");
            command.Parameters.AddWithValue("$state", state);
            command.Parameters.AddWithValue("$request", "{}");
            command.Parameters.AddWithValue("$completion", "{}");
            command.Parameters.AddWithValue("$created", "2026-08-17T09:59:00.0000000Z");
            command.Parameters.AddWithValue("$updated", updatedAt);
            command.ExecuteNonQuery();
        }

        private static long ScalarInt64(SqliteConnection connection, string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return (long)command.ExecuteScalar();
        }

        private static string CreateTempDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), "BarTenderPrinterTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
