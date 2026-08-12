using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BarTenderPrinter
{
    public class TemplateSettings
    {
        public string TemplateName { get; set; } = "";
        public string TemplatePath { get; set; } = "";
        public string Printer { get; set; } = "";
        public int Copies { get; set; } = 1;
        public bool InputValidation { get; set; }
        public bool DuplicateValidation { get; set; } = true;
        public bool LengthValidation { get; set; }
        public int GlobalExpectedLength { get; set; }
        public long GlobalLengthRevision { get; set; }
        public long LengthRevisionCounter { get; set; }
        public string LocalDataPath { get; set; } = "";
        public List<string> LocalData { get; set; } = new List<string>();
        public List<DataSourceItem> DataSources { get; set; } = new List<DataSourceItem>();
    }

    public class TemplateSettingsManager
    {
        private readonly string _path = AppPaths.TemplateSettingsFile;
        private readonly Dictionary<string, TemplateSettings> _settings = new Dictionary<string, TemplateSettings>(StringComparer.OrdinalIgnoreCase);

        public TemplateSettingsManager()
        {
            AppPaths.Initialize();
            Load();
        }

        public bool TryGet(string templateName, string templatePath, out TemplateSettings settings)
        {
            return _settings.TryGetValue(GetKey(templateName, templatePath), out settings);
        }

        public void Save(TemplateSettings settings)
        {
            var key = GetKey(settings.TemplateName, settings.TemplatePath);
            var snapshot = _settings.Values.Where(item => !string.Equals(GetKey(item.TemplateName, item.TemplatePath), key, StringComparison.OrdinalIgnoreCase)).ToList();
            snapshot.Add(settings);
            var tempPath = _path + ".tmp";
            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(tempPath, json);
            if (File.Exists(_path)) File.Copy(_path, _path + ".bak", true);
            File.Move(tempPath, _path, true);
            _settings[key] = settings;
        }

        private void Load()
        {
            if (!File.Exists(_path)) return;
            try
            {
                var items = JsonSerializer.Deserialize<List<TemplateSettings>>(File.ReadAllText(_path)) ?? new List<TemplateSettings>();
                foreach (var item in items) _settings[GetKey(item.TemplateName, item.TemplatePath)] = item;
            }
            catch (Exception ex)
            {
                LoggerService.Error("加载模板设置失败", ex);
                var backupPath = _path + ".bak";
                if (!File.Exists(backupPath)) return;
                try
                {
                    var items = JsonSerializer.Deserialize<List<TemplateSettings>>(File.ReadAllText(backupPath)) ?? new List<TemplateSettings>();
                    foreach (var item in items) _settings[GetKey(item.TemplateName, item.TemplatePath)] = item;
                }
                catch (Exception backupEx) { LoggerService.Error("加载模板设置备份失败", backupEx); }
            }
        }

        private static string GetKey(string templateName, string templatePath)
        {
            string normalizedPath;
            try { normalizedPath = Path.GetFullPath(templatePath ?? "").TrimEnd(Path.DirectorySeparatorChar); }
            catch { normalizedPath = (templatePath ?? "").Trim(); }
            return $"{templateName?.Trim()}|{normalizedPath}";
        }
    }
}
