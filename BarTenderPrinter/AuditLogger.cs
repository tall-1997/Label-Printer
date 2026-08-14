using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace BarTenderPrinter
{
    public static class AuditLogger
    {
        private static readonly string AuditFile = Path.Combine(AppPaths.DataDirectory, "audit.log");
        private static readonly string AuditKeyFile = Path.Combine(AppPaths.DataDirectory, "audit-integrity.key");
        private static readonly object Lock = new object();

        public static void Append(string operatorName, string action, string detail)
        {
            try
            {
                AppPaths.Initialize();
                lock (Lock)
                {
                    var previousHash = GetLastHash();
                    var timestamp = DateTime.UtcNow.ToString("O");
                    var payload = $"{timestamp}|{Sanitize(operatorName)}|{Sanitize(action)}|{Sanitize(detail)}|{previousHash}";
                    var hash = ComputeHash(payload);
                    File.AppendAllText(AuditFile, $"{payload}|{hash}{Environment.NewLine}", Encoding.UTF8);
                }
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
            using (var hmac = new HMACSHA256(GetAuditKey()))
                return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload ?? "")));
        }

        private static byte[] GetAuditKey()
        {
            if (File.Exists(AuditKeyFile)) return Convert.FromBase64String(File.ReadAllText(AuditKeyFile).Trim());
            var key = RandomNumberGenerator.GetBytes(32);
            AtomicFileWriter.WriteAllText(AuditKeyFile, Convert.ToBase64String(key), Encoding.UTF8);
            return key;
        }

        private static string Sanitize(string value)
        {
            return (value ?? "").Replace("|", "\\u007C").Replace("\r", "\\r").Replace("\n", "\\n");
        }
    }
}
