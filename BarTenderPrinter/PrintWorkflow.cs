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

        public bool RecordPrintResult(IHistoryRepository history, PrintHistoryEntry entry)
        {
            return history.Add(entry);
        }

        public PrintSubmissionState Classify(PrintResult result)
        {
            return result?.State ?? PrintSubmissionState.Failed;
        }

        public string GetHistoryStatus(PrintResult result, PrintJobKind kind)
        {
            var state = Classify(result);
            if (kind == PrintJobKind.Reprint)
            {
                if (state == PrintSubmissionState.Submitted) return "REPRINT_PASS";
                if (state == PrintSubmissionState.Uncertain) return "REPRINT_UNCERTAIN";
                return "REPRINT_FAIL";
            }
            if (state == PrintSubmissionState.Submitted) return "PASS";
            if (state == PrintSubmissionState.Uncertain) return "UNCERTAIN";
            return "FAIL";
        }

        public string GetCompletionStatus(PrintResult result, bool historySaved, PrintJobKind kind)
        {
            var state = Classify(result);
            if (kind == PrintJobKind.Reprint)
            {
                if (state == PrintSubmissionState.Submitted)
                    return historySaved ? "补打印作业已提交" : "补打印作业已提交，历史保存失败";
                if (state == PrintSubmissionState.Uncertain)
                    return historySaved ? "补打印结果待核查" : "补打印结果待核查，历史保存失败";
                return historySaved ? "补打印失败" : "补打印失败，历史保存失败";
            }
            if (state == PrintSubmissionState.Submitted)
                return historySaved ? "就绪" : "打印作业已提交，历史保存失败";
            if (state == PrintSubmissionState.Uncertain)
                return historySaved ? "打印结果待核查" : "打印结果待核查，历史保存失败";
            return historySaved ? "打印提交失败" : "打印提交失败，历史保存失败";
        }
    }
}
