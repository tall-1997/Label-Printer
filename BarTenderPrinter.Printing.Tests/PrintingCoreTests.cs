using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace BarTenderPrinter.Printing.Tests
{
    public sealed class PrintingCoreTests
    {
        [Fact]
        public async Task CompletedRequestIsReplayedWithoutSecondSubmission()
        {
            var ledger = new SqlitePrintJobLedger(NewDatabasePath());
            var service = new CountingService();
            var coordinator = Coordinator(service, ledger);

            var first = await coordinator.ExecuteAsync(Request());
            var replay = await coordinator.ExecuteAsync(Request());

            Assert.Equal(PrintSubmissionState.Submitted, first.PrintResult.State);
            Assert.Equal(PrintSubmissionState.Submitted, replay.PrintResult.State);
            Assert.True(replay.IsIdempotentReplay);
            Assert.Equal(1, service.Count);
        }

        [Fact]
        public async Task DifferentPayloadWithSameKeyIsRejected()
        {
            var service = new CountingService();
            var coordinator = Coordinator(service, new SqlitePrintJobLedger(NewDatabasePath()));
            await coordinator.ExecuteAsync(Request());
            var changed = Request();
            changed.FieldValues["IMEI"] = "999";

            var completion = await coordinator.ExecuteAsync(changed);

            Assert.Equal("IDEMPOTENCY_CONFLICT", completion.PrintResult.DiagnosticDetails);
            Assert.Equal(1, service.Count);
        }

        [Fact]
        public void InterruptedSubmissionRecoversAsUncertain()
        {
            var path = NewDatabasePath();
            var ledger = new SqlitePrintJobLedger(path);
            var request = Request();
            ledger.Register(request, PrintJobCoordinator.ComputeRequestHash(request));
            Assert.True(ledger.TryMarkSubmitting(request.IdempotencyKey));

            var recovered = new SqlitePrintJobLedger(path).Get(request.IdempotencyKey);

            Assert.Equal(PrintJobLedgerState.Uncertain, recovered.State);
            Assert.Equal(PrintSubmissionState.Uncertain, recovered.ToCompletion(true).PrintResult.State);
        }

        [Fact]
        public async Task ReceivedRequestResumesAfterRestart()
        {
            var path = NewDatabasePath();
            var request = Request();
            new SqlitePrintJobLedger(path).Register(request, PrintJobCoordinator.ComputeRequestHash(request));
            var service = new CountingService();

            var completion = await Coordinator(service, new SqlitePrintJobLedger(path)).ExecuteAsync(Request());

            Assert.Equal(PrintSubmissionState.Submitted, completion.PrintResult.State);
            Assert.Equal(1, service.Count);
        }

        [Fact]
        public async Task ConcurrentDuplicateSubmitsOnce()
        {
            var service = new BlockingService();
            var coordinator = Coordinator(service, new SqlitePrintJobLedger(NewDatabasePath()));
            var first = coordinator.ExecuteAsync(Request());
            await service.Started;

            var duplicate = await coordinator.ExecuteAsync(Request());
            service.Release();
            await first;

            Assert.Equal(1, service.Count);
            Assert.True(duplicate.IsIdempotentReplay);
            Assert.Equal(PrintSubmissionState.Uncertain, duplicate.PrintResult.State);
        }

        [Fact]
        public void RegistryResolvesAllFourLabelTypesByLatestEffectiveVersion()
        {
            var now = new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc);
            foreach (var type in new[] { LabelType.Body, LabelType.ColorBox, LabelType.Carton, LabelType.Pallet })
            {
                var registry = new LabelTemplateRegistry(new[] { Registration(type, "v1", now.AddDays(-2)), Registration(type, "v2", now.AddDays(-1)) });
                Assert.Equal("v2", registry.Resolve("Customer", "Model", type, now).Version);
            }
        }

        [Fact]
        public async Task ReprintRequiresApprovalSnapshot()
        {
            var service = new CountingService();
            var request = Request();
            request.Kind = PrintJobKind.Reprint;

            var completion = await Coordinator(service, null).ExecuteAsync(request);

            Assert.Equal("REPRINT_APPROVAL_REQUIRED", completion.PrintResult.DiagnosticDetails);
            Assert.Equal(0, service.Count);
        }

        private static PrintJobCoordinator Coordinator(IBarTenderService service, IPrintJobLedger ledger) =>
            new(service, new HistoryRepository(), new PrintWorkflow(), ledger);

        private static PrintJobRequest Request() => new()
        {
            JobId = "job-1",
            IdempotencyKey = "key-1",
            LabelType = LabelType.Body,
            TemplateName = "body.btw",
            TemplatePath = "C:\\body.btw",
            TemplateId = "body-v1",
            TemplateVersion = "v1",
            FieldValues = new Dictionary<string, string> { ["IMEI"] = "123" },
            Printer = "Printer"
        };

        private static LabelTemplateRegistration Registration(LabelType type, string version, DateTime from) => new()
        {
            Customer = "Customer",
            ProductModel = "Model",
            LabelType = type,
            TemplateId = $"{type}-{version}",
            TemplatePath = $"C:\\{type}-{version}.btw",
            Version = version,
            EffectiveFromUtc = from
        };

        private static string NewDatabasePath()
        {
            var directory = Path.Combine(Path.GetTempPath(), "BarTenderPrintingTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, "print-jobs.db");
        }

        private class CountingService : IBarTenderService
        {
            public int Count { get; protected set; }
            public bool IsConnected => true;
            public bool IsOfflineMode => false;
            public bool IsPreviewAvailable => false;
            public string PreviewUnavailableReason => "";
            public bool Connect() => true;
            public List<string> GetTemplateDataSources(string templatePath) => new();
            public void RunDiagnostics(string templatePath) { }
            public PrintResult Print(string templatePath, Dictionary<string, string> fieldValues, string printer, int copies) => throw new NotSupportedException();
            public virtual Task<PrintResult> PrintAsync(string templatePath, Dictionary<string, string> fieldValues, string printer, int copies)
            {
                Count++;
                return Task.FromResult(new PrintResult(PrintSubmissionState.Submitted, ""));
            }
            public Task<string> ExportPreviewAsync(string templatePath, Dictionary<string, string> fieldValues) => Task.FromResult("");
            public string[] GetAvailableTemplates(string directory) => Array.Empty<string>();
            public string[] GetPrinters() => Array.Empty<string>();
            public void Disconnect() { }
            public void Dispose() { }
        }

        private sealed class BlockingService : CountingService
        {
            private readonly TaskCompletionSource<bool> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<PrintResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public Task Started => _started.Task;
            public override Task<PrintResult> PrintAsync(string templatePath, Dictionary<string, string> fieldValues, string printer, int copies)
            {
                Count++;
                _started.TrySetResult(true);
                return _completion.Task;
            }
            public void Release() => _completion.TrySetResult(new PrintResult(PrintSubmissionState.Submitted, ""));
        }

        private sealed class HistoryRepository : IHistoryRepository
        {
            public IReadOnlyList<PrintRecord> Records { get; } = Array.Empty<PrintRecord>();
            public void Load() { }
            public bool Add(PrintHistoryEntry entry) => true;
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
