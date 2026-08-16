using System.Security.Cryptography;
using System.Text;
using BarTenderPrinter.Domain.Common;

namespace BarTenderPrinter.Domain.Archiving;

public enum ArchiveRepairTaskStatus
{
    Open,
    Repaired
}

public sealed record ArchiveRepairTask(
    EntityId Id,
    EntityId OrderId,
    EntityId ArchiveId,
    string ExpectedHash,
    string ActualHash,
    ArchiveRepairTaskStatus Status,
    DateTimeOffset CreatedAtUtc,
    string RepairedBy = "",
    DateTimeOffset? RepairedAtUtc = null,
    EntityId? ReplacementArchiveId = null);

public sealed record OrderArchiveSnapshot
{
    public EntityId Id { get; }
    public EntityId OrderId { get; }
    public string PayloadJson { get; }
    public string PayloadHash { get; }
    public DateTimeOffset ArchivedAtUtc { get; }
    public string ArchivedBy { get; }

    public OrderArchiveSnapshot(EntityId id, EntityId orderId, string payloadJson, DateTimeOffset archivedAtUtc,
        string archivedBy)
    {
        if (archivedAtUtc.Offset != TimeSpan.Zero) throw new ArgumentException("归档时间必须使用 UTC。", nameof(archivedAtUtc));
        Id = id;
        OrderId = orderId;
        PayloadJson = string.IsNullOrWhiteSpace(payloadJson)
            ? throw new ArgumentException("归档快照不能为空。", nameof(payloadJson))
            : payloadJson;
        PayloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(PayloadJson))).ToLowerInvariant();
        ArchivedAtUtc = archivedAtUtc;
        ArchivedBy = string.IsNullOrWhiteSpace(archivedBy)
            ? throw new ArgumentException("归档操作人不能为空。", nameof(archivedBy))
            : archivedBy.Trim();
    }
}
