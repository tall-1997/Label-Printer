using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace BarTenderPrinter
{
    public class PackagingOrder
    {
        public string Customer { get; set; } = "";
        public string ProductModel { get; set; } = "";
        public string Color { get; set; } = "";
        public string OrderNumber { get; set; } = "";
        public string TemplatePath { get; set; } = "";
        public TemplateSettings Settings { get; set; } = new TemplateSettings();
        public List<OrderTemplate> Templates { get; set; } = new List<OrderTemplate>();

        public string DisplayName => $"{Customer} / {ProductModel} / {Color} / {OrderNumber}";
        public string Key => BuildKey(Customer, ProductModel, Color, OrderNumber);

        public static string BuildKey(string customer, string productModel, string color, string orderNumber)
        {
            return Normalize(orderNumber);
        }

        private static string Normalize(string value) => (value ?? "").Trim();
    }

    public class OrderTemplate
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string SourcePath { get; set; } = "";
        public string ArchivedPath { get; set; } = "";
        public long SourceLastWriteTimeUtcTicks { get; set; }
        public long SourceLength { get; set; }
        public string SourceSha256 { get; set; } = "";
        public TemplateSettings Settings { get; set; } = new TemplateSettings();

        public string DisplayName => !string.IsNullOrWhiteSpace(SourcePath) ? Path.GetFileName(SourcePath) : Path.GetFileName(ArchivedPath);
    }

    public enum TemplateUpdateStatus
    {
        Unchanged,
        Changed,
        CheckFailed
    }

    public class OrderManager
    {
        private readonly List<PackagingOrder> _orders = new List<PackagingOrder>();
        private readonly string _path = AppPaths.OrdersFile;

        public IReadOnlyList<PackagingOrder> Orders => _orders;

        public OrderManager()
        {
            AppPaths.Initialize();
            Load();
        }

        public bool Contains(string customer, string productModel, string color, string orderNumber)
        {
            var key = PackagingOrder.BuildKey(customer, productModel, color, orderNumber);
            return _orders.Any(order => string.Equals(order.Key, key, StringComparison.OrdinalIgnoreCase));
        }

        public void Add(PackagingOrder order)
        {
            var key = order.Key;
            _orders.RemoveAll(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
            _orders.Add(order);
            _orders.Sort((left, right) => string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase));
            Save();
        }

        public PackagingOrder Find(string customer, string productModel, string color, string orderNumber)
        {
            var key = PackagingOrder.BuildKey(customer, productModel, color, orderNumber);
            return _orders.FirstOrDefault(order => string.Equals(order.Key, key, StringComparison.OrdinalIgnoreCase));
        }

        public OrderTemplate ArchiveTemplate(string sourceTemplatePath, string customer, string productModel, string color, string orderNumber, string templateId = null)
        {
            if (string.IsNullOrEmpty(sourceTemplatePath) || !File.Exists(sourceTemplatePath))
                throw new FileNotFoundException("模板文件不存在", sourceTemplatePath);

            var orderDir = Path.Combine(AppPaths.OrdersDirectory, MakeSafeFolderName(PackagingOrder.BuildKey(customer, productModel, color, orderNumber)));
            Directory.CreateDirectory(orderDir);
            var id = string.IsNullOrWhiteSpace(templateId) ? Guid.NewGuid().ToString("N") : templateId;
            var targetPath = Path.Combine(orderDir, $"{id}_{Path.GetFileName(sourceTemplatePath)}");
            File.Copy(sourceTemplatePath, targetPath, true);
            var sourceInfo = new FileInfo(sourceTemplatePath);
            return new OrderTemplate
            {
                Id = id,
                SourcePath = Path.GetFullPath(sourceTemplatePath),
                ArchivedPath = targetPath,
                SourceLastWriteTimeUtcTicks = sourceInfo.LastWriteTimeUtc.Ticks,
                SourceLength = sourceInfo.Length,
                SourceSha256 = ComputeSha256(sourceTemplatePath)
            };
        }

        public TemplateUpdateStatus GetSourceUpdateStatus(OrderTemplate template)
        {
            if (template == null || string.IsNullOrWhiteSpace(template.SourcePath)) return TemplateUpdateStatus.Unchanged;
            if (!File.Exists(template.SourcePath)) return TemplateUpdateStatus.CheckFailed;
            try
            {
                var sourceInfo = new FileInfo(template.SourcePath);
                var changed = sourceInfo.LastWriteTimeUtc.Ticks != template.SourceLastWriteTimeUtcTicks ||
                              sourceInfo.Length != template.SourceLength ||
                              !string.Equals(ComputeSha256(template.SourcePath), template.SourceSha256, StringComparison.OrdinalIgnoreCase);
                return changed ? TemplateUpdateStatus.Changed : TemplateUpdateStatus.Unchanged;
            }
            catch (IOException ex)
            {
                LoggerService.Warn($"检查外部模板更新失败: {ex.Message}");
                return TemplateUpdateStatus.CheckFailed;
            }
            catch (UnauthorizedAccessException ex)
            {
                LoggerService.Warn($"检查外部模板更新失败: {ex.Message}");
                return TemplateUpdateStatus.CheckFailed;
            }
        }

        public void UseSourceTemplate(PackagingOrder order, OrderTemplate template)
        {
            if (order == null || template == null || !File.Exists(template.SourcePath))
                throw new FileNotFoundException("外部模板文件不存在", template?.SourcePath);
            Directory.CreateDirectory(Path.GetDirectoryName(template.ArchivedPath) ?? AppPaths.OrdersDirectory);
            File.Copy(template.SourcePath, template.ArchivedPath, true);
            UpdateSourceSnapshot(template);
            template.Settings.TemplateName = Path.GetFileName(template.ArchivedPath);
            template.Settings.TemplatePath = template.ArchivedPath;
            Save();
        }

        public void KeepArchivedTemplate(OrderTemplate template)
        {
            if (template == null) return;
            UpdateSourceSnapshot(template);
            Save();
        }

        private void Load()
        {
            if (!File.Exists(_path)) return;
            try
            {
                var items = JsonSerializer.Deserialize<List<PackagingOrder>>(File.ReadAllText(_path)) ?? new List<PackagingOrder>();
                _orders.Clear();
                var migrated = false;
                foreach (var item in items.Where(item => item != null))
                {
                    item.Templates ??= new List<OrderTemplate>();
                    if (item.Templates.Count == 0 && !string.IsNullOrWhiteSpace(item.TemplatePath))
                    {
                        var template = new OrderTemplate
                        {
                            SourcePath = "",
                            ArchivedPath = item.TemplatePath,
                            Settings = item.Settings ?? new TemplateSettings()
                        };
                        item.Templates.Add(template);
                        item.TemplatePath = "";
                        item.Settings = new TemplateSettings();
                        migrated = true;
                    }
                    foreach (var template in item.Templates)
                    {
                        template.Id = string.IsNullOrWhiteSpace(template.Id) ? Guid.NewGuid().ToString("N") : template.Id;
                        template.Settings ??= new TemplateSettings();
                    }
                    _orders.Add(item);
                }
                if (migrated) Save();
            }
            catch (Exception ex)
            {
                LoggerService.Error("加载订单失败", ex);
            }
        }

        private void Save()
        {
            var tempPath = _path + ".tmp";
            var json = JsonSerializer.Serialize(_orders, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(tempPath, json);
            if (File.Exists(_path)) File.Copy(_path, _path + ".bak", true);
            File.Move(tempPath, _path, true);
        }

        private static void UpdateSourceSnapshot(OrderTemplate template)
        {
            if (string.IsNullOrWhiteSpace(template.SourcePath) || !File.Exists(template.SourcePath)) return;
            var sourceInfo = new FileInfo(template.SourcePath);
            template.SourceLastWriteTimeUtcTicks = sourceInfo.LastWriteTimeUtc.Ticks;
            template.SourceLength = sourceInfo.Length;
            template.SourceSha256 = ComputeSha256(template.SourcePath);
        }

        private static string ComputeSha256(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha256 = SHA256.Create())
                return Convert.ToHexString(sha256.ComputeHash(stream));
        }

        private static string MakeSafeFolderName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var chars = (value ?? "order").Select(ch => invalid.Contains(ch) || ch == '|' ? '_' : ch).ToArray();
            var result = new string(chars).Trim('_', ' ');
            return string.IsNullOrEmpty(result) ? Guid.NewGuid().ToString("N") : result;
        }
    }
}
