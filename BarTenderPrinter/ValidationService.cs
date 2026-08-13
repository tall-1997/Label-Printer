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

        public static List<string> FindLocalDataMismatches(Dictionary<string, string> fieldValues, HashSet<string> localData, IEnumerable<DataSourceItem> sources)
        {
            var selectedFields = new HashSet<string>((sources ?? new List<DataSourceItem>())
                .Where(source => source.Enabled && source.UseLocalDataValidation)
                .Select(source => source.Field)
                .Where(field => !string.IsNullOrWhiteSpace(field)), System.StringComparer.OrdinalIgnoreCase);
            if (selectedFields.Count == 0) return new List<string>();
            return FindLocalDataMismatches(fieldValues, localData, selectedFields);
        }

        public static List<string> FindLocalDataMismatches(Dictionary<string, string> fieldValues, HashSet<string> localData, ISet<string> selectedFields)
        {
            var result = new List<string>();
            if (fieldValues == null || localData == null || localData.Count == 0 || selectedFields == null) return result;
            foreach (var item in fieldValues)
            {
                if (!selectedFields.Contains(item.Key) || string.IsNullOrEmpty(item.Value)) continue;
                if (!localData.Contains(item.Value)) result.Add($"{item.Key}={item.Value}");
            }
            return result;
        }

        public static void MigrateLocalDataSelection(TemplateSettings settings)
        {
            if (settings == null) return;
            settings.DataSources ??= new List<DataSourceItem>();
            if (settings.SchemaVersion >= 3) return;
            var hasLocalData = !string.IsNullOrWhiteSpace(settings.LocalDataStoragePath) ||
                (settings.LocalData?.Count ?? 0) > 0 || !string.IsNullOrWhiteSpace(settings.LocalDataPath);
            foreach (var source in settings.DataSources)
            {
                source.UseLocalDataValidation = hasLocalData && source.Enabled &&
                    (string.IsNullOrWhiteSpace(settings.LocalDataTargetField) ||
                     string.Equals(source.Field, settings.LocalDataTargetField, System.StringComparison.OrdinalIgnoreCase));
            }
            settings.SchemaVersion = 3;
        }

        public static List<string> FindTemplateFieldIssues(IEnumerable<string> templateFields, IEnumerable<DataSourceItem> configuredSources, Dictionary<string, string> fieldValues)
        {
            var fields = (templateFields ?? new List<string>()).ToList();
            var configured = (configuredSources ?? new List<DataSourceItem>()).ToList();
            fieldValues ??= new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
            var enabledFields = new HashSet<string>(configured.Where(source => source.Enabled).Select(source => source.Field).Where(field => !string.IsNullOrWhiteSpace(field)), System.StringComparer.OrdinalIgnoreCase);
            var valueFields = new HashSet<string>(fieldValues.Keys, System.StringComparer.OrdinalIgnoreCase);
            var issues = new List<string>();
            var missingConfig = enabledFields.Where(field => !fields.Contains(field, System.StringComparer.OrdinalIgnoreCase)).ToList();
            var missingValues = enabledFields.Where(field => !valueFields.Contains(field) || !fieldValues.TryGetValue(field, out var value) || string.IsNullOrWhiteSpace(value)).ToList();
            if (missingConfig.Count > 0) issues.Add($"启用数据源不存在于模板: {string.Join(", ", missingConfig)}");
            if (missingValues.Count > 0) issues.Add($"模板字段缺少打印值: {string.Join(", ", missingValues)}");
            return issues;
        }
    }
}
