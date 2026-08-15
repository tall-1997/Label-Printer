namespace BarTenderPrinter
{
    public sealed class PrintHistoryEntry
    {
        public string TemplateName { get; set; } = "";
        public string TemplatePath { get; set; } = "";
        public string TemplateId { get; set; } = "";
        public System.Collections.Generic.IReadOnlyDictionary<string, string> FieldValues { get; set; } = new System.Collections.Generic.Dictionary<string, string>();
        public string Status { get; set; } = "UNCERTAIN";
        public string Printer { get; set; } = "";
        public int Copies { get; set; } = 1;
        public string OperatorName { get; set; } = "";
        public string ReprintReason { get; set; } = "";
        public string TemplateVersion { get; set; } = "";
        public string DiagnosticDetails { get; set; } = "";
        public string OrderName { get; set; } = "";
        public string OrderId { get; set; } = "";
        public System.Collections.Generic.IReadOnlyList<string> TemplateFields { get; set; } = System.Array.Empty<string>();
    }

    public enum PrintJobKind
    {
        Print,
        Reprint
    }

    public enum PrintSubmissionState
    {
        Submitted,
        Failed,
        Uncertain
    }

    public class PrintResult
    {
        public PrintSubmissionState State { get; }
        public bool Success => State == PrintSubmissionState.Submitted;
        public string ErrorMessage { get; }
        public string DiagnosticDetails { get; }

        public PrintResult(bool success, string message, string diagnostics = "")
            : this(success ? PrintSubmissionState.Submitted : PrintSubmissionState.Failed, message, diagnostics)
        {
        }

        public PrintResult(PrintSubmissionState state, string message, string diagnostics = "")
        {
            State = state;
            ErrorMessage = message ?? "";
            DiagnosticDetails = diagnostics ?? "";
        }
    }
}
