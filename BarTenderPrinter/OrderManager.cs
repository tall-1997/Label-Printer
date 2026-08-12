using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        public string DisplayName => $"{Customer} / {ProductModel} / {Color} / {OrderNumber}";
        public string Key => BuildKey(Customer, ProductModel, Color, OrderNumber);

        public static string BuildKey(string customer, string productModel, string color, string orderNumber)
        {
            return Normalize(orderNumber);
        }

        private static string Normalize(string value) => (value ?? "").Trim();
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

        public string CopyTemplateForOrder(string sourceTemplatePath, string customer, string productModel, string color, string orderNumber)
        {
            if (string.IsNullOrEmpty(sourceTemplatePath) || !File.Exists(sourceTemplatePath))
                throw new FileNotFoundException("模板文件不存在", sourceTemplatePath);

            var orderDir = Path.Combine(AppPaths.OrdersDirectory, MakeSafeFolderName(PackagingOrder.BuildKey(customer, productModel, color, orderNumber)));
            Directory.CreateDirectory(orderDir);
            var targetPath = Path.Combine(orderDir, Path.GetFileName(sourceTemplatePath));
            File.Copy(sourceTemplatePath, targetPath, true);
            return targetPath;
        }

        private void Load()
        {
            if (!File.Exists(_path)) return;
            try
            {
                var items = JsonSerializer.Deserialize<List<PackagingOrder>>(File.ReadAllText(_path)) ?? new List<PackagingOrder>();
                _orders.Clear();
                _orders.AddRange(items.Where(item => item != null));
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

        private static string MakeSafeFolderName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var chars = (value ?? "order").Select(ch => invalid.Contains(ch) || ch == '|' ? '_' : ch).ToArray();
            var result = new string(chars).Trim('_', ' ');
            return string.IsNullOrEmpty(result) ? Guid.NewGuid().ToString("N") : result;
        }
    }
}
