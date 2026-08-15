using BarTenderPrinter.Domain.Common;

namespace BarTenderPrinter.Domain.Shipping;

public enum ShipmentStatus
{
    Draft,
    PendingConfirmation,
    Confirmed
}

public sealed record ShipmentItem(EntityId CartonId, int Quantity, DateTimeOffset ScannedAtUtc, string OperatorId);

public sealed class Shipment
{
    private readonly List<ShipmentItem> _items = new();

    public EntityId Id { get; }
    public EntityId OrderId { get; }
    public string Customer { get; }
    public int PlannedQuantity { get; }
    public string DeliveryReference { get; }
    public ShipmentStatus Status { get; private set; } = ShipmentStatus.Draft;
    public IReadOnlyList<ShipmentItem> Items => _items.AsReadOnly();
    public int ActualQuantity => _items.Sum(item => item.Quantity);
    public long Version { get; private set; }

    public Shipment(EntityId id, EntityId orderId, string customer, int plannedQuantity, string deliveryReference)
    {
        if (plannedQuantity <= 0) throw new ArgumentOutOfRangeException(nameof(plannedQuantity));
        Id = id;
        OrderId = orderId;
        Customer = Required(customer, "客户");
        PlannedQuantity = plannedQuantity;
        DeliveryReference = Required(deliveryReference, "交付信息");
    }

    public void AddItem(ShipmentItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (Status == ShipmentStatus.Confirmed) throw new InvalidOperationException("已确认出库单不可变更。");
        if (item.Quantity <= 0) throw new ArgumentOutOfRangeException(nameof(item));
        if (item.ScannedAtUtc.Offset != TimeSpan.Zero) throw new ArgumentException("扫描时间必须使用 UTC。", nameof(item));
        if (_items.Any(existing => existing.CartonId == item.CartonId)) throw new InvalidOperationException("卡通箱已加入出库单。");
        _items.Add(item with { OperatorId = Required(item.OperatorId, "操作员") });
        Status = ShipmentStatus.PendingConfirmation;
        Version++;
    }

    public void Confirm()
    {
        if (Status != ShipmentStatus.PendingConfirmation) throw new InvalidOperationException("出库单尚无待确认明细。");
        if (ActualQuantity != PlannedQuantity) throw new InvalidOperationException("实际数量与计划数量不一致。");
        Status = ShipmentStatus.Confirmed;
        Version++;
    }

    private static string Required(string value, string name)
    {
        var normalized = value?.Trim() ?? "";
        return normalized.Length > 0 ? normalized : throw new ArgumentException($"{name}不能为空。", nameof(value));
    }
}
