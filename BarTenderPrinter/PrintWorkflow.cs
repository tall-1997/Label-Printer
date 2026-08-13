namespace BarTenderPrinter
{
    public class PrintWorkflow
    {
        public string BuildTemplateVersion(OrderTemplate template)
        {
            if (template == null) return "";
            var hash = string.IsNullOrWhiteSpace(template.SourceSha256) ? "nohash" : template.SourceSha256.Substring(0, System.Math.Min(12, template.SourceSha256.Length));
            return $"ticks={template.SourceLastWriteTimeUtcTicks};len={template.SourceLength};sha={hash}";
        }

        public bool RecordPrintResult(IHistoryRepository history, string templateName, string templatePath, string templateId,
            System.Collections.Generic.Dictionary<string, string> fieldValues, string status, string printer, int copies,
            string operatorName, string reprintReason, string templateVersion, string diagnosticDetails,
            string orderName, string orderId, System.Collections.Generic.List<string> templateFields)
        {
            return history.Add(templateName, templatePath, templateId, fieldValues, status, printer, copies,
                operatorName, reprintReason, templateVersion, diagnosticDetails, orderName, orderId, templateFields);
        }
    }
}
