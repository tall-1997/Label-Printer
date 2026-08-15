using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BarTenderPrinter
{
    public sealed class OrderTemplate
    {
        public string SourceSha256 { get; set; } = "";
        public long SourceLastWriteTimeUtcTicks { get; set; }
        public long SourceLength { get; set; }
    }

    public sealed class PrintRecord
    {
    }

    public interface IBarTenderService : IDisposable
    {
        bool IsConnected { get; }
        bool IsOfflineMode { get; }
        bool IsPreviewAvailable { get; }
        string PreviewUnavailableReason { get; }
        bool Connect();
        List<string> GetTemplateDataSources(string templatePath);
        void RunDiagnostics(string templatePath);
        PrintResult Print(string templatePath, Dictionary<string, string> fieldValues, string printer, int copies);
        Task<PrintResult> PrintAsync(string templatePath, Dictionary<string, string> fieldValues, string printer, int copies);
        Task<string> ExportPreviewAsync(string templatePath, Dictionary<string, string> fieldValues);
        string[] GetAvailableTemplates(string directory);
        string[] GetPrinters();
        void Disconnect();
    }

    public interface IHistoryRepository
    {
        IReadOnlyList<PrintRecord> Records { get; }
        void Load();
        bool Add(PrintHistoryEntry entry);
        bool Clear(string templateName, string templatePath, string templateId, string operatorName = "", string reason = "");
        bool Delete(string recordId, string operatorName = "", string reason = "");
        PrintRecord GetById(string recordId);
        PrintRecord GetLatestSuccessful(string templateName, string templatePath, string templateId);
        List<PrintRecord> Search(string templateName, string templatePath, string templateId, string keyword, bool exact, int limit = 0, bool newestFirst = false, int offset = 0);
        List<PrintRecord> Search(string templateName, string templatePath, string templateId, string keyword, bool exact, int limit, bool newestFirst, int offset, string status, string datePrefix, string printer, string orderQuery);
        int Count(string templateName, string templatePath, string templateId);
        int TodayCount(string templateName, string templatePath, string templateId);
        bool ContainsAnyValue(string templateName, string templatePath, string templateId, string value);
        void Export(string path, IEnumerable<PrintRecord> records);
    }
}
