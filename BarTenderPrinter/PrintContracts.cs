namespace BarTenderPrinter
{
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
        public bool Success { get; }
        public string ErrorMessage { get; }
        public string DiagnosticDetails { get; }

        public PrintResult(bool success, string message, string diagnostics = "")
        {
            Success = success;
            ErrorMessage = message ?? "";
            DiagnosticDetails = diagnostics ?? "";
        }
    }
}
