using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BarTenderPrinter
{
    public sealed class PrintJobRequest
    {
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
    }

    public sealed class PrintJobCoordinator
    {
        private readonly IBarTenderService _barTender;
        private readonly IHistoryRepository _history;
        private readonly PrintWorkflow _workflow;

        public PrintJobCoordinator(IBarTenderService barTender, IHistoryRepository history, PrintWorkflow workflow)
        {
            _barTender = barTender ?? throw new ArgumentNullException(nameof(barTender));
            _history = history ?? throw new ArgumentNullException(nameof(history));
            _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        }

        public async Task<PrintJobCompletion> ExecuteAsync(PrintJobRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var snapshot = new PrintJobRequest
            {
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
            PrintResult result;
            try
            {
                result = await _barTender.PrintAsync(snapshot.TemplatePath, snapshot.FieldValues, snapshot.Printer, snapshot.Copies).ConfigureAwait(false);
                result ??= new PrintResult(PrintSubmissionState.Uncertain, "打印服务未返回结果", "submission=uncertain;result=null");
            }
            catch (Exception ex)
            {
                result = new PrintResult(PrintSubmissionState.Failed, ex.Message,
                    $"type={ex.GetType().Name};template={snapshot.TemplatePath};printer={snapshot.Printer};copies={snapshot.Copies};message={ex.Message}");
            }

            var historyStatus = _workflow.GetHistoryStatus(result, snapshot.Kind);
            var historySaved = false;
            var historyError = "";
            try
            {
                historySaved = _workflow.RecordPrintResult(_history, new PrintHistoryEntry
                {
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

            return new PrintJobCompletion
            {
                PrintResult = result,
                HistorySaved = historySaved,
                HistoryStatus = historyStatus,
                CompletionStatus = _workflow.GetCompletionStatus(result, historySaved, snapshot.Kind),
                HistoryError = historyError
            };
        }
    }
}
