using BarTenderPrinter.Domain.Common;

namespace BarTenderPrinter.Domain.Packaging;

public enum PackagingUnitType
{
    Body,
    ColorBox,
    Carton,
    Pallet
}

public enum PackagingUnitStatus
{
    Open,
    Closed,
    Frozen,
    Shipped
}

public enum LabelType
{
    Body,
    ColorBox,
    Carton,
    Pallet
}

public sealed class PackagingUnit
{
    private readonly List<EntityId> _childIds = new();

    public EntityId Id { get; }
    public EntityId OrderId { get; }
    public PackagingUnitType Type { get; }
    public string Code { get; }
    public string ProductModel { get; }
    public string Color { get; }
    public int Capacity { get; }
    public EntityId? ProductionUnitId { get; }
    public PackagingUnitStatus Status { get; private set; }
    public long Version { get; private set; }
    public IReadOnlyList<EntityId> ChildIds => _childIds.AsReadOnly();
    public bool IsFull => Type != PackagingUnitType.Body && _childIds.Count == Capacity;

    public PackagingUnit(
        EntityId id,
        EntityId orderId,
        PackagingUnitType type,
        string code,
        string productModel,
        string color,
        int capacity,
        EntityId? productionUnitId = null)
    {
        if (type == PackagingUnitType.Body && capacity != 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "机身包装单元容量必须为零。");
        if (type != PackagingUnitType.Body && capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "容器包装单元容量必须大于零。");
        if (type != PackagingUnitType.Body && productionUnitId.HasValue)
            throw new ArgumentException("只有机身包装单元可以关联生产单元。", nameof(productionUnitId));

        Id = id;
        OrderId = orderId;
        Type = type;
        Code = Required(code, "包装码");
        ProductModel = Required(productModel, "产品型号");
        Color = Required(color, "颜色");
        Capacity = capacity;
        ProductionUnitId = productionUnitId;
        Status = type == PackagingUnitType.Body ? PackagingUnitStatus.Closed : PackagingUnitStatus.Open;
    }

    internal void AddChild(EntityId childId)
    {
        if (Status != PackagingUnitStatus.Open) throw new InvalidOperationException("已关闭包装单元无法新增子项。");
        if (_childIds.Count >= Capacity) throw new InvalidOperationException("包装单元已达到容量上限。");
        if (_childIds.Contains(childId)) throw new InvalidOperationException("包装子项已经存在。");
        _childIds.Add(childId);
        Version++;
    }

    internal void RemoveChild(EntityId childId)
    {
        if (Status != PackagingUnitStatus.Open) throw new InvalidOperationException("已关闭包装单元无法解绑子项。");
        if (!_childIds.Remove(childId)) throw new InvalidOperationException("包装子项不存在。");
        Version++;
    }

    internal void Close()
    {
        if (Status != PackagingUnitStatus.Open) throw new InvalidOperationException("包装单元已经关闭。");
        if (!IsFull) throw new InvalidOperationException("包装单元达到配置容量后才能关闭。");
        Status = PackagingUnitStatus.Closed;
        Version++;
    }

    private static string Required(string value, string displayName)
    {
        var normalized = value?.Trim() ?? "";
        if (normalized.Length == 0) throw new ArgumentException($"{displayName}不能为空。", nameof(value));
        return normalized;
    }
}

public sealed record PackagingBinding(
    EntityId ParentId,
    EntityId ChildId,
    DateTimeOffset BoundAtUtc,
    string OperatorId);

public sealed record PackagingPrintIntent
{
    public required EntityId Id { get; init; }
    public required EntityId PackagingUnitId { get; init; }
    public required LabelType LabelType { get; init; }
    public required IReadOnlyDictionary<string, string> Fields { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
}
