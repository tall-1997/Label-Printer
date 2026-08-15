using BarTenderPrinter.Domain.Common;
using BarTenderPrinter.Domain.Quality;
using Xunit;

namespace BarTenderPrinter.Domain.Tests;

public sealed class QualityTests
{
    [Fact]
    public void FailedInspectionRequiresApprovedDisposition()
    {
        var unitId = EntityId.New();
        var lot = new InspectionLot(EntityId.New(), EntityId.New(), "OQC", "AQL-1", [unitId]);
        lot.AddResult(new InspectionResult(EntityId.New(), lot.Id, unitId, "APPEARANCE",
            InspectionOutcome.Failed, "SCRATCH", "OP-20", "visible", DateTimeOffset.UtcNow));

        lot.Complete();
        lot.ApplyDisposition(new Disposition(EntityId.New(), lot.Id, DispositionDecision.Rework,
            "REPAIR", "quality-manager", DateTimeOffset.UtcNow));

        Assert.Equal(InspectionLotStatus.Disposed, lot.Status);
        Assert.Equal(DispositionDecision.Rework, lot.Disposition?.Decision);
    }

    [Fact]
    public void InspectionRejectsResultOutsideSample()
    {
        var lot = new InspectionLot(EntityId.New(), EntityId.New(), "QA", "ONE", [EntityId.New()]);
        var result = new InspectionResult(EntityId.New(), lot.Id, EntityId.New(), "POWER",
            InspectionOutcome.Passed, "", "OP-10", "", DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => lot.AddResult(result));
    }
}
