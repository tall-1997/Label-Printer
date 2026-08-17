using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

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
        public static readonly string PrintJobLedgerFile = Path.Combine(DataDirectory, "print_jobs.db");
        public static readonly string LogFile = Path.Combine(DataDirectory, "bartender-printer.log");
        public static readonly string TemplateSettingsFile = Path.Combine(DataDirectory, "template_settings.json");
        public static readonly string OrdersFile = Path.Combine(DataDirectory, "orders.json");
        public static readonly string AccountsFile = Path.Combine(DataDirectory, "accounts.json");
        public static readonly string ApplicationStateFile = Path.Combine(DataDirectory, "application-state.json");
        public static readonly string ValidationDataDirectory = Path.Combine(DataDirectory, "validation-data");
        public static readonly string PreviewDirectory = Path.Combine(DataDirectory, "previews");
        public static readonly string HistoryRecordsDirectory = Path.Combine(DataDirectory, "history-records");
        public static readonly string SyncProfileFile = Path.Combine(DataDirectory, "sync-profile.dat");
        public static readonly string SyncDatabaseFile = Path.Combine(DataDirectory, "sync.db");
        public static readonly string SyncIncomingDirectory = Path.Combine(DataDirectory, "sync-incoming");
        public static readonly string SyncTemplateCacheDirectory = Path.Combine(DataDirectory, "template-cache");
        public static readonly string SyncStagingDirectory = Path.Combine(DataDirectory, "sync-staging");
        public static readonly string DirectSyncCertificatesDirectory = Path.Combine(DataDirectory, "direct-sync-certificates");

        public static string GetDirectSyncCertificateFile(string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId) || deviceId.Length > 128 ||
                deviceId.Any(character => !char.IsLetterOrDigit(character) && character != '-' && character != '_'))
                throw new ArgumentException("设备标识无效。", nameof(deviceId));
            return Path.Combine(DirectSyncCertificatesDirectory, deviceId + ".pfx.dat");
        }

        public static void Initialize()
        {
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(ValidationDataDirectory);
            Directory.CreateDirectory(PreviewDirectory);
            Directory.CreateDirectory(SyncIncomingDirectory);
            Directory.CreateDirectory(SyncTemplateCacheDirectory);
            Directory.CreateDirectory(DirectSyncCertificatesDirectory);
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
