using System;
using System.IO;
using System.Text.Json;

namespace BarTenderPrinter
{
    public sealed class ApplicationState
    {
        public int SchemaVersion { get; set; } = 1;
        public string ActiveOrderId { get; set; } = "";
        public string ActiveTemplateId { get; set; } = "";
        public string SelectedTemplatePath { get; set; } = "";
        public string Printer { get; set; } = "";
        public int Copies { get; set; } = 1;
        public bool PreviewEnabled { get; set; }
    }

    public sealed class ApplicationStateManager
    {
        private readonly string _path;

        public ApplicationStateManager() : this(AppPaths.ApplicationStateFile)
        {
        }

        public ApplicationStateManager(string path)
        {
            _path = path;
        }

        public ApplicationState Load()
        {
            if (!File.Exists(_path)) return CreateDefaultState();
            try
            {
                return JsonSerializer.Deserialize<ApplicationState>(File.ReadAllText(_path)) ?? new ApplicationState();
            }
            catch (Exception ex)
            {
                LoggerService.Warn($"加载最近使用状态失败: {ex.Message}");
                return CreateDefaultState();
            }
        }

        public void Save(ApplicationState state)
        {
            var json = JsonSerializer.Serialize(state ?? new ApplicationState(), new JsonSerializerOptions { WriteIndented = true });
            AtomicFileWriter.WriteAllText(_path, json);
        }

        private static ApplicationState CreateDefaultState()
        {
            return new ApplicationState { SchemaVersion = 0 };
        }
    }
}
