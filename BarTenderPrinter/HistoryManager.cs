using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace BarTenderPrinter
{
    public class PrintRecord
    {
        public string Imei { get; set; }
        public string PrintTime { get; set; }
        public string Status { get; set; }

        public PrintRecord(string imei, string printTime, string status)
        {
            Imei = imei ?? "";
            PrintTime = printTime ?? "";
            Status = status ?? "PASS";
        }
    }

    public class HistoryManager
    {
        private readonly string _recordsFile;
        public List<PrintRecord> Records { get; private set; }

        public HistoryManager()
        {
            var appDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".bartender-printer");
            Directory.CreateDirectory(appDir);
            _recordsFile = Path.Combine(appDir, "print_records.csv");
            Records = new List<PrintRecord>();
            Load();
        }

        public void Load()
        {
            Records.Clear();
            if (!File.Exists(_recordsFile)) return;
            try
            {
                var lines = File.ReadAllLines(_recordsFile, Encoding.UTF8);
                for (int i = 1; i < lines.Length; i++)
                {
                    var parts = ParseCsvLine(lines[i]);
                    if (parts.Count >= 3)
                    {
                        Records.Add(new PrintRecord(parts[0], parts[1], parts[2]));
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerService.Error("加载历史记录失败", ex);
            }
        }

        public void Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("imei,print_time,status");
                foreach (var r in Records)
                {
                    sb.AppendLine($"\"{r.Imei}\",\"{r.PrintTime}\",\"{r.Status}\"");
                }
                File.WriteAllText(_recordsFile, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                LoggerService.Error("保存历史记录失败", ex);
            }
        }

        public void Add(string imei, string status)
        {
            Records.Add(new PrintRecord(imei, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), status));
            Save();
        }

        public bool IsPrinted(string imei)
        {
            return Records.Any(r => r.Imei == imei);
        }

        public bool ContainsAnyValue(string value)
        {
            return Records.Any(r =>
            {
                var parts = r.Imei.Split('|');
                return parts.Contains(value);
            });
        }

        public void Clear()
        {
            Records.Clear();
            Save();
        }

        public void Export(string path, string keyword = "")
        {
            var filtered = string.IsNullOrEmpty(keyword)
                ? Records
                : Records.Where(r =>
                    r.Imei.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    r.PrintTime.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    r.Status.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            var sb = new StringBuilder();
            sb.AppendLine("imei,print_time,status");
            foreach (var r in filtered)
            {
                sb.AppendLine($"\"{r.Imei}\",\"{r.PrintTime}\",\"{r.Status}\"");
            }
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        public int TodayCount()
        {
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            return Records.Count(r => r.PrintTime.StartsWith(today));
        }

        public int TotalCount()
        {
            return Records.Count;
        }

        private static List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(line)) return result;
            var current = new StringBuilder();
            bool inQuotes = false;
            foreach (var c in line)
            {
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(current.ToString().Trim());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
            result.Add(current.ToString().Trim());
            return result;
        }
    }
}
