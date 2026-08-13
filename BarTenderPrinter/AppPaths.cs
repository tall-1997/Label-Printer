using System;
using System.Diagnostics;
using System.IO;

namespace BarTenderPrinter
{
    public static class AppPaths
    {
        public static readonly string DataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BarTenderPrinter");

        public static readonly string ConfigFile = Path.Combine(DataDirectory, "config.ini");
        public static readonly string RecordsFile = Path.Combine(DataDirectory, "print_records.csv");
        public static readonly string RecordsJsonlFile = Path.Combine(DataDirectory, "print_records.jsonl");
        public static readonly string RecordsSqliteFile = Path.Combine(DataDirectory, "print_records.db");
        public static readonly string LogFile = Path.Combine(DataDirectory, "bartender-printer.log");
        public static readonly string TemplateSettingsFile = Path.Combine(DataDirectory, "template_settings.json");
        public static readonly string OrdersFile = Path.Combine(DataDirectory, "orders.json");
        public static readonly string AccountsFile = Path.Combine(DataDirectory, "accounts.json");
        public static readonly string ApplicationStateFile = Path.Combine(DataDirectory, "application-state.json");
        public static readonly string ValidationDataDirectory = Path.Combine(DataDirectory, "validation-data");
        public static readonly string PreviewDirectory = Path.Combine(DataDirectory, "previews");
        public static readonly string HistoryRecordsDirectory = Path.Combine(AppContext.BaseDirectory, "history-records");

        public static void Initialize()
        {
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(ValidationDataDirectory);
            Directory.CreateDirectory(PreviewDirectory);
            var legacyDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".bartender-printer");

            MigrateFile(legacyDirectory, "config.ini", ConfigFile);
            MigrateFile(legacyDirectory, "print_records.csv", RecordsFile);
            MigrateFile(legacyDirectory, "print_records.jsonl", RecordsJsonlFile);
            MigrateFile(legacyDirectory, "bartender-printer.log", LogFile);
        }

        private static void MigrateFile(string legacyDirectory, string fileName, string targetPath)
        {
            try
            {
                var sourcePath = Path.Combine(legacyDirectory, fileName);
                if (!File.Exists(targetPath) && File.Exists(sourcePath))
                    File.Copy(sourcePath, targetPath);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"迁移旧版文件失败: {fileName}; {ex.Message}");
            }
        }
    }
}
