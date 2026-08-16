using BarTenderPrinter.Domain.Common;

namespace BarTenderPrinter.Domain.Production;

public enum WeightMeasurementResult
{
    Passed,
    Failed
}

public sealed record WeightRule
{
    public EntityId Id { get; }
    public EntityId OrderId { get; }
    public string PackagingUnitType { get; }
    public decimal MinimumWeight { get; }
    public decimal MaximumWeight { get; }
    public string Unit { get; }

    public WeightRule(EntityId id, EntityId orderId, string packagingUnitType,
        decimal minimumWeight, decimal maximumWeight, string unit)
    {
        if (minimumWeight < 0 || maximumWeight < minimumWeight)
            throw new ArgumentOutOfRangeException(nameof(maximumWeight), "重量范围无效。");
        Id = id;
        OrderId = orderId;
        PackagingUnitType = Required(packagingUnitType, "包装类型");
        MinimumWeight = minimumWeight;
        MaximumWeight = maximumWeight;
        Unit = Required(unit, "重量单位");
    }

    public WeightMeasurementResult Evaluate(decimal weight, string unit)
    {
        if (weight < 0) throw new ArgumentOutOfRangeException(nameof(weight), "重量必须大于或等于零。");
        if (!string.Equals(Unit, Required(unit, "重量单位"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("WEIGHT_UNIT_MISMATCH");
        return weight >= MinimumWeight && weight <= MaximumWeight
            ? WeightMeasurementResult.Passed
            : WeightMeasurementResult.Failed;
    }

    private static string Required(string value, string name)
    {
        var normalized = value?.Trim() ?? "";
        if (normalized.Length == 0) throw new ArgumentException($"{name}不能为空。", nameof(value));
        return normalized;
    }
}

public enum IdentifierWriteTaskState
{
    Pending,
    InProgress,
    Succeeded,
    Failed,
    Uncertain
}
