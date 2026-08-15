namespace BarTenderPrinter.Devices;

public interface IIdentifierWriter : IDeviceAdapter, IDisposable
{
    Task<IdentifierWriteResult> WriteAndVerifyAsync(
        IdentifierWriteTask task,
        CancellationToken cancellationToken);
}

public sealed record IdentifierWriteTask
{
    public required string TaskId { get; init; }
    public required IReadOnlyDictionary<string, string> Identifiers { get; init; }
    public required string DevicePlatform { get; init; }
}

public enum IdentifierWriteStatus
{
    Succeeded,
    Failed,
    Uncertain
}

public sealed record IdentifierWriteResult
{
    public required string TaskId { get; init; }
    public required IdentifierWriteStatus Status { get; init; }
    public required IReadOnlyDictionary<string, string> RequestedIdentifiers { get; init; }
    public required IReadOnlyDictionary<string, string> ReadBackIdentifiers { get; init; }
    public required string ToolVersion { get; init; }
    public required string DevicePlatform { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public required DateTimeOffset CompletedAtUtc { get; init; }
    public required bool IsSimulation { get; init; }
    public DeviceError? Error { get; init; }

    public bool IsReadBackConsistent =>
        RequestedIdentifiers.Count == ReadBackIdentifiers.Count &&
        RequestedIdentifiers.All(pair =>
            ReadBackIdentifiers.TryGetValue(pair.Key, out var value) &&
            string.Equals(pair.Value, value, StringComparison.Ordinal));
}
