using System;
using System.Collections.Generic;

namespace BarTenderPrinter
{
    public interface IBarTenderService : IDisposable
    {
        bool IsConnected { get; }
        bool IsOfflineMode { get; }
        bool Connect();
        List<string> GetTemplateDataSources(string templatePath);
        void RunDiagnostics(string templatePath);
        PrintResult Print(string templatePath, Dictionary<string, string> fieldValues, string printer, int copies);
        string ExportPreviewImage(string templatePath, Dictionary<string, string> fieldValues);
        string[] GetAvailableTemplates(string directory);
        string[] GetPrinters();
        void Disconnect();
    }

    public interface IHistoryRepository
    {
        List<PrintRecord> Records { get; }
        void Load();
        bool Add(string templateName, string templatePath, string templateId, Dictionary<string, string> fieldValues, string status, string printer, int copies, string operatorName = "", string reprintReason = "", string templateVersion = "", string diagnosticDetails = "", string orderName = "", string orderId = "", List<string> templateFields = null);
        bool Clear(string templateName, string templatePath, string templateId);
        bool Delete(string recordId);
        PrintRecord GetById(string recordId);
        List<PrintRecord> Search(string templateName, string templatePath, string templateId, string keyword, bool exact, int limit = 0, bool newestFirst = false, int offset = 0);
        List<PrintRecord> Search(string templateName, string templatePath, string templateId, string keyword, bool exact, int limit, bool newestFirst, int offset, string status, string datePrefix, string printer, string orderQuery);
        int Count(string templateName, string templatePath, string templateId);
        int TodayCount(string templateName, string templatePath, string templateId);
        bool ContainsAnyValue(string templateName, string templatePath, string templateId, string value);
        void Export(string path, IEnumerable<PrintRecord> records);
    }
}
