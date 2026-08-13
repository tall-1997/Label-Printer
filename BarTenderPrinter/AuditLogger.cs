using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace BarTenderPrinter
{
    public static class AuditLogger
    {
        private static readonly string AuditFile = Path.Combine(AppPaths.DataDirectory, "audit.log");

        public static void Append(string operatorName, string action, string detail)
        {
            try
            {
                AppPaths.Initialize();
                var previousHash = GetLastHash();
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                var payload = $"{timestamp}|{operatorName}|{action}|{detail}|{previousHash}";
                var hash = ComputeHash(payload);
                File.AppendAllText(AuditFile, $"{payload}|{hash}{Environment.NewLine}", Encoding.UTF8);
            }
            catch (Exception ex)
            {
                LoggerService.Warn($"写入审计日志失败: {ex.Message}");
            }
        }

        private static string GetLastHash()
        {
            if (!File.Exists(AuditFile)) return "ROOT";
            string last = null;
            foreach (var line in File.ReadLines(AuditFile, Encoding.UTF8))
                if (!string.IsNullOrWhiteSpace(line)) last = line;
            if (string.IsNullOrWhiteSpace(last)) return "ROOT";
            var index = last.LastIndexOf('|');
            return index >= 0 ? last.Substring(index + 1) : "ROOT";
        }

        private static string ComputeHash(string payload)
        {
            using (var sha = SHA256.Create())
                return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(payload ?? "")));
        }
    }
}
