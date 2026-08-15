using BarTenderPrinter.Domain.Common;
using BarTenderPrinter.Domain.Numbering;

namespace BarTenderPrinter.Domain.Production;

public enum ProductionUnitStatus
{
    Created,
    Active,
    Frozen,
    Scrapped,
    Completed
}

public sealed class ProductionUnit
{
    private readonly Dictionary<NumberType, NumberAllocation> _identifiers = new();

    public EntityId Id { get; }
    public EntityId OrderId { get; }
    public ProductionUnitStatus Status { get; private set; } = ProductionUnitStatus.Created;
    public string CurrentOperationId { get; private set; } = "";
    public long Version { get; private set; }
    public IReadOnlyDictionary<NumberType, NumberAllocation> Identifiers => _identifiers;

    public ProductionUnit(EntityId id, EntityId orderId)
    {
        Id = id;
        OrderId = orderId;
    }

    public void Activate()
    {
        if (Status != ProductionUnitStatus.Created)
            throw new InvalidOperationException("只有新建生产单元可以激活。");
        Status = ProductionUnitStatus.Active;
        Version++;
    }

    public void AssignIdentifier(NumberType type, NumberAllocation allocation)
    {
        ArgumentNullException.ThrowIfNull(allocation);
        if (Status is ProductionUnitStatus.Scrapped or ProductionUnitStatus.Completed)
            throw new InvalidOperationException("当前生产单元状态无法分配标识。");
        if (_identifiers.ContainsKey(type))
            throw new InvalidOperationException($"生产单元已经分配 {type} 标识。");

        allocation.Assign(Id.Value);
        _identifiers.Add(type, allocation);
        Version++;
    }

    public string GetIdentifier(NumberType type) =>
        _identifiers.TryGetValue(type, out var allocation)
            ? allocation.Value
            : "";

    public void MoveToOperation(string operationId)
    {
        if (Status != ProductionUnitStatus.Active)
            throw new InvalidOperationException("只有活动生产单元可以移动工序。");
        CurrentOperationId = Required(operationId, "工序 ID");
        Version++;
    }

    public void Freeze()
    {
        if (Status != ProductionUnitStatus.Active)
            throw new InvalidOperationException("只有活动生产单元可以冻结。");
        Status = ProductionUnitStatus.Frozen;
        Version++;
    }

    public void Release()
    {
        if (Status != ProductionUnitStatus.Frozen)
            throw new InvalidOperationException("只有冻结生产单元可以恢复。");
        Status = ProductionUnitStatus.Active;
        Version++;
    }

    public void Scrap()
    {
        if (Status is ProductionUnitStatus.Scrapped or ProductionUnitStatus.Completed)
            throw new InvalidOperationException("当前生产单元状态无法报废。");
        Status = ProductionUnitStatus.Scrapped;
        Version++;
    }

    public void Complete()
    {
        if (Status != ProductionUnitStatus.Active)
            throw new InvalidOperationException("只有活动生产单元可以完成生产。");
        Status = ProductionUnitStatus.Completed;
        Version++;
    }

    private static string Required(string value, string name)
    {
        var normalized = value?.Trim() ?? "";
        if (normalized.Length == 0) throw new ArgumentException($"{name}不能为空。", nameof(value));
        return normalized;
    }
}
