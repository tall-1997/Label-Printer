using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BarTenderPrinter
{
    public sealed class PrintJobRequest
    {
        public string JobId { get; set; } = "";
        public string IdempotencyKey { get; set; } = "";
        public string BatchId { get; set; } = "";
        public string BatchItemId { get; set; } = "";
        public LabelType LabelType { get; set; }
        public string OriginalJobId { get; set; } = "";
        public string ApprovalId { get; set; } = "";
        public int ReprintSequence { get; set; }
        public PrintJobKind Kind { get; set; }
        public string TemplateName { get; set; } = "";
        public string TemplatePath { get; set; } = "";
        public string TemplateId { get; set; } = "";
        public Dictionary<string, string> FieldValues { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public string Printer { get; set; } = "";
        public int Copies { get; set; } = 1;
        public string OperatorName { get; set; } = "";
        public string ReprintReason { get; set; } = "";
        public string TemplateVersion { get; set; } = "";
        public string OrderName { get; set; } = "";
        public string OrderId { get; set; } = "";
        public List<string> TemplateFields { get; set; } = new List<string>();
    }

    public sealed class PrintJobCompletion
    {
        public PrintResult PrintResult { get; set; }
        public bool HistorySaved { get; set; }
        public string HistoryStatus { get; set; } = "";
        public string CompletionStatus { get; set; } = "";
        public string HistoryError { get; set; } = "";
        public string JobId { get; set; } = "";
        public string IdempotencyKey { get; set; } = "";
        public bool IsIdempotentReplay { get; set; }
        public string LedgerState { get; set; } = "";
    }

    public sealed class PrintJobCoordinator
    {
        private readonly IBarTenderService _barTender;
        private readonly IHistoryRepository _history;
        private readonly PrintWorkflow _workflow;
        private readonly IPrintJobLedger _ledger;

        public PrintJobCoordinator(IBarTenderService barTender, IHistoryRepository history, PrintWorkflow workflow)
            : this(barTender, history, workflow, null)
        {
        }

        public PrintJobCoordinator(IBarTenderService barTender, IHistoryRepository history, PrintWorkflow workflow, IPrintJobLedger ledger)
        {
            _barTender = barTender ?? throw new ArgumentNullException(nameof(barTender));
            _history = history ?? throw new ArgumentNullException(nameof(history));
            _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
            _ledger = ledger;
        }

        public async Task<PrintJobCompletion> ExecuteAsync(PrintJobRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var snapshot = new PrintJobRequest
            {
                JobId = string.IsNullOrWhiteSpace(request.JobId)
                    ? (string.IsNullOrWhiteSpace(request.IdempotencyKey) ? Guid.NewGuid().ToString("N") : request.IdempotencyKey.Trim())
                    : request.JobId.Trim(),
                IdempotencyKey = request.IdempotencyKey?.Trim() ?? "",
                BatchId = request.BatchId?.Trim() ?? "",
                BatchItemId = request.BatchItemId?.Trim() ?? "",
                LabelType = request.LabelType,
                OriginalJobId = request.OriginalJobId?.Trim() ?? "",
                ApprovalId = request.ApprovalId?.Trim() ?? "",
                ReprintSequence = Math.Max(0, request.ReprintSequence),
                Kind = request.Kind,
                TemplateName = request.TemplateName ?? "",
                TemplatePath = request.TemplatePath ?? "",
                TemplateId = request.TemplateId ?? "",
                FieldValues = new Dictionary<string, string>(request.FieldValues ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase),
                Printer = request.Printer ?? "",
                Copies = Math.Max(1, request.Copies),
                OperatorName = request.OperatorName ?? "",
                ReprintReason = request.ReprintReason ?? "",
                TemplateVersion = request.TemplateVersion ?? "",
                OrderName = request.OrderName ?? "",
                OrderId = request.OrderId ?? "",
                TemplateFields = new List<string>(request.TemplateFields ?? new List<string>())
            };
            if (string.IsNullOrWhiteSpace(snapshot.IdempotencyKey)) snapshot.IdempotencyKey = snapshot.JobId;
            if (snapshot.Kind == PrintJobKind.Reprint &&
                (string.IsNullOrWhiteSpace(snapshot.OriginalJobId) || string.IsNullOrWhiteSpace(snapshot.ApprovalId) ||
                 string.IsNullOrWhiteSpace(snapshot.ReprintReason) || snapshot.ReprintSequence < 1))
            {
                return CreateLedgerCompletion(snapshot, PrintSubmissionState.Failed,
                    "补打印作业缺少原作业、审批、原因或补打序号", "REPRINT_APPROVAL_REQUIRED", false, PrintJobLedgerState.Failed);
            }

            if (_ledger != null)
            {
                var hash = ComputeRequestHash(snapshot);
                var registration = _ledger.Register(snapshot, hash);
                if (registration.Outcome == PrintJobRegistrationOutcome.Conflict)
                    return CreateLedgerCompletion(snapshot, PrintSubmissionState.Failed, "幂等键对应的打印请求不一致", "IDEMPOTENCY_CONFLICT", true, registration.Entry?.State ?? PrintJobLedgerState.Failed);
                if (registration.Outcome == PrintJobRegistrationOutcome.Existing)
                    return registration.Entry.ToCompletion(true);
                if (!_ledger.TryMarkSubmitting(snapshot.IdempotencyKey))
                    return _ledger.Get(snapshot.IdempotencyKey)?.ToCompletion(true)
                        ?? CreateLedgerCompletion(snapshot, PrintSubmissionState.Uncertain, "打印作业状态待核查", "LEDGER_STATE_UNCERTAIN", true, PrintJobLedgerState.Uncertain);
            }

            PrintResult result;
            try
            {
                result = await _barTender.PrintAsync(snapshot.TemplatePath, snapshot.FieldValues, snapshot.Printer, snapshot.Copies).ConfigureAwait(false);
                result ??= new PrintResult(PrintSubmissionState.Uncertain, "打印服务未返回结果", "submission=uncertain;result=null");
            }
            catch (Exception ex)
            {
                result = new PrintResult(PrintSubmissionState.Uncertain, ex.Message,
                    $"type={ex.GetType().Name};template={snapshot.TemplatePath};printer={snapshot.Printer};copies={snapshot.Copies};message={ex.Message}");
            }

            var historyStatus = _workflow.GetHistoryStatus(result, snapshot.Kind);
            var historySaved = false;
            var historyError = "";
            try
            {
                historySaved = _workflow.RecordPrintResult(_history, new PrintHistoryEntry
                {
                    JobId = snapshot.JobId,
                    IdempotencyKey = snapshot.IdempotencyKey,
                    BatchId = snapshot.BatchId,
                    BatchItemId = snapshot.BatchItemId,
                    LabelType = snapshot.LabelType,
                    OriginalJobId = snapshot.OriginalJobId,
                    ApprovalId = snapshot.ApprovalId,
                    ReprintSequence = snapshot.ReprintSequence,
                    TemplateName = snapshot.TemplateName,
                    TemplatePath = snapshot.TemplatePath,
                    TemplateId = snapshot.TemplateId,
                    FieldValues = snapshot.FieldValues,
                    Status = historyStatus,
                    Printer = snapshot.Printer,
                    Copies = snapshot.Copies,
                    OperatorName = snapshot.OperatorName,
                    ReprintReason = snapshot.ReprintReason,
                    TemplateVersion = snapshot.TemplateVersion,
                    DiagnosticDetails = result.DiagnosticDetails,
                    OrderName = snapshot.OrderName,
                    OrderId = snapshot.OrderId,
                    TemplateFields = snapshot.TemplateFields
                });
            }
            catch (Exception ex)
            {
                historyError = ex.Message;
            }

            var completion = new PrintJobCompletion
            {
                PrintResult = result,
                HistorySaved = historySaved,
                HistoryStatus = historyStatus,
                CompletionStatus = _workflow.GetCompletionStatus(result, historySaved, snapshot.Kind),
                HistoryError = historyError,
                JobId = snapshot.JobId,
                IdempotencyKey = snapshot.IdempotencyKey,
                LedgerState = result.State.ToString()
            };
            if (_ledger != null)
            {
                try
                {
                    _ledger.Complete(snapshot.IdempotencyKey, completion);
                }
                catch (Exception ex)
                {
                    completion.PrintResult = new PrintResult(PrintSubmissionState.Uncertain,
                        "打印已进入外部提交阶段，账本结果保存失败", $"LEDGER_COMPLETION_FAILED;type={ex.GetType().Name}");
                    completion.CompletionStatus = "打印结果待核查";
                    completion.LedgerState = PrintJobLedgerState.Uncertain.ToString();
                }
            }
            return completion;
        }

        internal static string ComputeRequestHash(PrintJobRequest request)
        {
            var canonical = new
            {
                request.JobId,
                request.IdempotencyKey,
                request.BatchId,
                request.BatchItemId,
                LabelType = request.LabelType.ToString(),
                request.OriginalJobId,
                request.ApprovalId,
                request.ReprintSequence,
                Kind = request.Kind.ToString(),
                request.TemplateName,
                request.TemplatePath,
                request.TemplateId,
                FieldValues = (request.FieldValues ?? new Dictionary<string, string>())
                    .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(item => new[] { item.Key, item.Value ?? "" }),
                request.Printer,
                request.Copies,
                request.OperatorName,
                request.ReprintReason,
                request.TemplateVersion,
                request.OrderName,
                request.OrderId,
                TemplateFields = (request.TemplateFields ?? new List<string>()).OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            };
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical)));
            return Convert.ToHexString(bytes);
        }

        private static PrintJobCompletion CreateLedgerCompletion(PrintJobRequest request, PrintSubmissionState state,
            string message, string diagnostics, bool replay, PrintJobLedgerState ledgerState)
        {
            return new PrintJobCompletion
            {
                PrintResult = new PrintResult(state, message, diagnostics),
                CompletionStatus = message,
                JobId = request.JobId,
                IdempotencyKey = request.IdempotencyKey,
                IsIdempotentReplay = replay,
                LedgerState = ledgerState.ToString()
            };
        }
    }
}
