namespace BarTenderPrinter.Domain.Common;

public sealed record AuditContext
{
    public required string ActorId { get; init; }
    public required string StationId { get; init; }
    public string ShiftId { get; init; } = "";
    public string CorrelationId { get; init; } = "";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ActorId)) throw new InvalidOperationException("操作员 ID 不能为空。");
        if (string.IsNullOrWhiteSpace(StationId)) throw new InvalidOperationException("工位 ID 不能为空。");
    }
}
