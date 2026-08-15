using BarTenderPrinter.Domain.Common;
using BarTenderPrinter.Domain.Numbering;
using BarTenderPrinter.Domain.Production;
using Xunit;

namespace BarTenderPrinter.Domain.Tests;

public sealed class ProductionUnitTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Unit_AssignsReservedIdentifierOnce()
    {
        var unit = new ProductionUnit(EntityId.New(), EntityId.New());
        var allocation = Allocate(NumberType.SerialNumber);

        unit.AssignIdentifier(NumberType.SerialNumber, allocation);

        Assert.Equal(allocation.Value, unit.GetIdentifier(NumberType.SerialNumber));
        Assert.Equal(NumberAllocationStatus.Assigned, allocation.Status);
        Assert.Throws<InvalidOperationException>(() => unit.AssignIdentifier(NumberType.SerialNumber, Allocate(NumberType.SerialNumber)));
    }

    [Fact]
    public void Unit_FollowsActiveFreezeReleaseAndCompleteLifecycle()
    {
        var unit = new ProductionUnit(EntityId.New(), EntityId.New());

        unit.Activate();
        unit.MoveToOperation("ASSEMBLY-01");
        unit.Freeze();
        unit.Release();
        unit.Complete();

        Assert.Equal(ProductionUnitStatus.Completed, unit.Status);
        Assert.Equal("ASSEMBLY-01", unit.CurrentOperationId);
    }

    [Fact]
    public void FrozenUnit_RejectsOperationMovement()
    {
        var unit = new ProductionUnit(EntityId.New(), EntityId.New());
        unit.Activate();
        unit.Freeze();

        Assert.Throws<InvalidOperationException>(() => unit.MoveToOperation("NEXT"));
    }

    private static NumberAllocation Allocate(NumberType type)
    {
        var range = new NumberRange(EntityId.New(), EntityId.New(), type, "", NumberDatePattern.None, 1, 1);
        return range.Allocate(new IdempotencyKey(Guid.NewGuid().ToString("N")), "station", "operator", Now).Value!;
    }
}
