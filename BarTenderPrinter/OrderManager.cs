using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
            return string.Concat(new[] { customer, productModel, color, orderNumber }
                .Select(value => EncodeKeyPart(Normalize(value))));
        }

        private static string EncodeKeyPart(string value) => $"{Encoding.UTF8.GetByteCount(value)}:{value}";

        private static string Normalize(string value) => (value ?? "").Trim();
    }

    internal static class OrderCascadeService
    {
        public static string[] GetModels(IEnumerable<PackagingOrder> orders, string customer) =>
            Select(orders, order => Matches(order.Customer, customer), order => order.ProductModel);

        public static string[] GetColors(IEnumerable<PackagingOrder> orders, string customer, string productModel) =>
            Select(orders, order => Matches(order.Customer, customer) && Matches(order.ProductModel, productModel), order => order.Color);

        public static string[] GetOrderNumbers(IEnumerable<PackagingOrder> orders, string customer, string productModel, string color) =>
            Select(orders, order => Matches(order.Customer, customer) && Matches(order.ProductModel, productModel) && Matches(order.Color, color), order => order.OrderNumber);

        public static bool Contains(IEnumerable<string> candidates, string value) =>
            !string.IsNullOrWhiteSpace(value) && (candidates ?? Array.Empty<string>())
                .Any(candidate => string.Equals(candidate, value.Trim(), StringComparison.OrdinalIgnoreCase));

        private static bool Matches(string actual, string expected) =>
            string.IsNullOrWhiteSpace(expected) || string.Equals(actual?.Trim(), expected.Trim(), StringComparison.OrdinalIgnoreCase);

        private static string[] Select(IEnumerable<PackagingOrder> orders, Func<PackagingOrder, bool> predicate, Func<PackagingOrder, string> selector) =>
            (orders ?? Array.Empty<PackagingOrder>())
                .Where(order => order != null && predicate(order))
                .Select(selector)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, NaturalStringComparer.Instance)
                .ToArray();
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
        private readonly string _path;

        public IReadOnlyList<PackagingOrder> Orders => _orders;
        public Exception LoadError { get; private set; }

        public void Reload()
        {
            LoadError = null;
            Load();
        }

        public OrderManager()
            : this(AppPaths.OrdersFile, true)
        {
        }

        public OrderManager(string path)
            : this(path, false)
        {
        }

        private OrderManager(string path, bool initializePaths)
        {
            if (initializePaths) AppPaths.Initialize();
            _path = path;
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
                var migrated = items.Any(item => item == null);
                foreach (var item in items.Where(item => item != null))
                {
                    if (string.IsNullOrWhiteSpace(item.Id)) { item.Id = Guid.NewGuid().ToString("N"); migrated = true; }
                    if (item.SchemaVersion < 2) { item.SchemaVersion = 2; migrated = true; }
                    if (item.Templates == null) { item.Templates = new List<OrderTemplate>(); migrated = true; }
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
                    if (item.Templates.Any(template => template == null)) migrated = true;
                    item.Templates = item.Templates.Where(template => template != null).ToList();
                    foreach (var template in item.Templates)
                    {
                        if (string.IsNullOrWhiteSpace(template.Id)) { template.Id = Guid.NewGuid().ToString("N"); migrated = true; }
                        if (template.SchemaVersion < 2) { template.SchemaVersion = 2; migrated = true; }
                        if (template.FieldSnapshot == null) { template.FieldSnapshot = new List<string>(); migrated = true; }
                        if (template.Settings == null) { template.Settings = new TemplateSettings(); migrated = true; }
                        var previousSettingsVersion = template.Settings.SchemaVersion;
                        ValidationService.MigrateLocalDataSelection(template.Settings);
                        if (template.Settings.SchemaVersion != previousSettingsVersion) migrated = true;
                        if (!string.Equals(template.Settings.OrderId, item.Id, StringComparison.Ordinal) ||
                            !string.Equals(template.Settings.TemplateId, template.Id, StringComparison.Ordinal) ||
                            !string.Equals(template.Settings.Scope, "OrderTemplate", StringComparison.Ordinal)) migrated = true;
                        template.Settings.OrderId = item.Id;
                        template.Settings.TemplateId = template.Id;
                        template.Settings.Scope = "OrderTemplate";
                        if (!string.IsNullOrWhiteSpace(template.SourcePath))
                        {
                            var previousSourcePath = template.SourcePath;
                            var previousArchivedPath = template.ArchivedPath;
                            var previousTemplateName = template.Settings.TemplateName;
                            var previousTemplatePath = template.Settings.TemplatePath;
                            try { template.SourcePath = Path.GetFullPath(template.SourcePath); }
                            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
                            {
                                LoggerService.Warn($"订单模板路径无效，等待重新选择: {template.SourcePath}");
                                template.SourcePath = "";
                            }
                            template.ArchivedPath = "";
                            template.Settings.TemplateName = Path.GetFileName(template.SourcePath);
                            template.Settings.TemplatePath = template.SourcePath;
                            if (!string.Equals(previousSourcePath, template.SourcePath, StringComparison.Ordinal) ||
                                !string.Equals(previousArchivedPath, template.ArchivedPath, StringComparison.Ordinal) ||
                                !string.Equals(previousTemplateName, template.Settings.TemplateName, StringComparison.Ordinal) ||
                                !string.Equals(previousTemplatePath, template.Settings.TemplatePath, StringComparison.Ordinal)) migrated = true;
                        }
                    }
                    _orders.Add(item);
                }
                if (migrated) Save();
            }
            catch (Exception ex)
            {
                LoadError = ex;
                LoggerService.Error("加载订单失败", ex);
            }
        }

        private void Save(IEnumerable<PackagingOrder> orders = null)
        {
            if (LoadError != null) throw new InvalidOperationException("订单文件加载失败，已阻止覆盖保存。请先恢复订单文件。", LoadError);
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
