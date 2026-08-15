using BarTenderPrinter.Domain.Common;

namespace BarTenderPrinter.Domain.Rework;

public enum ReworkOrderStatus
{
    Draft,
    Approved,
    Active,
    Completed,
    Cancelled
}

public sealed class ReworkOrder
{
    public EntityId Id { get; }
    public EntityId ProductionUnitId { get; }
    public EntityId RouteId { get; }
    public string ReasonCode { get; }
    public string StartOperationId { get; }
    public string ApprovedBy { get; private set; } = "";
    public DateTimeOffset? ApprovedAtUtc { get; private set; }
    public string ClosedBy { get; private set; } = "";
    public DateTimeOffset? ClosedAtUtc { get; private set; }
    public ReworkOrderStatus Status { get; private set; } = ReworkOrderStatus.Draft;
    public int Sequence { get; }
    public long Version { get; private set; }

    public ReworkOrder(
        EntityId id,
        EntityId productionUnitId,
        EntityId routeId,
        string reasonCode,
        string startOperationId,
        int sequence)
    {
        if (sequence <= 0) throw new ArgumentOutOfRangeException(nameof(sequence), "返工序次必须大于零。");
        Id = id;
        ProductionUnitId = productionUnitId;
        RouteId = routeId;
        ReasonCode = Required(reasonCode, "返工原因码");
        StartOperationId = Required(startOperationId, "返工起始工序");
        Sequence = sequence;
    }

    public void Approve(string approvedBy, DateTimeOffset? approvedAtUtc = null)
    {
        if (Status != ReworkOrderStatus.Draft) throw InvalidTransition(ReworkOrderStatus.Approved);
        ApprovedBy = Required(approvedBy, "审批人");
        ApprovedAtUtc = EnsureUtc(approvedAtUtc ?? DateTimeOffset.UtcNow, nameof(approvedAtUtc));
        Status = ReworkOrderStatus.Approved;
        Version++;
    }

    public void Activate()
    {
        if (Status != ReworkOrderStatus.Approved) throw InvalidTransition(ReworkOrderStatus.Active);
        Status = ReworkOrderStatus.Active;
        Version++;
    }

    public void Complete()
        => Complete([], [], ApprovedBy, DateTimeOffset.UtcNow);

    public void Complete(IEnumerable<string> requiredOperationIds, IEnumerable<string> passedOperationIds,
        string closedBy, DateTimeOffset closedAtUtc)
    {
        if (Status != ReworkOrderStatus.Active) throw InvalidTransition(ReworkOrderStatus.Completed);
        var required = requiredOperationIds.Select(value => Required(value, "必需工序")).ToHashSet(StringComparer.Ordinal);
        var passed = passedOperationIds.Select(value => Required(value, "已通过工序")).ToHashSet(StringComparer.Ordinal);
        if (!required.IsSubsetOf(passed)) throw new InvalidOperationException("返工路线仍有必需工序未通过。");
        ClosedBy = Required(closedBy, "关闭人");
        ClosedAtUtc = EnsureUtc(closedAtUtc, nameof(closedAtUtc));
        Status = ReworkOrderStatus.Completed;
        Version++;
    }

    public void Cancel()
    {
        if (Status is ReworkOrderStatus.Completed or ReworkOrderStatus.Cancelled)
            throw InvalidTransition(ReworkOrderStatus.Cancelled);
        Status = ReworkOrderStatus.Cancelled;
        Version++;
    }

    private InvalidOperationException InvalidTransition(ReworkOrderStatus next) =>
        new($"返工任务状态不能从 {Status} 变更为 {next}。");

    private static string Required(string value, string displayName)
    {
        var normalized = value?.Trim() ?? "";
        if (normalized.Length == 0) throw new ArgumentException($"{displayName}不能为空。", nameof(value));
        return normalized;
    }

    private static DateTimeOffset EnsureUtc(DateTimeOffset value, string parameterName) =>
        value.Offset == TimeSpan.Zero ? value : throw new ArgumentException("时间必须使用 UTC。", parameterName);
}
