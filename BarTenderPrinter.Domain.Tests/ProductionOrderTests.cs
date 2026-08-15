using BarTenderPrinter.Domain.Common;
using BarTenderPrinter.Domain.Orders;
using Xunit;

namespace BarTenderPrinter.Domain.Tests;

public sealed class ProductionOrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Order_FollowsSupportedProductionLifecycle()
    {
        var order = CreateOrder();

        order.Publish();
        order.StartProduction(Now);
        Assert.True(order.AcceptsStationPass(Now));
        order.Pause();
        Assert.False(order.AcceptsStationPass(Now));
        order.StartProduction(Now);
        order.Close();

        Assert.Equal(ProductionOrderStatus.Closed, order.Status);
        Assert.Equal(5, order.Version);
    }

    [Fact]
    public void Order_RejectsProductionOutsideValidityWindow()
    {
        var order = CreateOrder(Now.AddHours(1), Now.AddHours(2));
        order.Publish();

        Assert.Throws<InvalidOperationException>(() => order.StartProduction(Now));
    }

    [Fact]
    public void ClosedOrder_RejectsFurtherTransitions()
    {
        var order = CreateOrder();
        order.Publish();
        order.Close();

        Assert.Throws<InvalidOperationException>(() => order.StartProduction(Now));
        Assert.Throws<InvalidOperationException>(order.Close);
    }

    private static ProductionOrder CreateOrder(DateTimeOffset? from = null, DateTimeOffset? to = null) =>
        new(EntityId.New(), "PO-001", "Customer", "Model", "Black", 100, from, to);
}
