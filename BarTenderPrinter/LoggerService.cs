using System;
using System.IO;

namespace BarTenderPrinter
{
    public static class LoggerService
    {
        private static readonly string LogFile = AppPaths.LogFile;
        private static readonly object Lock = new object();
        private const long MaxLogBytes = 5 * 1024 * 1024;

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
                if (string.Equals(Path.GetFullPath(targetPath), Path.GetFullPath(LogFile), StringComparison.OrdinalIgnoreCase))
                    throw new IOException("不能覆盖当前日志文件。");
                File.Copy(LogFile, targetPath, false);
            }
        }

        private static void Write(string level, string message)
        {
            try
            {
                lock (Lock)
                {
                    RotateIfNeeded();
                    var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {Sanitize(message)}{Environment.NewLine}";
                    File.AppendAllText(LogFile, line);
                }
            }
            catch
            {
            }
        }

        private static void RotateIfNeeded()
        {
            try
            {
                if (!File.Exists(LogFile) || new FileInfo(LogFile).Length < MaxLogBytes) return;
                var archive = Path.Combine(Path.GetDirectoryName(LogFile) ?? AppPaths.DataDirectory,
                    "bartender-printer." + DateTime.Now.ToString("yyyyMMddHHmmssfff") + "." + Guid.NewGuid().ToString("N") + ".log");
                File.Move(LogFile, archive);
            }
            catch { }
        }

        private static string Sanitize(string message)
        {
            return (message ?? "").Replace("\r", "\\r").Replace("\n", "\\n");
        }
    }
}
