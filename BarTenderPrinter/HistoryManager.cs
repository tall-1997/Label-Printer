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
        public int SchemaVersion { get; set; } = 5;
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
        public bool IsExcluded { get; set; }
        public string ExcludedAtUtc { get; set; }
        public string ExcludedBy { get; set; }
        public string ExclusionReason { get; set; }
        public string ExclusionBatchId { get; set; }
        public string JobId { get; set; }
        public string IdempotencyKey { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("BatchId")]
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string LegacyV4BatchId { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("BatchItemId")]
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string LegacyV4BatchItemId { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("LabelType")]
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
        public int LegacyLabelType { get; set; }
        public string OriginalJobId { get; set; }
        public string ApprovalId { get; set; }
        public int ReprintSequence { get; set; }

        public PrintRecord()
        {
            SchemaVersion = 5;
            RecordId = Guid.NewGuid().ToString("N");
            Imei = "";
            TemplateName = "";
            TemplatePath = "";
            TemplateId = "";
            OrderId = "";
            FieldValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            PrintTime = "";
            Status = "UNCERTAIN";
            Printer = "";
            Copies = 1;
            OperatorName = "";
            ReprintReason = "";
            TemplateVersion = "";
            DiagnosticDetails = "";
            OrderName = "";
            TemplateFields = new List<string>();
            RecordChecksum = "";
            ExcludedAtUtc = "";
            ExcludedBy = "";
            ExclusionReason = "";
            ExclusionBatchId = "";
            JobId = "";
            IdempotencyKey = "";
            OriginalJobId = "";
            ApprovalId = "";
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
            Status = string.IsNullOrWhiteSpace(status) ? "UNCERTAIN" : status;
            Printer = "";
            Copies = 1;
            OperatorName = "";
            ReprintReason = "";
            TemplateVersion = "";
            DiagnosticDetails = "";
            OrderName = "";
            TemplateFields = new List<string>();
            RecordChecksum = "";
            JobId = "";
            IdempotencyKey = "";
            OriginalJobId = "";
            ApprovalId = "";
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
            Status = string.IsNullOrWhiteSpace(status) ? "UNCERTAIN" : status;
            Printer = printer ?? "";
            Copies = Math.Max(1, copies);
            OperatorName = "";
            ReprintReason = "";
            TemplateVersion = "";
            DiagnosticDetails = "";
            OrderName = "";
            TemplateFields = new List<string>();
            RecordChecksum = "";
            JobId = "";
            IdempotencyKey = "";
            OriginalJobId = "";
            ApprovalId = "";
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
        private readonly string _historyRecordsDirectory;
        private readonly object _sync = new object();
        private static readonly object IntegrityKeySync = new object();
        private static byte[] _integrityKey;
        private readonly List<PrintRecord> _records = new List<PrintRecord>();
        private readonly Dictionary<string, HashSet<string>> _templateValueIndexes = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        private bool _usesCurrentFormat;
        private const string Header = "record_id,template_name,template_path,template_id,field_values,print_time,status,printer,copies,operator,reprint_reason,template_version,diagnostic_details,order_name";
        private const string TemplateIdHeader = "record_id,template_name,template_path,template_id,field_values,print_time,status,printer,copies";
        private const string PreviousHeader = "record_id,template_name,template_path,field_values,print_time,status,printer,copies";
        public IReadOnlyList<PrintRecord> Records
        {
            get
            {
                lock (_sync) return _records.ToList().AsReadOnly();
            }
        }

        public HistoryManager()
            : this(AppPaths.RecordsFile, AppPaths.RecordsJsonlFile, AppPaths.RecordsSqliteFile, AppPaths.HistoryRecordsDirectory, true)
        {
        }

        public HistoryManager(string recordsFile, string recordsJsonlFile = null)
            : this(recordsFile, recordsJsonlFile, null, null, false)
        {
        }

        public HistoryManager(string recordsFile, string recordsJsonlFile, string recordsSqliteFile)
            : this(recordsFile, recordsJsonlFile, recordsSqliteFile, null, false)
        {
        }

        public HistoryManager(string recordsFile, string recordsJsonlFile, string recordsSqliteFile, string historyRecordsDirectory)
            : this(recordsFile, recordsJsonlFile, recordsSqliteFile, historyRecordsDirectory, false)
        {
        }

        private HistoryManager(string recordsFile, string recordsJsonlFile, string recordsSqliteFile, string historyRecordsDirectory, bool initializePaths)
        {
            if (initializePaths) AppPaths.Initialize();
            _recordsFile = recordsFile;
            _recordsJsonlFile = string.IsNullOrWhiteSpace(recordsJsonlFile) ? Path.ChangeExtension(recordsFile, ".jsonl") : recordsJsonlFile;
            _recordsSqliteFile = string.IsNullOrWhiteSpace(recordsSqliteFile) ? Path.ChangeExtension(recordsFile, ".db") : recordsSqliteFile;
            _historyRecordsDirectory = string.IsNullOrWhiteSpace(historyRecordsDirectory)
                ? Path.Combine(Path.GetDirectoryName(_recordsSqliteFile) ?? AppContext.BaseDirectory, "history-records")
                : historyRecordsDirectory;
        }

        public void Load()
        {
            lock (_sync) LoadCore();
        }

        private void LoadCore()
        {
            _records.Clear();
            _templateValueIndexes.Clear();
            EnsureDatabase();
            var sqliteResult = LoadSqlite();
            if (sqliteResult.RejectedCount > 0)
            {
                RecoverRejectedSqliteRecords(sqliteResult.RejectedRecordIds);
                EnsureRecordArchives();
                return;
            }
            if (sqliteResult.LoadedCount > 0) { EnsureRecordArchives(); SyncJsonlMirror(); return; }
            if (IsLegacyMigrationComplete()) return;
            if (File.Exists(_recordsJsonlFile))
            {
                LoadJsonl();
                if (Records.Count > 0 || !File.Exists(_recordsFile)) { Save(); EnsureRecordArchives(); return; }
                LoggerService.Warn("JSONL 历史为空，回退读取 CSV 历史。");
            }
            if (!File.Exists(_recordsFile)) { Save(); return; }
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
                Save();
                EnsureRecordArchives();
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
                _records.Add(record);
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
                _records.Add(record);
                IndexRecord(record);
            }
            else if (!usesPreviousFormat && parts.Count >= 3)
            {
                var record = new PrintRecord(parts[0], parts[1], parts[2]);
                _records.Add(record);
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
                        _records.Add(record);
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
            lock (_sync) return SaveCore();
        }

        private bool SaveCore()
        {
            var tempFile = _recordsFile + ".tmp";
            try
            {
                foreach (var record in Records)
                    if (string.IsNullOrWhiteSpace(record.RecordChecksum)) StampChecksum(record);
                SaveSqlite(Records);
                try
                {
                    var lines = Records.Select(record => JsonSerializer.Serialize(record));
                    AtomicFileWriter.WriteAllLines(_recordsJsonlFile, lines, Encoding.UTF8);
                }
                catch (Exception ex) { LoggerService.Error("同步 JSONL 历史兼容副本失败", ex); }
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
            lock (_sync)
            {
                var record = new PrintRecord(imei, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), status);
                StampChecksum(record);
                _records.Add(record);
                IndexRecord(record);
                if (!Append(record)) { _records.Remove(record); RebuildIndexes(); return false; }
                WriteRecordArchive(record);
                return true;
            }
        }

        public bool Add(string templateName, string templatePath, Dictionary<string, string> fieldValues,
            string status, string printer, int copies)
        {
            return Add(templateName, templatePath, "", fieldValues, status, printer, copies);
        }

        public bool Add(string templateName, string templatePath, string templateId, Dictionary<string, string> fieldValues,
            string status, string printer, int copies, string operatorName = "", string reprintReason = "", string templateVersion = "", string diagnosticDetails = "", string orderName = "", string orderId = "", List<string> templateFields = null)
        {
            return Add(new PrintHistoryEntry
            {
                TemplateName = templateName,
                TemplatePath = templatePath,
                TemplateId = templateId,
                FieldValues = fieldValues,
                Status = status,
                Printer = printer,
                Copies = copies,
                OperatorName = operatorName,
                ReprintReason = reprintReason,
                TemplateVersion = templateVersion,
                DiagnosticDetails = diagnosticDetails,
                OrderName = orderName,
                OrderId = orderId,
                TemplateFields = templateFields
            });
        }

        public bool Add(PrintHistoryEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            lock (_sync)
            {
                var fields = new Dictionary<string, string>(entry.FieldValues ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
                var record = new PrintRecord(entry.TemplateName, entry.TemplatePath, entry.TemplateId, fields,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), entry.Status, entry.Printer, entry.Copies)
                {
                    JobId = entry.JobId ?? "",
                    IdempotencyKey = entry.IdempotencyKey ?? "",
                    OriginalJobId = entry.OriginalJobId ?? "",
                    ApprovalId = entry.ApprovalId ?? "",
                    ReprintSequence = Math.Max(0, entry.ReprintSequence),
                    OperatorName = entry.OperatorName ?? "",
                    ReprintReason = entry.ReprintReason ?? "",
                    TemplateVersion = entry.TemplateVersion ?? "",
                    DiagnosticDetails = entry.DiagnosticDetails ?? "",
                    OrderName = entry.OrderName ?? "",
                    OrderId = entry.OrderId ?? "",
                    TemplateFields = new List<string>(entry.TemplateFields ?? Array.Empty<string>())
                };
                StampChecksum(record);
                _records.Add(record);
                IndexRecord(record);
                if (!Append(record)) { _records.Remove(record); RebuildIndexes(); return false; }
                WriteRecordArchive(record);
                return true;
            }
        }

        public bool IsPrinted(string imei)
        {
            lock (_sync) return _records.Any(r => !r.IsExcluded && r.Imei == imei);
        }

        public bool ContainsAnyValue(string value)
        {
            lock (_sync) return _records.Any(r => !r.IsExcluded && GetValues(r).Contains(value, StringComparer.OrdinalIgnoreCase));
        }

        public bool ContainsAnyValue(string templateName, string templatePath, string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            lock (_sync) return _templateValueIndexes.TryGetValue(GetTemplateKey(templateName, templatePath), out var values) && values.Contains(value);
        }

        public bool ContainsAnyValue(string templateName, string templatePath, string templateId, string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            lock (_sync)
            {
                if (!string.IsNullOrWhiteSpace(templateId) && _templateValueIndexes.TryGetValue(GetTemplateKey(templateId), out var values) && values.Contains(value)) return true;
                return _templateValueIndexes.TryGetValue(GetTemplateKey(templateName, templatePath), out values) && values.Contains(value);
            }
        }

        public PrintRecord GetById(string recordId)
        {
            lock (_sync) return _records.FirstOrDefault(record => !record.IsExcluded && string.Equals(record.RecordId, recordId, StringComparison.Ordinal));
        }

        public bool Delete(string recordId, string operatorName = "", string reason = "")
        {
            lock (_sync)
            {
                var record = _records.FirstOrDefault(item => !item.IsExcluded && string.Equals(item.RecordId, recordId, StringComparison.Ordinal));
                if (record == null) return false;
                var snapshot = CloneExclusion(record);
                ExcludeRecord(record, operatorName, reason, Guid.NewGuid().ToString("N"));
                RebuildIndexes();
                if (!SaveCore()) { RestoreExclusion(record, snapshot); RebuildIndexes(); return false; }
                return true;
            }
        }

        public bool Clear()
        {
            lock (_sync)
            {
                var active = _records.Where(record => !record.IsExcluded).ToList();
                if (active.Count == 0) return true;
                var snapshots = active.ToDictionary(record => record.RecordId, CloneExclusion);
                var batchId = Guid.NewGuid().ToString("N");
                foreach (var record in active) ExcludeRecord(record, "", "清空全部历史控件", batchId);
                RebuildIndexes();
                if (SaveCore()) return true;
                foreach (var record in active) RestoreExclusion(record, snapshots[record.RecordId]);
                RebuildIndexes();
                return false;
            }
        }

        public bool Clear(string templateName, string templatePath)
        {
            return Clear(templateName, templatePath, "");
        }

        public bool Clear(string templateName, string templatePath, string templateId, string operatorName = "", string reason = "")
        {
            lock (_sync)
            {
                var active = _records.Where(record => !record.IsExcluded && IsTemplateMatch(record, templateName, templatePath, templateId)).ToList();
                if (active.Count == 0) return true;
                var snapshots = active.ToDictionary(record => record.RecordId, CloneExclusion);
                var batchId = Guid.NewGuid().ToString("N");
                foreach (var record in active) ExcludeRecord(record, operatorName, reason, batchId);
                RebuildIndexes();
                if (SaveCore()) return true;
                foreach (var record in active) RestoreExclusion(record, snapshots[record.RecordId]);
                RebuildIndexes();
                return false;
            }
        }

        public List<PrintRecord> Search(string templateName, string templatePath, string keyword, bool exact)
        {
            return Search(templateName, templatePath, "", keyword, exact, 0, false);
        }

        public PrintRecord GetLatestSuccessful(string templateName, string templatePath, string templateId)
        {
            lock (_sync) return _records.AsEnumerable().Reverse().FirstOrDefault(record =>
                !record.IsExcluded && IsTemplateMatch(record, templateName, templatePath, templateId) && IsSuccessfulStatus(record.Status));
        }

        public List<PrintRecord> Search(string templateName, string templatePath, string templateId, string keyword, bool exact, int limit = 0, bool newestFirst = false, int offset = 0)
        {
            return Search(templateName, templatePath, templateId, keyword, exact, limit, newestFirst, offset, "", "", "", "");
        }

        public List<PrintRecord> Search(string templateName, string templatePath, string templateId, string keyword, bool exact, int limit, bool newestFirst, int offset, string status, string datePrefix, string printer, string orderQuery)
        {
            lock (_sync) return SearchCore(templateName, templatePath, templateId, keyword, exact, limit, newestFirst, offset, status, datePrefix, printer, orderQuery);
        }

        private List<PrintRecord> SearchCore(string templateName, string templatePath, string templateId, string keyword, bool exact, int limit, bool newestFirst, int offset, string status, string datePrefix, string printer, string orderQuery)
        {
            var query = (keyword ?? "").Trim();
            var source = newestFirst ? _records.AsEnumerable().Reverse() : _records.AsEnumerable();
            var matches = new List<PrintRecord>();
            var skipped = 0;
            foreach (var record in source)
            {
                if (record.IsExcluded) continue;
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
            lock (_sync) return _records.Count(record => !record.IsExcluded && IsTemplateMatch(record, templateName, templatePath, templateId));
        }

        public int TodayCount(string templateName, string templatePath, string templateId)
        {
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            lock (_sync) return _records.Count(record => !record.IsExcluded && IsTemplateMatch(record, templateName, templatePath, templateId) && record.PrintTime?.StartsWith(today) == true);
        }

        public void Export(string path, IEnumerable<PrintRecord> records)
        {
            var filtered = records?.ToList() ?? new List<PrintRecord>();

            var sb = new StringBuilder();
            sb.AppendLine("record_id,template_name,template_path,template_id,order_id,order_name,template_fields,field_values,print_time,status,printer,copies,operator,reprint_reason,template_version,diagnostic_details");
            foreach (var r in filtered)
            {
                var values = string.Join("; ", (r.FieldValues ?? new Dictionary<string, string>()).Select(item => $"{item.Key}={item.Value}"));
                if (string.IsNullOrEmpty(values)) values = r.Imei;
                sb.AppendLine(string.Join(",", new[] { Csv(r.RecordId), Csv(r.TemplateName), Csv(r.TemplatePath), Csv(r.TemplateId), Csv(r.OrderId), Csv(r.OrderName), Csv(string.Join(";", r.TemplateFields ?? new List<string>())), Csv(values), Csv(r.PrintTime), Csv(r.Status), Csv(r.Printer), r.Copies.ToString(), Csv(r.OperatorName), Csv(r.ReprintReason), Csv(r.TemplateVersion), Csv(r.DiagnosticDetails) }));
            }
                AtomicFileWriter.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        public int TodayCount()
        {
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            lock (_sync) return _records.Count(r => !r.IsExcluded && r.PrintTime?.StartsWith(today) == true);
        }

        public int TotalCount()
        {
            lock (_sync) return _records.Count(record => !record.IsExcluded);
        }

        private static List<string> ParseCsvLine(string line) => CsvUtils.ParseLine(line);

        private void IndexRecord(PrintRecord record)
        {
            if (record.IsExcluded || !ShouldReserveValues(record.Status)) return;
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

        private static bool ShouldReserveValues(string status)
        {
            return IsSuccessfulStatus(status) || status?.EndsWith("UNCERTAIN", StringComparison.OrdinalIgnoreCase) == true;
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
                try { File.AppendAllText(_recordsJsonlFile, JsonSerializer.Serialize(record) + Environment.NewLine, Encoding.UTF8); }
                catch (Exception ex) { LoggerService.Error("追加 JSONL 历史兼容副本失败", ex); }
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

        private sealed class ExclusionSnapshot
        {
            public int SchemaVersion { get; set; }
            public bool IsExcluded { get; set; }
            public string ExcludedAtUtc { get; set; }
            public string ExcludedBy { get; set; }
            public string ExclusionReason { get; set; }
            public string ExclusionBatchId { get; set; }
        }

        private static ExclusionSnapshot CloneExclusion(PrintRecord record)
        {
            return new ExclusionSnapshot
            {
                SchemaVersion = record.SchemaVersion,
                IsExcluded = record.IsExcluded,
                ExcludedAtUtc = record.ExcludedAtUtc,
                ExcludedBy = record.ExcludedBy,
                ExclusionReason = record.ExclusionReason,
                ExclusionBatchId = record.ExclusionBatchId
            };
        }

        private static void ExcludeRecord(PrintRecord record, string operatorName, string reason, string batchId)
        {
            record.SchemaVersion = Math.Max(3, record.SchemaVersion);
            record.IsExcluded = true;
            record.ExcludedAtUtc = DateTime.UtcNow.ToString("O");
            record.ExcludedBy = operatorName ?? "";
            record.ExclusionReason = reason ?? "";
            record.ExclusionBatchId = batchId ?? "";
            StampChecksum(record);
        }

        private static void RestoreExclusion(PrintRecord record, ExclusionSnapshot snapshot)
        {
            record.SchemaVersion = snapshot.SchemaVersion;
            record.IsExcluded = snapshot.IsExcluded;
            record.ExcludedAtUtc = snapshot.ExcludedAtUtc;
            record.ExcludedBy = snapshot.ExcludedBy;
            record.ExclusionReason = snapshot.ExclusionReason;
            record.ExclusionBatchId = snapshot.ExclusionBatchId;
            StampChecksum(record);
        }

        private void EnsureRecordArchives()
        {
            foreach (var record in Records)
                WriteRecordArchive(record);
        }

        private void SyncJsonlMirror()
        {
            try
            {
                var lines = Records.Select(record => JsonSerializer.Serialize(record));
                AtomicFileWriter.WriteAllLines(_recordsJsonlFile, lines, Encoding.UTF8);
            }
            catch (Exception ex) { LoggerService.Error("重建 JSONL 历史兼容副本失败", ex); }
        }

        private bool WriteRecordArchive(PrintRecord record)
        {
            try
            {
                var timestamp = ParsePrintTime(record.PrintTime);
                var directory = Path.Combine(_historyRecordsDirectory, timestamp.ToString("yyyy"), timestamp.ToString("MM"), timestamp.ToString("dd"));
                Directory.CreateDirectory(directory);
                var safeRecordId = new string((record.RecordId ?? "").Where(char.IsLetterOrDigit).ToArray());
                if (string.IsNullOrWhiteSpace(safeRecordId)) safeRecordId = Guid.NewGuid().ToString("N");
                var path = Path.Combine(directory, $"{timestamp:yyyyMMdd_HHmmss}_{safeRecordId}.json");
                if (File.Exists(path)) return true;
                AtomicFileWriter.WriteAllText(path, JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
                return true;
            }
            catch (Exception ex)
            {
                LoggerService.Error($"保存历史记录独立副本失败: {record?.RecordId}", ex);
                return false;
            }
        }

        private static DateTime ParsePrintTime(string printTime)
        {
            return DateTime.TryParse(printTime, out var parsed) ? parsed : DateTime.Now;
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
                ExecuteNonQuery(connection, "CREATE TABLE IF NOT EXISTS StorageMetadata (Key TEXT PRIMARY KEY, Value TEXT NOT NULL)");
                ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS IX_PrintRecords_OrderId ON PrintRecords(OrderId)");
                ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS IX_PrintRecords_TemplateId ON PrintRecords(TemplateId)");
                ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS IX_PrintRecords_PrintTime ON PrintRecords(PrintTime)");
                ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS IX_PrintRecords_Status ON PrintRecords(Status)");
                ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS IX_FieldValues_Value ON FieldValues(FieldValue)");
            }
        }

        private sealed class SqliteLoadResult
        {
            public int LoadedCount { get; set; }
            public HashSet<string> RejectedRecordIds { get; } = new HashSet<string>(StringComparer.Ordinal);
            public int RejectedCount => RejectedRecordIds.Count;
        }

        private SqliteLoadResult LoadSqlite()
        {
            var result = new SqliteLoadResult();
            using (var connection = OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT RecordId, Json FROM PrintRecords ORDER BY PrintTime";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var recordId = reader.IsDBNull(0) ? "<unknown>" : reader.GetString(0);
                        PrintRecord record;
                        try
                        {
                            record = JsonSerializer.Deserialize<PrintRecord>(reader.GetString(1));
                        }
                        catch (Exception ex)
                        {
                            LoggerService.Error($"跳过损坏的历史记录: {recordId}", ex);
                            result.RejectedRecordIds.Add(recordId);
                            continue;
                        }
                        if (record == null)
                        {
                            LoggerService.Warn($"跳过空历史记录: {recordId}");
                            result.RejectedRecordIds.Add(recordId);
                            continue;
                        }
                        if (!IsChecksumValid(record))
                        {
                            LoggerService.Warn($"跳过完整性校验失败的历史记录: {recordId}");
                            result.RejectedRecordIds.Add(recordId);
                            continue;
                        }
                        record.FieldValues ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        record.TemplateFields ??= new List<string>();
                        _records.Add(record);
                        IndexRecord(record);
                        result.LoadedCount++;
                    }
                    _usesCurrentFormat = true;
                    return result;
                }
            }
        }

        private void RecoverRejectedSqliteRecords(IReadOnlyCollection<string> rejectedRecordIds)
        {
            var sqliteRecords = _records.ToList();
            if (!File.Exists(_recordsJsonlFile))
            {
                LoggerService.Warn("SQLite 历史存在损坏记录，JSONL 恢复副本不存在，已保留可读取记录且不会覆盖镜像。");
                return;
            }

            _records.Clear();
            _templateValueIndexes.Clear();
            LoadJsonl();
            var recoveredIds = new HashSet<string>(_records.Select(record => record.RecordId ?? ""), StringComparer.Ordinal);
            if (rejectedRecordIds.Any(recordId => !recoveredIds.Contains(recordId)))
            {
                _records.Clear();
                _records.AddRange(sqliteRecords);
                RebuildIndexes();
                LoggerService.Warn("SQLite 历史存在损坏记录，JSONL 恢复副本不完整，已保留可读取记录且不会覆盖镜像。");
                return;
            }
            foreach (var record in sqliteRecords)
            {
                if (_records.Any(item => string.Equals(item.RecordId, record.RecordId, StringComparison.Ordinal))) continue;
                _records.Add(record);
                IndexRecord(record);
            }
            if (_records.Count == 0)
            {
                LoggerService.Warn("SQLite 与 JSONL 历史均无法恢复，历史镜像保持原样以供人工处理。");
                return;
            }
            if (SaveCore())
                LoggerService.Warn("SQLite 历史存在损坏记录，已使用 JSONL 恢复副本重建主存储。");
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
                ExecuteNonQuery(connection, "INSERT OR REPLACE INTO StorageMetadata (Key, Value) VALUES ('LegacyMigrationComplete', '1')", transaction);
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
            var builder = new SqliteConnectionStringBuilder { DataSource = _recordsSqliteFile };
            var connection = new SqliteConnection(builder.ToString());
            connection.Open();
            return connection;
        }

        private bool IsLegacyMigrationComplete()
        {
            using (var connection = OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT Value FROM StorageMetadata WHERE Key = 'LegacyMigrationComplete' LIMIT 1";
                return string.Equals(command.ExecuteScalar()?.ToString(), "1", StringComparison.Ordinal);
            }
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

        internal static void StampChecksum(PrintRecord record)
        {
            record.RecordChecksum = ComputeChecksum(record);
        }

        private static bool IsChecksumValid(PrintRecord record)
        {
            if (record == null) return false;
            if (string.IsNullOrWhiteSpace(record.RecordChecksum)) return record.SchemaVersion < 3;
            return string.Equals(record.RecordChecksum, ComputeChecksum(record, true, record.SchemaVersion == 4, record.SchemaVersion >= 5), StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(record.RecordChecksum, ComputeChecksum(record, false, record.SchemaVersion == 4, record.SchemaVersion >= 5), StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(record.RecordChecksum, ComputeLegacyChecksum(record, true), StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(record.RecordChecksum, ComputeLegacyChecksum(record, false), StringComparison.OrdinalIgnoreCase);
        }

        private static string ComputeChecksum(PrintRecord record)
        {
            return ComputeChecksum(record, record.SchemaVersion >= 3, false, record.SchemaVersion >= 5);
        }

        private static string ComputeChecksum(PrintRecord record, bool includeLifecycle, bool includeLegacyMesFields,
            bool includePrintJobFields)
        {
            var values = new List<string>
            {
                record.RecordId, record.TemplateName, record.TemplatePath, record.TemplateId, record.OrderId,
                SerializeFields(record.FieldValues), record.PrintTime, record.Status, record.Printer, record.Copies.ToString(),
                record.OperatorName, record.ReprintReason, record.TemplateVersion, record.DiagnosticDetails, record.OrderName,
                string.Join(";", record.TemplateFields ?? new List<string>())
            };
            if (includeLifecycle)
            {
                values.Add(record.IsExcluded.ToString());
                values.Add(record.ExcludedAtUtc ?? "");
                values.Add(record.ExcludedBy ?? "");
                values.Add(record.ExclusionReason ?? "");
                values.Add(record.ExclusionBatchId ?? "");
            }
            if (includeLegacyMesFields)
            {
                values.Add(record.JobId ?? "");
                values.Add(record.IdempotencyKey ?? "");
                values.Add(record.LegacyV4BatchId ?? "");
                values.Add(record.LegacyV4BatchItemId ?? "");
                values.Add(LegacyLabelTypeName(record.LegacyLabelType));
                values.Add(record.OriginalJobId ?? "");
                values.Add(record.ApprovalId ?? "");
                values.Add(record.ReprintSequence.ToString());
            }
            else if (includePrintJobFields)
            {
                values.Add(record.JobId ?? "");
                values.Add(record.IdempotencyKey ?? "");
                values.Add(record.OriginalJobId ?? "");
                values.Add(record.ApprovalId ?? "");
                values.Add(record.ReprintSequence.ToString());
            }
            var payload = string.Join("|", values);
            using (var hmac = new HMACSHA256(GetIntegrityKey()))
                return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
        }

        private static string LegacyLabelTypeName(int value) => value switch
        {
            1 => "Body",
            2 => "ColorBox",
            3 => "Carton",
            4 => "Pallet",
            _ => "Unspecified"
        };

        private static string ComputeLegacyChecksum(PrintRecord record, bool includeLifecycle)
        {
            var values = new List<string>
            {
                record.RecordId, record.TemplateName, record.TemplatePath, record.TemplateId, record.OrderId,
                SerializeFields(record.FieldValues), record.PrintTime, record.Status, record.Printer, record.Copies.ToString(),
                record.OperatorName, record.ReprintReason, record.TemplateVersion, record.DiagnosticDetails, record.OrderName,
                string.Join(";", record.TemplateFields ?? new List<string>())
            };
            if (includeLifecycle)
            {
                values.Add(record.IsExcluded.ToString());
                values.Add(record.ExcludedAtUtc ?? "");
                values.Add(record.ExcludedBy ?? "");
                values.Add(record.ExclusionReason ?? "");
                values.Add(record.ExclusionBatchId ?? "");
            }
            using (var sha = SHA256.Create())
                return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("|", values))));
        }

        private static byte[] GetIntegrityKey()
        {
            lock (IntegrityKeySync)
            {
                if (_integrityKey != null) return _integrityKey;
                var path = Path.Combine(AppPaths.DataDirectory, "history-integrity.key");
                if (File.Exists(path)) return _integrityKey = Convert.FromBase64String(File.ReadAllText(path).Trim());
                var key = RandomNumberGenerator.GetBytes(32);
                AtomicFileWriter.WriteAllText(path, Convert.ToBase64String(key), Encoding.UTF8);
                return _integrityKey = key;
            }
        }
    }
}
