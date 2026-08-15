using BarTenderPrinter.Domain.Common;
using BarTenderPrinter.Domain.Rework;
using Xunit;

namespace BarTenderPrinter.Domain.Tests;

public sealed class ReworkOrderTests
{
    [Fact]
    public void ReworkOrder_FollowsApprovalLifecycle()
    {
        var order = Create();

        order.Approve("supervisor");
        order.Activate();
        order.Complete();

        Assert.Equal(ReworkOrderStatus.Completed, order.Status);
        Assert.Equal("supervisor", order.ApprovedBy);
        Assert.Equal(3, order.Version);
    }

    [Fact]
    public void ReworkOrder_RejectsActivationBeforeApproval()
    {
        var order = Create();

        Assert.Throws<InvalidOperationException>(order.Activate);
    }

    [Fact]
    public void ReworkOrder_CanBeCancelledBeforeCompletion()
    {
        var order = Create();
        order.Approve("supervisor");

        order.Cancel();

        Assert.Equal(ReworkOrderStatus.Cancelled, order.Status);
        Assert.Throws<InvalidOperationException>(order.Cancel);
    }

    [Fact]
    public void ReworkOrder_ClosesOnlyAfterRequiredRouteOperationsPass()
    {
        var order = Create();
        order.Approve("supervisor", DateTimeOffset.UtcNow);
        order.Activate();

        Assert.Throws<InvalidOperationException>(() =>
            order.Complete(["OP-1", "OP-2"], ["OP-1"], "supervisor", DateTimeOffset.UtcNow));

        order.Complete(["OP-1", "OP-2"], ["OP-1", "OP-2"], "supervisor", DateTimeOffset.UtcNow);
        Assert.Equal(ReworkOrderStatus.Completed, order.Status);
        Assert.Equal("supervisor", order.ClosedBy);
    }

    private static ReworkOrder Create() =>
        new(EntityId.New(), EntityId.New(), EntityId.New(), "DEFECT", "OP-1", 1);
}
