using System;
using System.IO;

namespace BarTenderPrinter
{
    public static class LoggerService
    {
        private static readonly string LogFile = AppPaths.LogFile;
        private static readonly object Lock = new object();

        static LoggerService()
        {
            AppPaths.Initialize();
        }

        public static void Info(string message)
        {
            Write("INFO", message);
        }

        public static void Debug(string message)
        {
            Write("DEBUG", message);
        }

        public static void Error(string message)
        {
            Write("ERROR", message);
        }

        public static void Error(string message, Exception ex)
        {
            Write("ERROR", $"{message}: {ex.Message}\n{ex.StackTrace}");
        }

        public static void Warn(string message)
        {
            Write("WARN", message);
        }

        public static string GetLogFile()
        {
            return LogFile;
        }

        public static void ExportLog(string targetPath)
        {
            if (File.Exists(LogFile))
            {
                File.Copy(LogFile, targetPath, true);
            }
        }

        private static void Write(string level, string message)
        {
            try
            {
                lock (Lock)
                {
                    var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}";
                    File.AppendAllText(LogFile, line);
                }
            }
            catch
            {
            }
        }
    }
}
