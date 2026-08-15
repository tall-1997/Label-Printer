using System;
using System.Collections.Generic;
using System.Linq;

namespace BarTenderPrinter
{
    public sealed class LabelTemplateRegistration
    {
        public string Customer { get; set; } = "";
        public string ProductModel { get; set; } = "";
        public LabelType LabelType { get; set; }
        public string TemplateId { get; set; } = "";
        public string TemplatePath { get; set; } = "";
        public string Version { get; set; } = "";
        public DateTime EffectiveFromUtc { get; set; }
        public DateTime? EffectiveToUtc { get; set; }
    }

    public sealed class LabelTemplateRegistry
    {
        private readonly List<LabelTemplateRegistration> _registrations;

        public LabelTemplateRegistry(IEnumerable<LabelTemplateRegistration> registrations)
        {
            _registrations = (registrations ?? Array.Empty<LabelTemplateRegistration>()).ToList();
            if (_registrations.Any(item => item == null || item.LabelType == LabelType.Unspecified ||
                string.IsNullOrWhiteSpace(item.Customer) || string.IsNullOrWhiteSpace(item.ProductModel) ||
                string.IsNullOrWhiteSpace(item.TemplateId) || string.IsNullOrWhiteSpace(item.TemplatePath) ||
                string.IsNullOrWhiteSpace(item.Version)))
                throw new ArgumentException("模板注册项缺少必填字段。", nameof(registrations));
            if (_registrations.Any(item => item.EffectiveFromUtc.Kind != DateTimeKind.Utc ||
                (item.EffectiveToUtc.HasValue && item.EffectiveToUtc.Value.Kind != DateTimeKind.Utc)))
                throw new ArgumentException("模板生效时间必须使用 UTC。", nameof(registrations));
            if (_registrations.Any(item => item.EffectiveToUtc.HasValue && item.EffectiveToUtc <= item.EffectiveFromUtc))
                throw new ArgumentException("模板失效时间必须晚于生效时间。", nameof(registrations));
        }

        public LabelTemplateRegistration Resolve(string customer, string productModel, LabelType labelType, DateTime effectiveAtUtc)
        {
            if (effectiveAtUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("解析时间必须使用 UTC。", nameof(effectiveAtUtc));
            var matches = _registrations.Where(item =>
                    string.Equals(item.Customer, customer?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(item.ProductModel, productModel?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    item.LabelType == labelType && item.EffectiveFromUtc <= effectiveAtUtc &&
                    (!item.EffectiveToUtc.HasValue || effectiveAtUtc < item.EffectiveToUtc.Value))
                .OrderByDescending(item => item.EffectiveFromUtc)
                .ToList();
            if (matches.Count == 0) throw new KeyNotFoundException("未找到生效的标签模板。");
            if (matches.Count > 1 && matches[0].EffectiveFromUtc == matches[1].EffectiveFromUtc)
                throw new InvalidOperationException("存在冲突的标签模板版本。");
            return matches[0];
        }
    }
}
