using System.Collections.Generic;
using System.Linq;

namespace BarTenderPrinter
{
    public static class ValidationService
    {
        public static int GetExpectedLength(DataSourceItem source, bool lengthValidationEnabled, int globalExpectedLength)
        {
            if (!lengthValidationEnabled || source == null) return 0;
            return source.ExpectedLength > 0 ? source.ExpectedLength : globalExpectedLength;
        }

        public static List<string> FindLocalDataMismatches(Dictionary<string, string> fieldValues, HashSet<string> localData, string targetField)
        {
            var result = new List<string>();
            if (fieldValues == null || localData == null || localData.Count == 0) return result;
            foreach (var item in fieldValues)
            {
                if (!string.IsNullOrWhiteSpace(targetField) && !string.Equals(item.Key, targetField, System.StringComparison.OrdinalIgnoreCase)) continue;
                if (string.IsNullOrEmpty(item.Value)) continue;
                if (!localData.Contains(item.Value)) result.Add($"{item.Key}={item.Value}");
            }
            return result;
        }

        public static List<string> FindTemplateFieldIssues(IEnumerable<string> templateFields, IEnumerable<DataSourceItem> configuredSources, Dictionary<string, string> fieldValues)
        {
            var fields = (templateFields ?? new List<string>()).ToList();
            var configured = (configuredSources ?? new List<DataSourceItem>()).ToList();
            fieldValues ??= new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
            var configuredFields = new HashSet<string>(configured.Select(source => source.Field).Where(field => !string.IsNullOrWhiteSpace(field)), System.StringComparer.OrdinalIgnoreCase);
            var enabledFields = new HashSet<string>(configured.Where(source => source.Enabled).Select(source => source.Field).Where(field => !string.IsNullOrWhiteSpace(field)), System.StringComparer.OrdinalIgnoreCase);
            var valueFields = new HashSet<string>(fieldValues.Keys, System.StringComparer.OrdinalIgnoreCase);
            var issues = new List<string>();
            var missingConfig = fields.Where(field => !configuredFields.Contains(field)).ToList();
            var disabledFields = fields.Where(field => configuredFields.Contains(field) && !enabledFields.Contains(field)).ToList();
            var missingValues = fields.Where(field => !valueFields.Contains(field) || !fieldValues.TryGetValue(field, out var value) || string.IsNullOrWhiteSpace(value)).ToList();
            var extraEnabled = enabledFields.Where(field => !fields.Contains(field, System.StringComparer.OrdinalIgnoreCase)).ToList();
            if (missingConfig.Count > 0) issues.Add($"模板字段未配置: {string.Join(", ", missingConfig)}");
            if (disabledFields.Count > 0) issues.Add($"模板字段已配置但未启用: {string.Join(", ", disabledFields)}");
            if (missingValues.Count > 0) issues.Add($"模板字段缺少打印值: {string.Join(", ", missingValues)}");
            if (extraEnabled.Count > 0) issues.Add($"配置中存在模板没有的字段: {string.Join(", ", extraEnabled)}");
            return issues;
        }
    }
}
