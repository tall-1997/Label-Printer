using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace BarTenderPrinter
{
    public static class BusinessHistoryCsvExporter
    {
        private static readonly string[] FixedHeaders = { "日期", "客户", "颜色", "机型", "订单号" };
        private static readonly string[] TrailingHeaders = { "操作人", "打印时间", "打印状态" };

        public static IReadOnlyList<string> Export(
            string directory,
            IEnumerable<PrintRecord> records,
            IEnumerable<PackagingOrder> orders,
            IEnumerable<string> templateFields,
            DateTime exportDate,
            bool overwriteExisting = false)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("请选择导出目录。", nameof(directory));

            var fields = DistinctFields(templateFields);
            var orderGroups = (orders ?? Enumerable.Empty<PackagingOrder>())
                .Where(order => order != null && !string.IsNullOrWhiteSpace(order.OrderId))
                .GroupBy(order => order.OrderId, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var duplicateOrder = orderGroups.FirstOrDefault(group => group.Count() > 1);
            if (duplicateOrder != null) throw new InvalidOperationException($"订单标识重复，无法导出：{duplicateOrder.Key}");
            var orderLookup = orderGroups.ToDictionary(group => group.Key, group => group.Single(), StringComparer.OrdinalIgnoreCase);
            var groups = (records ?? Enumerable.Empty<PrintRecord>())
                .Where(record => record != null)
                .GroupBy(record => orderLookup.ContainsKey(record.OrderId ?? "") ? record.OrderId : "", StringComparer.OrdinalIgnoreCase)
                .ToList();

            var exports = groups.Select(group =>
            {
                orderLookup.TryGetValue(group.Key, out var order);
                var fileName = BuildFileName(order, exportDate);
                var path = Path.Combine(directory, fileName);
                return new { Records = group.AsEnumerable(), Order = order, Path = path };
            }).ToList();
            var duplicatePath = exports.GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
            if (duplicatePath != null) throw new InvalidOperationException($"多个订单生成了相同文件名：{Path.GetFileName(duplicatePath.Key)}");
            var existing = exports.FirstOrDefault(item => File.Exists(item.Path));
            if (existing != null && !overwriteExisting)
                throw new IOException($"导出文件已存在：{Path.GetFileName(existing.Path)}");

            Directory.CreateDirectory(directory);
            foreach (var item in exports)
                File.WriteAllText(item.Path, BuildCsv(item.Records, item.Order, fields), new UTF8Encoding(true));
            return exports.Select(item => item.Path).ToList();
        }

        internal static string BuildCsv(IEnumerable<PrintRecord> records, PackagingOrder order, IReadOnlyList<string> templateFields)
        {
            var headers = FixedHeaders.Concat(templateFields).Concat(TrailingHeaders);
            var builder = new StringBuilder();
            builder.AppendLine(string.Join(",", headers.Select(CsvUtils.Escape)));

            foreach (var record in records ?? Enumerable.Empty<PrintRecord>())
            {
                var values = new List<string>
                {
                    FormatTime(record.PrintTime, "yyyy-MM-dd"),
                    order?.Customer ?? "",
                    order?.Color ?? "",
                    order?.ProductModel ?? "",
                    order?.OrderNumber ?? ""
                };
                foreach (var field in templateFields)
                    values.Add(GetFieldValue(record.FieldValues, field));
                values.Add(record.OperatorName ?? "");
                values.Add(FormatTime(record.PrintTime, "yyyy/MM/dd HH:mm:ss"));
                values.Add(record.Status ?? "");
                builder.AppendLine(string.Join(",", values.Select(CsvUtils.Escape)));
            }
            return builder.ToString();
        }

        internal static IReadOnlyList<string> DistinctFields(IEnumerable<string> fields)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return (fields ?? Enumerable.Empty<string>())
                .Where(field => !string.IsNullOrWhiteSpace(field) && seen.Add(field))
                .ToList();
        }

        internal static string BuildFileName(PackagingOrder order, DateTime exportDate)
        {
            var parts = new[]
            {
                order?.Customer ?? "",
                order?.ProductModel ?? "",
                order?.Color ?? "",
                order?.OrderNumber ?? "",
                exportDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
            };
            return string.Join("_", parts.Select(SanitizeFileNamePart)) + ".csv";
        }

        private static string GetFieldValue(IReadOnlyDictionary<string, string> values, string field)
        {
            if (values == null) return "";
            if (values.TryGetValue(field, out var value)) return value ?? "";
            var match = values.FirstOrDefault(item => string.Equals(item.Key, field, StringComparison.OrdinalIgnoreCase));
            return string.IsNullOrEmpty(match.Key) ? "" : match.Value ?? "";
        }

        private static string FormatTime(string value, string format)
        {
            if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed) &&
                !DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out parsed))
                return "";
            return parsed.ToString(format, CultureInfo.InvariantCulture);
        }

        private static string SanitizeFileNamePart(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string((value ?? "").Trim().Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        }
    }
}
