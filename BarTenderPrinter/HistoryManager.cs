using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace BarTenderPrinter
{
    public class PrintRecord
    {
        public string RecordId { get; set; }
        public string Imei { get; set; }
        public string TemplateName { get; set; }
        public string TemplatePath { get; set; }
        public Dictionary<string, string> FieldValues { get; set; }
        public string PrintTime { get; set; }
        public string Status { get; set; }
        public string Printer { get; set; }
        public int Copies { get; set; }

        public PrintRecord(string imei, string printTime, string status)
        {
            RecordId = Guid.NewGuid().ToString("N");
            Imei = imei ?? "";
            TemplateName = "";
            TemplatePath = "";
            FieldValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            PrintTime = printTime ?? "";
            Status = status ?? "PASS";
            Printer = "";
            Copies = 1;
        }

        public PrintRecord(string templateName, string templatePath, Dictionary<string, string> fieldValues,
            string printTime, string status, string printer, int copies)
        {
            RecordId = Guid.NewGuid().ToString("N");
            TemplateName = templateName ?? "";
            TemplatePath = templatePath ?? "";
            FieldValues = new Dictionary<string, string>(fieldValues ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
            Imei = string.Join("|", FieldValues.Values);
            PrintTime = printTime ?? "";
            Status = status ?? "PASS";
            Printer = printer ?? "";
            Copies = Math.Max(1, copies);
        }
    }

    public class HistoryManager
    {
        private readonly string _recordsFile;
        private readonly Dictionary<string, HashSet<string>> _templateValueIndexes = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        private bool _usesCurrentFormat;
        private const string Header = "record_id,template_name,template_path,field_values,print_time,status,printer,copies";
        public List<PrintRecord> Records { get; private set; }

        public HistoryManager()
        {
            AppPaths.Initialize();
            _recordsFile = AppPaths.RecordsFile;
            Records = new List<PrintRecord>();
        }

        public void Load()
        {
            Records.Clear();
            _templateValueIndexes.Clear();
            if (!File.Exists(_recordsFile)) return;
            try
            {
                var lines = File.ReadAllLines(_recordsFile, Encoding.UTF8);
                _usesCurrentFormat = lines.Length > 0 && string.Equals(lines[0].TrimStart('\uFEFF'), Header, StringComparison.Ordinal);
                for (int i = 1; i < lines.Length; i++)
                {
                    var parts = ParseCsvLine(lines[i]);
                    if (parts.Count >= 8)
                    {
                        var fields = DeserializeFields(parts[3]);
                        int.TryParse(parts[7], out var copies);
                        var record = new PrintRecord(parts[1], parts[2], fields, parts[4], parts[5], parts[6], copies)
                        {
                            RecordId = string.IsNullOrEmpty(parts[0]) ? Guid.NewGuid().ToString("N") : parts[0]
                        };
                        Records.Add(record);
                        IndexRecord(record);
                    }
                    else if (parts.Count >= 3)
                    {
                        var record = new PrintRecord(parts[0], parts[1], parts[2]);
                        Records.Add(record);
                        IndexRecord(record);
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
            var tempFile = _recordsFile + ".tmp";
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine(Header);
                foreach (var r in Records)
                {
                    sb.AppendLine(string.Join(",", new[]
                    {
                        Csv(r.RecordId), Csv(r.TemplateName), Csv(r.TemplatePath), Csv(SerializeFields(r.FieldValues)),
                        Csv(r.PrintTime), Csv(r.Status), Csv(r.Printer), r.Copies.ToString()
                    }));
                }
                File.WriteAllText(tempFile, sb.ToString(), Encoding.UTF8);
                if (File.Exists(_recordsFile))
                    File.Copy(_recordsFile, _recordsFile + ".bak", true);
                File.Move(tempFile, _recordsFile, true);
                _usesCurrentFormat = true;
            }
            catch (Exception ex)
            {
                LoggerService.Error("保存历史记录失败", ex);
            }
            finally
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            }
        }

        public void Add(string imei, string status)
        {
            var record = new PrintRecord(imei, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), status);
            Records.Add(record);
            IndexRecord(record);
            Append(record);
        }

        public void Add(string templateName, string templatePath, Dictionary<string, string> fieldValues,
            string status, string printer, int copies)
        {
            var record = new PrintRecord(templateName, templatePath, fieldValues,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), status, printer, copies);
            Records.Add(record);
            IndexRecord(record);
            Append(record);
        }

        public bool IsPrinted(string imei)
        {
            return Records.Any(r => r.Imei == imei);
        }

        public bool ContainsAnyValue(string value)
        {
            return Records.Any(r => GetValues(r).Contains(value, StringComparer.OrdinalIgnoreCase));
        }

        public bool ContainsAnyValue(string templateName, string templatePath, string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            return _templateValueIndexes.TryGetValue(GetTemplateKey(templateName, templatePath), out var values) && values.Contains(value);
        }

        public PrintRecord GetById(string recordId)
        {
            return Records.FirstOrDefault(record => string.Equals(record.RecordId, recordId, StringComparison.Ordinal));
        }

        public bool Delete(string recordId)
        {
            var removed = Records.RemoveAll(record => string.Equals(record.RecordId, recordId, StringComparison.Ordinal)) > 0;
            if (!removed) return false;
            RebuildIndexes();
            Save();
            return true;
        }

        public void Clear()
        {
            Records.Clear();
            _templateValueIndexes.Clear();
            Save();
        }

        public void Clear(string templateName, string templatePath)
        {
            var templateKey = GetTemplateKey(templateName, templatePath);
            Records.RemoveAll(record => string.Equals(GetTemplateKey(record.TemplateName, record.TemplatePath), templateKey, StringComparison.OrdinalIgnoreCase));
            RebuildIndexes();
            Save();
        }

        public List<PrintRecord> Search(string templateName, string templatePath, string keyword, bool exact)
        {
            var templateKey = GetTemplateKey(templateName, templatePath);
            var query = (keyword ?? "").Trim();
            return Records.Where(record =>
            {
                if (!string.Equals(GetTemplateKey(record.TemplateName, record.TemplatePath), templateKey, StringComparison.OrdinalIgnoreCase)) return false;
                if (string.IsNullOrEmpty(query)) return true;
                var fieldPairs = (record.FieldValues ?? new Dictionary<string, string>()).Select(item => $"{item.Key}={item.Value}").ToList();
                var fields = new[] { record.RecordId, record.TemplateName, record.TemplatePath, record.PrintTime, record.Status, record.Printer, record.Copies.ToString(), string.Join(" | ", fieldPairs) }
                    .Concat((record.FieldValues ?? new Dictionary<string, string>()).Keys)
                    .Concat(fieldPairs)
                    .Concat(GetValues(record));
                return exact
                    ? fields.Any(value => string.Equals(value, query, StringComparison.OrdinalIgnoreCase))
                    : fields.Any(value => (value ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
            }).ToList();
        }

        public void Export(string path, IEnumerable<PrintRecord> records)
        {
            var filtered = records?.ToList() ?? new List<PrintRecord>();

            var sb = new StringBuilder();
            sb.AppendLine("template_name,template_path,field_values,print_time,status,printer,copies");
            foreach (var r in filtered)
            {
                var values = string.Join("; ", (r.FieldValues ?? new Dictionary<string, string>()).Select(item => $"{item.Key}={item.Value}"));
                if (string.IsNullOrEmpty(values)) values = r.Imei;
                sb.AppendLine(string.Join(",", new[] { Csv(r.TemplateName), Csv(r.TemplatePath), Csv(values), Csv(r.PrintTime), Csv(r.Status), Csv(r.Printer), r.Copies.ToString() }));
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
            for (int i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else inQuotes = !inQuotes;
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

        private void IndexRecord(PrintRecord record)
        {
            var key = GetTemplateKey(record.TemplateName, record.TemplatePath);
            if (!_templateValueIndexes.TryGetValue(key, out var values))
            {
                values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _templateValueIndexes[key] = values;
            }
            foreach (var value in GetValues(record))
                if (!string.IsNullOrEmpty(value)) values.Add(value);
        }

        private void Append(PrintRecord record)
        {
            if (!_usesCurrentFormat)
            {
                Save();
                return;
            }
            try
            {
                File.AppendAllText(_recordsFile, ToCsvLine(record) + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                LoggerService.Error("追加历史记录失败", ex);
            }
        }

        private static string ToCsvLine(PrintRecord record)
        {
            return string.Join(",", new[]
            {
                Csv(record.RecordId), Csv(record.TemplateName), Csv(record.TemplatePath), Csv(SerializeFields(record.FieldValues)),
                Csv(record.PrintTime), Csv(record.Status), Csv(record.Printer), record.Copies.ToString()
            });
        }

        private void RebuildIndexes()
        {
            _templateValueIndexes.Clear();
            foreach (var record in Records) IndexRecord(record);
        }

        private static IEnumerable<string> GetValues(PrintRecord record)
        {
            if (record.FieldValues != null && record.FieldValues.Count > 0) return record.FieldValues.Values;
            return (record.Imei ?? "").Split('|');
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            try { return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar); }
            catch { return path.Trim(); }
        }

        private static string GetTemplateKey(string templateName, string templatePath)
        {
            return $"{templateName?.Trim()}|{NormalizePath(templatePath)}";
        }

        private static string SerializeFields(Dictionary<string, string> fields)
        {
            var json = JsonSerializer.Serialize(fields ?? new Dictionary<string, string>());
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        }

        private static Dictionary<string, string> DeserializeFields(string value)
        {
            try
            {
                var json = Encoding.UTF8.GetString(Convert.FromBase64String(value));
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
            }
            catch { return new Dictionary<string, string>(); }
        }

        private static string Csv(string value) => $"\"{(value ?? "").Replace("\"", "\"\"")}\"";
    }
}
