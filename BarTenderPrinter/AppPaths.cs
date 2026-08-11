using System;
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
        public static readonly string LogFile = Path.Combine(DataDirectory, "bartender-printer.log");

        public static void Initialize()
        {
            Directory.CreateDirectory(DataDirectory);

            var legacyDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".bartender-printer");

            MigrateFile(legacyDirectory, "config.ini", ConfigFile);
            MigrateFile(legacyDirectory, "print_records.csv", RecordsFile);
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
            catch
            {
            }
        }
    }
}
