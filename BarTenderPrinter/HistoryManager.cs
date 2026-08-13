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
        public string TemplateId { get; set; }
        public Dictionary<string, string> FieldValues { get; set; }
        public string PrintTime { get; set; }
        public string Status { get; set; }
        public string Printer { get; set; }
        public int Copies { get; set; }
        public string OperatorName { get; set; }
        public string ReprintReason { get; set; }
        public string TemplateVersion { get; set; }
        public string DiagnosticDetails { get; set; }
        public string OrderName { get; set; }

        public PrintRecord(string imei, string printTime, string status)
        {
            RecordId = Guid.NewGuid().ToString("N");
            Imei = imei ?? "";
            TemplateName = "";
            TemplatePath = "";
            TemplateId = "";
            FieldValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            PrintTime = printTime ?? "";
            Status = status ?? "PASS";
            Printer = "";
            Copies = 1;
            OperatorName = "";
            ReprintReason = "";
            TemplateVersion = "";
            DiagnosticDetails = "";
            OrderName = "";
        }

        public PrintRecord(string templateName, string templatePath, Dictionary<string, string> fieldValues,
            string printTime, string status, string printer, int copies)
        {
            RecordId = Guid.NewGuid().ToString("N");
            TemplateName = templateName ?? "";
            TemplatePath = templatePath ?? "";
            TemplateId = "";
            FieldValues = new Dictionary<string, string>(fieldValues ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
            Imei = string.Join("|", FieldValues.Values);
            PrintTime = printTime ?? "";
            Status = status ?? "PASS";
            Printer = printer ?? "";
            Copies = Math.Max(1, copies);
            OperatorName = "";
            ReprintReason = "";
            TemplateVersion = "";
            DiagnosticDetails = "";
            OrderName = "";
        }

        public PrintRecord(string templateName, string templatePath, string templateId, Dictionary<string, string> fieldValues,
            string printTime, string status, string printer, int copies)
            : this(templateName, templatePath, fieldValues, printTime, status, printer, copies)
        {
            TemplateId = templateId ?? "";
        }
    }

    public class HistoryManager
    {
        private readonly string _recordsFile;
        private readonly Dictionary<string, HashSet<string>> _templateValueIndexes = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        private bool _usesCurrentFormat;
        private const string Header = "record_id,template_name,template_path,template_id,field_values,print_time,status,printer,copies,operator,reprint_reason,template_version,diagnostic_details,order_name";
        private const string TemplateIdHeader = "record_id,template_name,template_path,template_id,field_values,print_time,status,printer,copies";
        private const string PreviousHeader = "record_id,template_name,template_path,field_values,print_time,status,printer,copies";
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
                _usesCurrentFormat = false;
                var lineNumber = 0;
                var usesPreviousFormat = false;
                foreach (var line in File.ReadLines(_recordsFile, Encoding.UTF8))
                {
                    lineNumber++;
                    if (lineNumber == 1)
                    {
                        var fileHeader = line.TrimStart('\uFEFF');
                        _usesCurrentFormat = string.Equals(fileHeader, Header, StringComparison.Ordinal) || string.Equals(fileHeader, TemplateIdHeader, StringComparison.Ordinal);
                        usesPreviousFormat = string.Equals(fileHeader, PreviousHeader, StringComparison.Ordinal);
                        if (_usesCurrentFormat || usesPreviousFormat) continue;
                    }
                    LoadRecordLine(line, lineNumber, usesPreviousFormat, true);
                }
            }
            catch (Exception ex)
            {
                LoggerService.Error("加载历史记录失败", ex);
            }
        }

        private void LoadRecordLine(string line, int lineNumber, bool usesPreviousFormat, bool allowLegacyHeader)
        {
            var parts = ParseCsvLine(line);
            if (allowLegacyHeader && lineNumber == 1 && IsLegacyHeader(parts)) return;
            if (_usesCurrentFormat && parts.Count >= 9)
            {
                var fields = DeserializeFields(parts[4]);
                int.TryParse(parts[8], out var copies);
                var record = new PrintRecord(parts[1], parts[2], parts[3], fields, parts[5], parts[6], parts[7], copies)
                {
                    RecordId = string.IsNullOrEmpty(parts[0]) ? Guid.NewGuid().ToString("N") : parts[0],
                    OperatorName = parts.Count > 9 ? parts[9] : "",
                    ReprintReason = parts.Count > 10 ? parts[10] : "",
                    TemplateVersion = parts.Count > 11 ? parts[11] : "",
                    DiagnosticDetails = parts.Count > 12 ? parts[12] : "",
                    OrderName = parts.Count > 13 ? parts[13] : ""
                };
                Records.Add(record);
                IndexRecord(record);
            }
            else if (parts.Count >= 8)
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
            else if (!usesPreviousFormat && parts.Count >= 3)
            {
                var record = new PrintRecord(parts[0], parts[1], parts[2]);
                Records.Add(record);
                IndexRecord(record);
            }
        }

        public bool Save()
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
                        Csv(r.RecordId), Csv(r.TemplateName), Csv(r.TemplatePath), Csv(r.TemplateId), Csv(SerializeFields(r.FieldValues)),
                        Csv(r.PrintTime), Csv(r.Status), Csv(r.Printer), r.Copies.ToString(),
                        Csv(r.OperatorName), Csv(r.ReprintReason), Csv(r.TemplateVersion), Csv(r.DiagnosticDetails), Csv(r.OrderName)
                    }));
                }
                File.WriteAllText(tempFile, sb.ToString(), Encoding.UTF8);
                if (File.Exists(_recordsFile))
                    File.Copy(_recordsFile, _recordsFile + ".bak", true);
                File.Move(tempFile, _recordsFile, true);
                _usesCurrentFormat = true;
                return true;
            }
            catch (Exception ex)
            {
                LoggerService.Error("保存历史记录失败", ex);
                return false;
            }
            finally
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            }
        }

        public bool Add(string imei, string status)
        {
            var record = new PrintRecord(imei, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), status);
            Records.Add(record);
            IndexRecord(record);
            if (!Append(record)) { Records.Remove(record); RebuildIndexes(); return false; }
            return true;
        }

        public bool Add(string templateName, string templatePath, Dictionary<string, string> fieldValues,
            string status, string printer, int copies)
        {
            return Add(templateName, templatePath, "", fieldValues, status, printer, copies);
        }

        public bool Add(string templateName, string templatePath, string templateId, Dictionary<string, string> fieldValues,
            string status, string printer, int copies, string operatorName = "", string reprintReason = "", string templateVersion = "", string diagnosticDetails = "", string orderName = "")
        {
            var record = new PrintRecord(templateName, templatePath, templateId, fieldValues,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), status, printer, copies)
            {
                OperatorName = operatorName ?? "",
                ReprintReason = reprintReason ?? "",
                TemplateVersion = templateVersion ?? "",
                DiagnosticDetails = diagnosticDetails ?? "",
                OrderName = orderName ?? ""
            };
            Records.Add(record);
            IndexRecord(record);
            if (!Append(record)) { Records.Remove(record); RebuildIndexes(); return false; }
            return true;
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

        public bool ContainsAnyValue(string templateName, string templatePath, string templateId, string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            if (!string.IsNullOrWhiteSpace(templateId) && _templateValueIndexes.TryGetValue(GetTemplateKey(templateId), out var values) && values.Contains(value)) return true;
            return ContainsAnyValue(templateName, templatePath, value);
        }

        public PrintRecord GetById(string recordId)
        {
            return Records.FirstOrDefault(record => string.Equals(record.RecordId, recordId, StringComparison.Ordinal));
        }

        public bool Delete(string recordId)
        {
            var snapshot = Records.ToList();
            var removed = Records.RemoveAll(record => string.Equals(record.RecordId, recordId, StringComparison.Ordinal)) > 0;
            if (!removed) return false;
            RebuildIndexes();
            if (!Save()) { Records = snapshot; RebuildIndexes(); return false; }
            return true;
        }

        public bool Clear()
        {
            var snapshot = Records.ToList();
            Records.Clear();
            _templateValueIndexes.Clear();
            if (Save()) return true;
            Records = snapshot;
            RebuildIndexes();
            return false;
        }

        public bool Clear(string templateName, string templatePath)
        {
            return Clear(templateName, templatePath, "");
        }

        public bool Clear(string templateName, string templatePath, string templateId)
        {
            var snapshot = Records.ToList();
            Records.RemoveAll(record => IsTemplateMatch(record, templateName, templatePath, templateId));
            RebuildIndexes();
            if (Save()) return true;
            Records = snapshot;
            RebuildIndexes();
            return false;
        }

        public List<PrintRecord> Search(string templateName, string templatePath, string keyword, bool exact)
        {
            return Search(templateName, templatePath, "", keyword, exact, 0, false);
        }

        public List<PrintRecord> Search(string templateName, string templatePath, string templateId, string keyword, bool exact, int limit = 0, bool newestFirst = false, int offset = 0)
        {
            var query = (keyword ?? "").Trim();
            var source = newestFirst ? Records.AsEnumerable().Reverse() : Records.AsEnumerable();
            var matches = new List<PrintRecord>();
            var skipped = 0;
            foreach (var record in source)
            {
                if (!IsTemplateMatch(record, templateName, templatePath, templateId)) continue;
                if (string.IsNullOrEmpty(query))
                {
                    if (skipped++ < offset) continue;
                    matches.Add(record);
                    if (limit > 0 && matches.Count >= limit) break;
                    continue;
                }
                var fieldPairs = (record.FieldValues ?? new Dictionary<string, string>()).Select(item => $"{item.Key}={item.Value}").ToList();
                var fields = new[] { record.RecordId, record.TemplateName, record.TemplatePath, record.PrintTime, record.Status, record.Printer, record.Copies.ToString(), string.Join(" | ", fieldPairs) }
                    .Concat((record.FieldValues ?? new Dictionary<string, string>()).Keys)
                    .Concat(fieldPairs)
                    .Concat(GetValues(record));
                var matched = exact
                    ? fields.Any(value => string.Equals(value, query, StringComparison.OrdinalIgnoreCase))
                    : fields.Any(value => (value ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
                if (!matched) continue;
                if (skipped++ < offset) continue;
                matches.Add(record);
                if (limit > 0 && matches.Count >= limit) break;
            }
            return matches;
        }

        public int Count(string templateName, string templatePath, string templateId)
        {
            return Records.Count(record => IsTemplateMatch(record, templateName, templatePath, templateId));
        }

        public int TodayCount(string templateName, string templatePath, string templateId)
        {
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            return Records.Count(record => IsTemplateMatch(record, templateName, templatePath, templateId) && record.PrintTime.StartsWith(today));
        }

        public void Export(string path, IEnumerable<PrintRecord> records)
        {
            var filtered = records?.ToList() ?? new List<PrintRecord>();

            var sb = new StringBuilder();
            sb.AppendLine("template_name,template_path,template_id,order_name,field_values,print_time,status,printer,copies,operator,reprint_reason,template_version,diagnostic_details");
            foreach (var r in filtered)
            {
                var values = string.Join("; ", (r.FieldValues ?? new Dictionary<string, string>()).Select(item => $"{item.Key}={item.Value}"));
                if (string.IsNullOrEmpty(values)) values = r.Imei;
                sb.AppendLine(string.Join(",", new[] { Csv(r.TemplateName), Csv(r.TemplatePath), Csv(r.TemplateId), Csv(r.OrderName), Csv(values), Csv(r.PrintTime), Csv(r.Status), Csv(r.Printer), r.Copies.ToString(), Csv(r.OperatorName), Csv(r.ReprintReason), Csv(r.TemplateVersion), Csv(r.DiagnosticDetails) }));
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

        private static List<string> ParseCsvLine(string line) => CsvUtils.ParseLine(line);

        private void IndexRecord(PrintRecord record)
        {
            if (!IsSuccessfulStatus(record.Status)) return;
            var key = GetTemplateKey(record);
            if (!_templateValueIndexes.TryGetValue(key, out var values))
            {
                values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _templateValueIndexes[key] = values;
            }
            foreach (var value in GetValues(record))
                if (!string.IsNullOrEmpty(value)) values.Add(value);
        }

        private static bool IsSuccessfulStatus(string status)
        {
            return string.Equals(status, "PASS", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(status, "REPRINT_PASS", StringComparison.OrdinalIgnoreCase);
        }

        private bool Append(PrintRecord record)
        {
            if (!_usesCurrentFormat)
            {
                return Save();
            }
            try
            {
                File.AppendAllText(_recordsFile, ToCsvLine(record) + Environment.NewLine, Encoding.UTF8);
                return true;
            }
            catch (Exception ex)
            {
                LoggerService.Error("追加历史记录失败", ex);
                return false;
            }
        }

        private static string ToCsvLine(PrintRecord record)
        {
            return string.Join(",", new[]
            {
                Csv(record.RecordId), Csv(record.TemplateName), Csv(record.TemplatePath), Csv(record.TemplateId), Csv(SerializeFields(record.FieldValues)),
                Csv(record.PrintTime), Csv(record.Status), Csv(record.Printer), record.Copies.ToString(),
                Csv(record.OperatorName), Csv(record.ReprintReason), Csv(record.TemplateVersion), Csv(record.DiagnosticDetails), Csv(record.OrderName)
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

        private static bool IsLegacyHeader(List<string> parts)
        {
            if (parts == null || parts.Count < 3) return false;
            return string.Equals(parts[0], "imei", StringComparison.OrdinalIgnoreCase) &&
                   parts[1].IndexOf("time", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   parts[2].IndexOf("status", StringComparison.OrdinalIgnoreCase) >= 0;
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

        private static string GetTemplateKey(string templateId)
        {
            return $"id:{templateId?.Trim()}";
        }

        private static string GetTemplateKey(PrintRecord record)
        {
            return !string.IsNullOrWhiteSpace(record.TemplateId) ? GetTemplateKey(record.TemplateId) : GetTemplateKey(record.TemplateName, record.TemplatePath);
        }

        private static bool IsTemplateMatch(PrintRecord record, string templateName, string templatePath, string templateId)
        {
            if (!string.IsNullOrWhiteSpace(templateId) && !string.IsNullOrWhiteSpace(record.TemplateId))
                return string.Equals(record.TemplateId, templateId, StringComparison.OrdinalIgnoreCase);
            return string.Equals(GetTemplateKey(record.TemplateName, record.TemplatePath), GetTemplateKey(templateName, templatePath), StringComparison.OrdinalIgnoreCase);
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

        private static string Csv(string value) => CsvUtils.Escape(value);
    }
}
