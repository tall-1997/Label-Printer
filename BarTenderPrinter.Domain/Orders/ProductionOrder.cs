using BarTenderPrinter.Domain.Common;

namespace BarTenderPrinter.Domain.Orders;

public enum ProductionOrderStatus
{
    Draft,
    Published,
    InProduction,
    Paused,
    Closed
}

public sealed class ProductionOrder
{
    public EntityId Id { get; }
    public string OrderNumber { get; }
    public string Customer { get; }
    public string ProductModel { get; }
    public string Color { get; }
    public int PlannedQuantity { get; }
    public DateTimeOffset? ValidFromUtc { get; }
    public DateTimeOffset? ValidToUtc { get; }
    public ProductionOrderStatus Status { get; private set; }
    public long Version { get; private set; }

    public ProductionOrder(
        EntityId id,
        string orderNumber,
        string customer,
        string productModel,
        string color,
        int plannedQuantity,
        DateTimeOffset? validFromUtc = null,
        DateTimeOffset? validToUtc = null)
    {
        Id = id;
        OrderNumber = NormalizeRequired(orderNumber, "订单编号");
        Customer = NormalizeRequired(customer, "客户");
        ProductModel = NormalizeRequired(productModel, "产品型号");
        Color = NormalizeRequired(color, "颜色");
        if (plannedQuantity <= 0) throw new ArgumentOutOfRangeException(nameof(plannedQuantity), "计划数量必须大于零。");
        ValidateUtc(validFromUtc, nameof(validFromUtc));
        ValidateUtc(validToUtc, nameof(validToUtc));
        if (validFromUtc.HasValue && validToUtc.HasValue && validFromUtc > validToUtc)
            throw new ArgumentException("订单有效期结束时间必须晚于或等于开始时间。", nameof(validToUtc));

        PlannedQuantity = plannedQuantity;
        ValidFromUtc = validFromUtc;
        ValidToUtc = validToUtc;
        Status = ProductionOrderStatus.Draft;
    }

    public void Publish() => Transition(ProductionOrderStatus.Draft, ProductionOrderStatus.Published);

    public void StartProduction(DateTimeOffset utcNow)
    {
        ValidateUtc(utcNow, nameof(utcNow));
        if (Status is not (ProductionOrderStatus.Published or ProductionOrderStatus.Paused))
            throw InvalidTransition(ProductionOrderStatus.InProduction);
        if (ValidFromUtc.HasValue && utcNow < ValidFromUtc.Value)
            throw new InvalidOperationException("订单尚未进入有效期。");
        if (ValidToUtc.HasValue && utcNow > ValidToUtc.Value)
            throw new InvalidOperationException("订单已超过有效期。");

        Status = ProductionOrderStatus.InProduction;
        Version++;
    }

    public void Pause() => Transition(ProductionOrderStatus.InProduction, ProductionOrderStatus.Paused);

    public void Close()
    {
        if (Status is not (ProductionOrderStatus.Published or ProductionOrderStatus.InProduction or ProductionOrderStatus.Paused))
            throw InvalidTransition(ProductionOrderStatus.Closed);

        Status = ProductionOrderStatus.Closed;
        Version++;
    }

    public bool AcceptsStationPass(DateTimeOffset utcNow)
    {
        ValidateUtc(utcNow, nameof(utcNow));
        return Status == ProductionOrderStatus.InProduction &&
               (!ValidFromUtc.HasValue || utcNow >= ValidFromUtc.Value) &&
               (!ValidToUtc.HasValue || utcNow <= ValidToUtc.Value);
    }

    private void Transition(ProductionOrderStatus expected, ProductionOrderStatus next)
    {
        if (Status != expected) throw InvalidTransition(next);
        Status = next;
        Version++;
    }

    private InvalidOperationException InvalidTransition(ProductionOrderStatus next) =>
        new($"订单状态不能从 {Status} 变更为 {next}。");

    private static string NormalizeRequired(string value, string displayName)
    {
        var normalized = value?.Trim() ?? "";
        if (normalized.Length == 0) throw new ArgumentException($"{displayName}不能为空。", nameof(value));
        return normalized;
    }

    private static void ValidateUtc(DateTimeOffset? value, string parameterName)
    {
        if (value.HasValue && value.Value.Offset != TimeSpan.Zero)
            throw new ArgumentException("领域时间必须使用 UTC。", parameterName);
    }
}
