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
    }
}
