using BarTenderPrinter.Domain.Common;

namespace BarTenderPrinter.Domain.Quality;

public enum InspectionLotStatus
{
    Open,
    Passed,
    Failed,
    Disposed
}

public enum InspectionOutcome
{
    Passed,
    Failed
}

public enum DispositionDecision
{
    Release,
    Rework,
    Scrap
}

public sealed record InspectionResult(
    EntityId Id,
    EntityId LotId,
    EntityId UnitId,
    string ItemCode,
    InspectionOutcome Outcome,
    string DefectCode,
    string ResponsibleOperationId,
    string Remarks,
    DateTimeOffset InspectedAtUtc);

public sealed record Disposition(
    EntityId Id,
    EntityId LotId,
    DispositionDecision Decision,
    string ReasonCode,
    string ApprovedBy,
    DateTimeOffset ApprovedAtUtc);

public sealed class InspectionLot
{
    private readonly List<InspectionResult> _results = new();

    public EntityId Id { get; }
    public EntityId OrderId { get; }
    public string InspectionType { get; }
    public string SampleRule { get; }
    public IReadOnlyList<EntityId> SampleUnitIds { get; }
    public InspectionLotStatus Status { get; private set; } = InspectionLotStatus.Open;
    public IReadOnlyList<InspectionResult> Results => _results.AsReadOnly();
    public Disposition? Disposition { get; private set; }
    public long Version { get; private set; }

    public InspectionLot(EntityId id, EntityId orderId, string inspectionType, string sampleRule,
        IEnumerable<EntityId> sampleUnitIds)
    {
        Id = id;
        OrderId = orderId;
        InspectionType = Required(inspectionType, "检验类型");
        SampleRule = Required(sampleRule, "抽样规则");
        SampleUnitIds = sampleUnitIds?.Distinct().ToArray() ?? throw new ArgumentNullException(nameof(sampleUnitIds));
        if (SampleUnitIds.Count == 0) throw new ArgumentException("抽检单至少包含一个样本。", nameof(sampleUnitIds));
    }

    public void AddResult(InspectionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (Status != InspectionLotStatus.Open) throw new InvalidOperationException("抽检单已完成判定。");
        if (result.LotId != Id || !SampleUnitIds.Contains(result.UnitId))
            throw new InvalidOperationException("检验结果与抽检单样本不匹配。");
        if (_results.Any(item => item.UnitId == result.UnitId && item.ItemCode == result.ItemCode))
            throw new InvalidOperationException("检验项目结果已经存在。");
        if (result.InspectedAtUtc.Offset != TimeSpan.Zero) throw new ArgumentException("检验时间必须使用 UTC。", nameof(result));
        _results.Add(result);
        Version++;
    }

    public void Complete()
    {
        if (Status != InspectionLotStatus.Open) throw new InvalidOperationException("抽检单已完成判定。");
        if (_results.Count == 0) throw new InvalidOperationException("抽检单尚无检验结果。");
        Status = _results.Any(result => result.Outcome == InspectionOutcome.Failed)
            ? InspectionLotStatus.Failed
            : InspectionLotStatus.Passed;
        Version++;
    }

    public void ApplyDisposition(Disposition disposition)
    {
        ArgumentNullException.ThrowIfNull(disposition);
        if (Status != InspectionLotStatus.Failed) throw new InvalidOperationException("仅失败抽检单需要质量处置。");
        if (disposition.LotId != Id) throw new InvalidOperationException("处置与抽检单不匹配。");
        if (disposition.ApprovedAtUtc.Offset != TimeSpan.Zero) throw new ArgumentException("审批时间必须使用 UTC。", nameof(disposition));
        Disposition = disposition;
        Status = InspectionLotStatus.Disposed;
        Version++;
    }

    private static string Required(string value, string name)
    {
        var normalized = value?.Trim() ?? "";
        return normalized.Length > 0 ? normalized : throw new ArgumentException($"{name}不能为空。", nameof(value));
    }
}
