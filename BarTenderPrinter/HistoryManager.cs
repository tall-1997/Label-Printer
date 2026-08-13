using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace BarTenderPrinter
{
    public class PrintRecord
    {
        public int SchemaVersion { get; set; } = 2;
        public string RecordId { get; set; }
        public string Imei { get; set; }
        public string TemplateName { get; set; }
        public string TemplatePath { get; set; }
        public string TemplateId { get; set; }
        public string OrderId { get; set; }
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
        public List<string> TemplateFields { get; set; }
        public string RecordChecksum { get; set; }

        public PrintRecord()
        {
            SchemaVersion = 2;
            RecordId = Guid.NewGuid().ToString("N");
            Imei = "";
            TemplateName = "";
            TemplatePath = "";
            TemplateId = "";
            OrderId = "";
            FieldValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            PrintTime = "";
            Status = "PASS";
            Printer = "";
            Copies = 1;
            OperatorName = "";
            ReprintReason = "";
            TemplateVersion = "";
            DiagnosticDetails = "";
            OrderName = "";
            TemplateFields = new List<string>();
            RecordChecksum = "";
        }

        public PrintRecord(string imei, string printTime, string status)
        {
            RecordId = Guid.NewGuid().ToString("N");
            Imei = imei ?? "";
            TemplateName = "";
            TemplatePath = "";
            TemplateId = "";
            OrderId = "";
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
            TemplateFields = new List<string>();
            RecordChecksum = "";
        }

        public PrintRecord(string templateName, string templatePath, Dictionary<string, string> fieldValues,
            string printTime, string status, string printer, int copies)
        {
            RecordId = Guid.NewGuid().ToString("N");
            TemplateName = templateName ?? "";
            TemplatePath = templatePath ?? "";
            TemplateId = "";
            OrderId = "";
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
            TemplateFields = new List<string>();
            RecordChecksum = "";
        }

        public PrintRecord(string templateName, string templatePath, string templateId, Dictionary<string, string> fieldValues,
            string printTime, string status, string printer, int copies)
            : this(templateName, templatePath, fieldValues, printTime, status, printer, copies)
        {
            TemplateId = templateId ?? "";
        }
    }

    public class HistoryManager : IHistoryRepository
    {
        private readonly string _recordsFile;
        private readonly string _recordsJsonlFile;
        private readonly string _recordsSqliteFile;
        private readonly Dictionary<string, HashSet<string>> _templateValueIndexes = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        private bool _usesCurrentFormat;
        private const string Header = "record_id,template_name,template_path,template_id,field_values,print_time,status,printer,copies,operator,reprint_reason,template_version,diagnostic_details,order_name";
        private const string TemplateIdHeader = "record_id,template_name,template_path,template_id,field_values,print_time,status,printer,copies";
        private const string PreviousHeader = "record_id,template_name,template_path,field_values,print_time,status,printer,copies";
        public List<PrintRecord> Records { get; private set; }

        public HistoryManager()
            : this(AppPaths.RecordsFile, AppPaths.RecordsJsonlFile, AppPaths.RecordsSqliteFile, true)
        {
        }

        public HistoryManager(string recordsFile, string recordsJsonlFile = null)
            : this(recordsFile, recordsJsonlFile, null, false)
        {
        }

        public HistoryManager(string recordsFile, string recordsJsonlFile, string recordsSqliteFile)
            : this(recordsFile, recordsJsonlFile, recordsSqliteFile, false)
        {
        }

        private HistoryManager(string recordsFile, string recordsJsonlFile, string recordsSqliteFile, bool initializePaths)
        {
            if (initializePaths) AppPaths.Initialize();
            _recordsFile = recordsFile;
            _recordsJsonlFile = string.IsNullOrWhiteSpace(recordsJsonlFile) ? Path.ChangeExtension(recordsFile, ".jsonl") : recordsJsonlFile;
            _recordsSqliteFile = string.IsNullOrWhiteSpace(recordsSqliteFile) ? Path.ChangeExtension(recordsFile, ".db") : recordsSqliteFile;
            Records = new List<PrintRecord>();
        }

        public void Load()
        {
            Records.Clear();
            _templateValueIndexes.Clear();
            EnsureDatabase();
            if (LoadSqlite()) return;
            if (File.Exists(_recordsJsonlFile))
            {
                LoadJsonl();
                if (Records.Count > 0 || !File.Exists(_recordsFile)) return;
                LoggerService.Warn("JSONL 历史为空，回退读取 CSV 历史。");
            }
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
                if (Records.Count > 0) Save();
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

        private void LoadJsonl()
        {
            try
            {
                var badLines = new List<string>();
                foreach (var line in File.ReadLines(_recordsJsonlFile, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        var record = JsonSerializer.Deserialize<PrintRecord>(line);
                        if (record == null) continue;
                        record.FieldValues ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        record.TemplateFields ??= new List<string>();
                        if (!IsChecksumValid(record))
                        {
                            badLines.Add(line);
                            LoggerService.Warn($"跳过校验失败的历史记录: {record.RecordId}");
                            continue;
                        }
                        Records.Add(record);
                        IndexRecord(record);
                    }
                    catch (Exception ex)
                    {
                        badLines.Add(line);
                        LoggerService.Warn($"跳过损坏 JSONL 历史行: {ex.Message}");
                    }
                }
                if (badLines.Count > 0)
                    AtomicFileWriter.WriteAllLines(_recordsJsonlFile + ".bad", badLines, Encoding.UTF8);
                _usesCurrentFormat = true;
            }
            catch (Exception ex)
            {
                LoggerService.Error("加载 JSONL 历史记录失败", ex);
            }
        }

        public bool Save()
        {
            var tempFile = _recordsFile + ".tmp";
            try
            {
                foreach (var record in Records)
                    if (string.IsNullOrWhiteSpace(record.RecordChecksum)) StampChecksum(record);
                SaveSqlite(Records);
                var lines = Records.Select(record => JsonSerializer.Serialize(record));
                AtomicFileWriter.WriteAllLines(_recordsJsonlFile, lines, Encoding.UTF8);
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
            StampChecksum(record);
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
            string status, string printer, int copies, string operatorName = "", string reprintReason = "", string templateVersion = "", string diagnosticDetails = "", string orderName = "", string orderId = "", List<string> templateFields = null)
        {
            var record = new PrintRecord(templateName, templatePath, templateId, fieldValues,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), status, printer, copies)
            {
                OperatorName = operatorName ?? "",
                ReprintReason = reprintReason ?? "",
                TemplateVersion = templateVersion ?? "",
                DiagnosticDetails = diagnosticDetails ?? "",
                OrderName = orderName ?? "",
                OrderId = orderId ?? "",
                TemplateFields = templateFields ?? new List<string>()
            };
            StampChecksum(record);
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
            return Search(templateName, templatePath, templateId, keyword, exact, limit, newestFirst, offset, "", "", "", "");
        }

        public List<PrintRecord> Search(string templateName, string templatePath, string templateId, string keyword, bool exact, int limit, bool newestFirst, int offset, string status, string datePrefix, string printer, string orderQuery)
        {
            var query = (keyword ?? "").Trim();
            var source = newestFirst ? Records.AsEnumerable().Reverse() : Records.AsEnumerable();
            var matches = new List<PrintRecord>();
            var skipped = 0;
            foreach (var record in source)
            {
                if (!IsTemplateMatch(record, templateName, templatePath, templateId)) continue;
                if (!string.IsNullOrWhiteSpace(status) && !string.Equals(record.Status, status, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrWhiteSpace(datePrefix) && !(record.PrintTime ?? "").StartsWith(datePrefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrWhiteSpace(printer) && !string.Equals(record.Printer, printer, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrWhiteSpace(orderQuery) && (record.OrderName ?? "").IndexOf(orderQuery, StringComparison.OrdinalIgnoreCase) < 0 && !string.Equals(record.OrderId, orderQuery, StringComparison.OrdinalIgnoreCase)) continue;
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
            sb.AppendLine("template_name,template_path,template_id,order_id,order_name,template_fields,field_values,print_time,status,printer,copies,operator,reprint_reason,template_version,diagnostic_details");
            foreach (var r in filtered)
            {
                var values = string.Join("; ", (r.FieldValues ?? new Dictionary<string, string>()).Select(item => $"{item.Key}={item.Value}"));
                if (string.IsNullOrEmpty(values)) values = r.Imei;
                sb.AppendLine(string.Join(",", new[] { Csv(r.TemplateName), Csv(r.TemplatePath), Csv(r.TemplateId), Csv(r.OrderId), Csv(r.OrderName), Csv(string.Join(";", r.TemplateFields ?? new List<string>())), Csv(values), Csv(r.PrintTime), Csv(r.Status), Csv(r.Printer), r.Copies.ToString(), Csv(r.OperatorName), Csv(r.ReprintReason), Csv(r.TemplateVersion), Csv(r.DiagnosticDetails) }));
            }
                AtomicFileWriter.WriteAllText(path, sb.ToString(), Encoding.UTF8);
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
                InsertSqlite(record);
                File.AppendAllText(_recordsJsonlFile, JsonSerializer.Serialize(record) + Environment.NewLine, Encoding.UTF8);
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

        private void EnsureDatabase()
        {
            var directory = Path.GetDirectoryName(_recordsSqliteFile);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);
            using (var connection = OpenConnection())
            {
                ExecuteNonQuery(connection, "CREATE TABLE IF NOT EXISTS PrintRecords (RecordId TEXT PRIMARY KEY, OrderId TEXT, TemplateId TEXT, TemplateName TEXT, TemplatePath TEXT, PrintTime TEXT, Status TEXT, Printer TEXT, Copies INTEGER, OperatorName TEXT, ReprintReason TEXT, TemplateVersion TEXT, DiagnosticDetails TEXT, OrderName TEXT, RecordChecksum TEXT, Json TEXT NOT NULL)");
                ExecuteNonQuery(connection, "CREATE TABLE IF NOT EXISTS FieldValues (RecordId TEXT NOT NULL, FieldName TEXT NOT NULL, FieldValue TEXT, TemplateId TEXT, OrderId TEXT)");
                ExecuteNonQuery(connection, "CREATE TABLE IF NOT EXISTS TemplateSnapshots (TemplateId TEXT, RecordId TEXT, FieldName TEXT)");
                ExecuteNonQuery(connection, "CREATE TABLE IF NOT EXISTS Orders (OrderId TEXT PRIMARY KEY, OrderName TEXT)");
                ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS IX_PrintRecords_OrderId ON PrintRecords(OrderId)");
                ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS IX_PrintRecords_TemplateId ON PrintRecords(TemplateId)");
                ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS IX_PrintRecords_PrintTime ON PrintRecords(PrintTime)");
                ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS IX_PrintRecords_Status ON PrintRecords(Status)");
                ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS IX_FieldValues_Value ON FieldValues(FieldValue)");
            }
        }

        private bool LoadSqlite()
        {
            using (var connection = OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT Json FROM PrintRecords ORDER BY PrintTime";
                using (var reader = command.ExecuteReader())
                {
                    var loaded = false;
                    while (reader.Read())
                    {
                        var record = JsonSerializer.Deserialize<PrintRecord>(reader.GetString(0));
                        if (record == null || !IsChecksumValid(record)) continue;
                        record.FieldValues ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        record.TemplateFields ??= new List<string>();
                        Records.Add(record);
                        IndexRecord(record);
                        loaded = true;
                    }
                    _usesCurrentFormat = true;
                    return loaded;
                }
            }
        }

        private void SaveSqlite(IEnumerable<PrintRecord> records)
        {
            EnsureDatabase();
            using (var connection = OpenConnection())
            using (var transaction = connection.BeginTransaction())
            {
                ExecuteNonQuery(connection, "DELETE FROM PrintRecords", transaction);
                ExecuteNonQuery(connection, "DELETE FROM FieldValues", transaction);
                ExecuteNonQuery(connection, "DELETE FROM TemplateSnapshots", transaction);
                ExecuteNonQuery(connection, "DELETE FROM Orders", transaction);
                foreach (var record in records) InsertSqlite(record, connection, transaction);
                transaction.Commit();
            }
        }

        private void InsertSqlite(PrintRecord record)
        {
            EnsureDatabase();
            using (var connection = OpenConnection())
            using (var transaction = connection.BeginTransaction())
            {
                InsertSqlite(record, connection, transaction);
                transaction.Commit();
            }
        }

        private void InsertSqlite(PrintRecord record, SqliteConnection connection, SqliteTransaction transaction)
        {
            StampChecksum(record);
            ExecuteNonQuery(connection, "INSERT OR REPLACE INTO PrintRecords (RecordId, OrderId, TemplateId, TemplateName, TemplatePath, PrintTime, Status, Printer, Copies, OperatorName, ReprintReason, TemplateVersion, DiagnosticDetails, OrderName, RecordChecksum, Json) VALUES ($RecordId,$OrderId,$TemplateId,$TemplateName,$TemplatePath,$PrintTime,$Status,$Printer,$Copies,$OperatorName,$ReprintReason,$TemplateVersion,$DiagnosticDetails,$OrderName,$RecordChecksum,$Json)", transaction,
                ("$RecordId", record.RecordId), ("$OrderId", record.OrderId), ("$TemplateId", record.TemplateId), ("$TemplateName", record.TemplateName), ("$TemplatePath", record.TemplatePath), ("$PrintTime", record.PrintTime), ("$Status", record.Status), ("$Printer", record.Printer), ("$Copies", record.Copies), ("$OperatorName", record.OperatorName), ("$ReprintReason", record.ReprintReason), ("$TemplateVersion", record.TemplateVersion), ("$DiagnosticDetails", record.DiagnosticDetails), ("$OrderName", record.OrderName), ("$RecordChecksum", record.RecordChecksum), ("$Json", JsonSerializer.Serialize(record)));
            ExecuteNonQuery(connection, "INSERT OR REPLACE INTO Orders (OrderId, OrderName) VALUES ($OrderId,$OrderName)", transaction, ("$OrderId", record.OrderId), ("$OrderName", record.OrderName));
            foreach (var item in record.FieldValues ?? new Dictionary<string, string>())
                ExecuteNonQuery(connection, "INSERT INTO FieldValues (RecordId, FieldName, FieldValue, TemplateId, OrderId) VALUES ($RecordId,$FieldName,$FieldValue,$TemplateId,$OrderId)", transaction, ("$RecordId", record.RecordId), ("$FieldName", item.Key), ("$FieldValue", item.Value), ("$TemplateId", record.TemplateId), ("$OrderId", record.OrderId));
            foreach (var field in record.TemplateFields ?? new List<string>())
                ExecuteNonQuery(connection, "INSERT INTO TemplateSnapshots (TemplateId, RecordId, FieldName) VALUES ($TemplateId,$RecordId,$FieldName)", transaction, ("$TemplateId", record.TemplateId), ("$RecordId", record.RecordId), ("$FieldName", field));
        }

        private SqliteConnection OpenConnection()
        {
            var connection = new SqliteConnection($"Data Source={_recordsSqliteFile}");
            connection.Open();
            return connection;
        }

        private static void ExecuteNonQuery(SqliteConnection connection, string sql, SqliteTransaction transaction = null, params (string Name, object Value)[] parameters)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                command.Transaction = transaction;
                foreach (var parameter in parameters)
                    command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? "");
                command.ExecuteNonQuery();
            }
        }

        private static void StampChecksum(PrintRecord record)
        {
            record.RecordChecksum = ComputeChecksum(record);
        }

        private static bool IsChecksumValid(PrintRecord record)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.RecordChecksum)) return true;
            return string.Equals(record.RecordChecksum, ComputeChecksum(record), StringComparison.OrdinalIgnoreCase);
        }

        private static string ComputeChecksum(PrintRecord record)
        {
            var payload = string.Join("|", new[]
            {
                record.RecordId, record.TemplateName, record.TemplatePath, record.TemplateId, record.OrderId,
                SerializeFields(record.FieldValues), record.PrintTime, record.Status, record.Printer, record.Copies.ToString(),
                record.OperatorName, record.ReprintReason, record.TemplateVersion, record.DiagnosticDetails, record.OrderName,
                string.Join(";", record.TemplateFields ?? new List<string>())
            });
            using (var sha = SHA256.Create())
                return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(payload)));
        }
    }
}
