using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace BarTenderPrinter.Tests
{
    public sealed class PrintJobLedgerTests
    {
        [Fact]
        public async Task CoordinatorPersistsReceivedBeforeSubmittingAndReplaysCompletedResult()
        {
            var path = Path.Combine(CreateTempDirectory(), "print-jobs.db");
            var ledger = new SqlitePrintJobLedger(path);
            var service = new CountingBarTenderService();
            var history = new CapturingHistoryRepository();
            var coordinator = new PrintJobCoordinator(service, history, new PrintWorkflow(), ledger);
            var request = CreateRequest("job-1", "key-1");

            var first = await coordinator.ExecuteAsync(request);
            var replay = await coordinator.ExecuteAsync(CreateRequest("job-1", "key-1"));

            Assert.Equal(1, service.SubmissionCount);
            Assert.Equal(PrintSubmissionState.Submitted, first.PrintResult.State);
            Assert.True(replay.IsIdempotentReplay);
            Assert.Equal(PrintSubmissionState.Submitted, replay.PrintResult.State);
            Assert.Equal(PrintJobLedgerState.Submitted, ledger.Get("key-1").State);
            Assert.Equal("job-1", history.LastEntry.JobId);
        }

        [Fact]
        public async Task CoordinatorRejectsSameKeyWithDifferentRequestHash()
        {
            var path = Path.Combine(CreateTempDirectory(), "print-jobs.db");
            var service = new CountingBarTenderService();
            var coordinator = new PrintJobCoordinator(service, new CapturingHistoryRepository(), new PrintWorkflow(), new SqlitePrintJobLedger(path));
            await coordinator.ExecuteAsync(CreateRequest("job-1", "key-1"));
            var conflicting = CreateRequest("job-1", "key-1");
            conflicting.FieldValues["IMEI"] = "999";

            var result = await coordinator.ExecuteAsync(conflicting);

            Assert.Equal(1, service.SubmissionCount);
            Assert.Equal(PrintSubmissionState.Failed, result.PrintResult.State);
            Assert.Equal("IDEMPOTENCY_CONFLICT", result.PrintResult.DiagnosticDetails);
        }

        [Fact]
        public void LedgerRecoversInterruptedSubmissionAsUncertain()
        {
            var path = Path.Combine(CreateTempDirectory(), "print-jobs.db");
            var ledger = new SqlitePrintJobLedger(path);
            var request = CreateRequest("job-1", "key-1");
            ledger.Register(request, PrintJobCoordinator.ComputeRequestHash(request));
            Assert.True(ledger.TryMarkSubmitting("key-1"));

            var recovered = new SqlitePrintJobLedger(path);

            Assert.Equal(PrintJobLedgerState.Uncertain, recovered.Get("key-1").State);
            Assert.Equal(PrintSubmissionState.Uncertain, recovered.Get("key-1").ToCompletion(true).PrintResult.State);
        }

        [Fact]
        public async Task ReceivedJobCanResumeAfterProcessRestart()
        {
            var path = Path.Combine(CreateTempDirectory(), "print-jobs.db");
            var request = CreateRequest("job-1", "key-1");
            var firstLedger = new SqlitePrintJobLedger(path);
            firstLedger.Register(request, PrintJobCoordinator.ComputeRequestHash(request));
            var service = new CountingBarTenderService();
            var coordinator = new PrintJobCoordinator(service, new CapturingHistoryRepository(), new PrintWorkflow(), new SqlitePrintJobLedger(path));

            var completion = await coordinator.ExecuteAsync(CreateRequest("job-1", "key-1"));

            Assert.Equal(PrintSubmissionState.Submitted, completion.PrintResult.State);
            Assert.Equal(1, service.SubmissionCount);
        }

        [Fact]
        public async Task ConcurrentDuplicateRequestSubmitsOnlyOnce()
        {
            var path = Path.Combine(CreateTempDirectory(), "print-jobs.db");
            var service = new BlockingBarTenderService();
            var coordinator = new PrintJobCoordinator(service, new CapturingHistoryRepository(), new PrintWorkflow(), new SqlitePrintJobLedger(path));

            var firstTask = coordinator.ExecuteAsync(CreateRequest("job-1", "key-1"));
            await service.Started;
            var duplicate = await coordinator.ExecuteAsync(CreateRequest("job-1", "key-1"));
            service.Release();
            await firstTask;

            Assert.Equal(1, service.SubmissionCount);
            Assert.True(duplicate.IsIdempotentReplay);
            Assert.Equal(PrintSubmissionState.Uncertain, duplicate.PrintResult.State);
        }

        [Fact]
        public void HistoryManagerPersistsJobAndReprintApprovalFields()
        {
            var directory = CreateTempDirectory();
            var history = new HistoryManager(Path.Combine(directory, "records.csv"), Path.Combine(directory, "records.jsonl"), Path.Combine(directory, "records.db"));
            history.Load();

            Assert.True(history.Add(new PrintHistoryEntry
            {
                JobId = "job-2",
                IdempotencyKey = "key-2",
                OriginalJobId = "job-1",
                ApprovalId = "approval-1",
                ReprintSequence = 1,
                TemplateName = "carton.btw",
                TemplatePath = "C:\\carton.btw",
                Status = "REPRINT_PASS"
            }));

            var reloaded = new HistoryManager(Path.Combine(directory, "records.csv"), Path.Combine(directory, "records.jsonl"), Path.Combine(directory, "records.db"));
            reloaded.Load();
            var record = Assert.Single(reloaded.Records);
            Assert.Equal(5, record.SchemaVersion);
            Assert.Equal("job-2", record.JobId);
            Assert.Equal("key-2", record.IdempotencyKey);
            Assert.Equal("job-1", record.OriginalJobId);
            Assert.Equal("approval-1", record.ApprovalId);
            Assert.Equal(1, record.ReprintSequence);
            var jsonl = File.ReadAllText(Path.Combine(directory, "records.jsonl"));
            Assert.DoesNotContain("\"BatchId\"", jsonl);
            Assert.DoesNotContain("\"BatchItemId\"", jsonl);
            Assert.DoesNotContain("\"LabelType\"", jsonl);
        }

        [Fact]
        public async Task ReprintRequiresOriginalJobApprovalReasonAndSequence()
        {
            var service = new CountingBarTenderService();
            var coordinator = new PrintJobCoordinator(service, new CapturingHistoryRepository(), new PrintWorkflow());
            var request = CreateRequest("job-2", "key-2");
            request.Kind = PrintJobKind.Reprint;

            var completion = await coordinator.ExecuteAsync(request);

            Assert.Equal(PrintSubmissionState.Failed, completion.PrintResult.State);
            Assert.Equal("REPRINT_APPROVAL_REQUIRED", completion.PrintResult.DiagnosticDetails);
            Assert.Equal(0, service.SubmissionCount);
        }

        private static PrintJobRequest CreateRequest(string jobId, string key) => new PrintJobRequest
        {
            JobId = jobId,
            IdempotencyKey = key,
            TemplateName = "body.btw",
            TemplatePath = "C:\\body.btw",
            TemplateId = "body-v1",
            TemplateVersion = "v1",
            FieldValues = new Dictionary<string, string> { ["IMEI"] = "123" },
            Printer = "Printer",
            Copies = 1
        };

        private static string CreateTempDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), "BarTenderPrinterTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private class CountingBarTenderService : IBarTenderService
        {
            public int SubmissionCount { get; protected set; }
            public bool IsConnected => true;
            public bool IsOfflineMode => false;
            public bool IsPreviewAvailable => false;
            public string PreviewUnavailableReason => "";
            public bool Connect() => true;
            public List<string> GetTemplateDataSources(string templatePath) => new List<string>();
            public void RunDiagnostics(string templatePath) { }
            public PrintResult Print(string templatePath, Dictionary<string, string> fieldValues, string printer, int copies) => throw new NotSupportedException();
            public virtual Task<PrintResult> PrintAsync(string templatePath, Dictionary<string, string> fieldValues, string printer, int copies)
            {
                SubmissionCount++;
                return Task.FromResult(new PrintResult(PrintSubmissionState.Submitted, ""));
            }
            public Task<string> ExportPreviewAsync(string templatePath, Dictionary<string, string> fieldValues) => Task.FromResult("");
            public string[] GetAvailableTemplates(string directory) => Array.Empty<string>();
            public string[] GetPrinters() => Array.Empty<string>();
            public void Disconnect() { }
            public void Dispose() { }
        }

        private sealed class BlockingBarTenderService : CountingBarTenderService
        {
            private readonly TaskCompletionSource<bool> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<PrintResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public Task Started => _started.Task;

            public override Task<PrintResult> PrintAsync(string templatePath, Dictionary<string, string> fieldValues, string printer, int copies)
            {
                SubmissionCount++;
                _started.TrySetResult(true);
                return _completion.Task;
            }

            public void Release() => _completion.TrySetResult(new PrintResult(PrintSubmissionState.Submitted, ""));
        }

        private sealed class CapturingHistoryRepository : IHistoryRepository
        {
            public PrintHistoryEntry LastEntry { get; private set; }
            public IReadOnlyList<PrintRecord> Records { get; } = Array.Empty<PrintRecord>();
            public void Load() { }
            public bool Add(PrintHistoryEntry entry) { LastEntry = entry; return true; }
            public bool Clear(string templateName, string templatePath, string templateId, string operatorName = "", string reason = "") => true;
            public bool Delete(string recordId, string operatorName = "", string reason = "") => true;
            public PrintRecord GetById(string recordId) => null;
            public PrintRecord GetLatestSuccessful(string templateName, string templatePath, string templateId) => null;
            public List<PrintRecord> Search(string templateName, string templatePath, string templateId, string keyword, bool exact, int limit = 0, bool newestFirst = false, int offset = 0) => new();
            public List<PrintRecord> Search(string templateName, string templatePath, string templateId, string keyword, bool exact, int limit, bool newestFirst, int offset, string status, string datePrefix, string printer, string orderQuery) => new();
            public int Count(string templateName, string templatePath, string templateId) => 0;
            public int TodayCount(string templateName, string templatePath, string templateId) => 0;
            public bool ContainsAnyValue(string templateName, string templatePath, string templateId, string value) => false;
            public void Export(string path, IEnumerable<PrintRecord> records) { }
        }
    }
}
