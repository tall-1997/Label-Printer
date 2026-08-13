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
        public int SchemaVersion { get; set; } = 2;
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Customer { get; set; } = "";
        public string ProductModel { get; set; } = "";
        public string Color { get; set; } = "";
        public string OrderNumber { get; set; } = "";
        public string TemplatePath { get; set; } = "";
        public TemplateSettings Settings { get; set; } = new TemplateSettings();
        public List<OrderTemplate> Templates { get; set; } = new List<OrderTemplate>();

        public string DisplayName => $"{Customer} / {ProductModel} / {Color} / {OrderNumber}";
        public string OrderId => Id;
        public string Key => BuildKey(Customer, ProductModel, Color, OrderNumber);

        public static string BuildKey(string customer, string productModel, string color, string orderNumber)
        {
            return string.Join("|", new[]
            {
                Normalize(customer),
                Normalize(productModel),
                Normalize(color),
                Normalize(orderNumber)
            });
        }

        private static string Normalize(string value) => (value ?? "").Trim();
    }

    public class OrderTemplate
    {
        public int SchemaVersion { get; set; } = 2;
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string SourcePath { get; set; } = "";
        public string ArchivedPath { get; set; } = "";
        public long SourceLastWriteTimeUtcTicks { get; set; }
        public long SourceLength { get; set; }
        public string SourceSha256 { get; set; } = "";
        public List<string> FieldSnapshot { get; set; } = new List<string>();
        public TemplateSettings Settings { get; set; } = new TemplateSettings();

        public string DisplayName => Path.GetFileName(SourcePath);
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

        public void Add(PackagingOrder order, string previousKey = null)
        {
            var key = order.Key;
            var updatedOrders = _orders
                .Where(item => !string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase) &&
                               (string.IsNullOrEmpty(previousKey) || !string.Equals(item.Key, previousKey, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            updatedOrders.Add(order);
            updatedOrders.Sort((left, right) => string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase));
            Save(updatedOrders);
            _orders.Clear();
            _orders.AddRange(updatedOrders);
        }

        public PackagingOrder Find(string customer, string productModel, string color, string orderNumber)
        {
            var key = PackagingOrder.BuildKey(customer, productModel, color, orderNumber);
            return _orders.FirstOrDefault(order => string.Equals(order.Key, key, StringComparison.OrdinalIgnoreCase));
        }

        public OrderTemplate CreateTemplateReference(string sourceTemplatePath, string templateId = null)
        {
            if (string.IsNullOrEmpty(sourceTemplatePath) || !File.Exists(sourceTemplatePath))
                throw new FileNotFoundException("模板文件不存在", sourceTemplatePath);

            var id = string.IsNullOrWhiteSpace(templateId) ? Guid.NewGuid().ToString("N") : templateId;
            var template = new OrderTemplate
            {
                Id = id,
                SourcePath = Path.GetFullPath(sourceTemplatePath),
                ArchivedPath = ""
            };
            UpdateSourceSnapshot(template);
            return template;
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

        public void RefreshSourceTemplate(PackagingOrder order, OrderTemplate template)
        {
            if (order == null || template == null || !File.Exists(template.SourcePath))
                throw new FileNotFoundException("外部模板文件不存在", template?.SourcePath);
            UpdateSourceSnapshot(template);
            template.ArchivedPath = "";
            template.Settings.TemplateName = Path.GetFileName(template.SourcePath);
            template.Settings.TemplatePath = template.SourcePath;
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
                    item.Id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString("N") : item.Id;
                    item.SchemaVersion = Math.Max(item.SchemaVersion, 2);
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
                    item.Templates = item.Templates.Where(template => template != null).ToList();
                    foreach (var template in item.Templates)
                    {
                        template.Id = string.IsNullOrWhiteSpace(template.Id) ? Guid.NewGuid().ToString("N") : template.Id;
                        template.SchemaVersion = Math.Max(template.SchemaVersion, 2);
                        template.FieldSnapshot ??= new List<string>();
                        template.Settings ??= new TemplateSettings();
                        template.Settings.OrderId = item.Id;
                        template.Settings.TemplateId = template.Id;
                        template.Settings.Scope = "OrderTemplate";
                        if (!string.IsNullOrWhiteSpace(template.SourcePath))
                        {
                            try { template.SourcePath = Path.GetFullPath(template.SourcePath); }
                            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
                            {
                                LoggerService.Warn($"订单模板路径无效，等待重新选择: {template.SourcePath}");
                                template.SourcePath = "";
                            }
                            template.ArchivedPath = "";
                            template.Settings.TemplateName = Path.GetFileName(template.SourcePath);
                            template.Settings.TemplatePath = template.SourcePath;
                        }
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

        private void Save(IEnumerable<PackagingOrder> orders = null)
        {
            var json = JsonSerializer.Serialize(orders ?? _orders, new JsonSerializerOptions { WriteIndented = true });
            AtomicFileWriter.WriteAllText(_path, json);
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

    }
}
