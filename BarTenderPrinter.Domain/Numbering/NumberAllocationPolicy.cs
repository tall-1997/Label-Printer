namespace BarTenderPrinter.Domain.Numbering;

public static class NumberAllocationPolicy
{
    public static bool CanTransition(NumberAllocationStatus current, NumberAllocationStatus target) =>
        (current, target) is
            (NumberAllocationStatus.Reserved, NumberAllocationStatus.Released) or
            (NumberAllocationStatus.Reserved, NumberAllocationStatus.Scrapped) or
            (NumberAllocationStatus.Reserved, NumberAllocationStatus.Frozen) or
            (NumberAllocationStatus.Assigned, NumberAllocationStatus.Scrapped) or
            (NumberAllocationStatus.Assigned, NumberAllocationStatus.Frozen) or
            (NumberAllocationStatus.Frozen, NumberAllocationStatus.Scrapped);
}
