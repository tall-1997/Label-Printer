namespace BarTenderPrinter.Devices;

public interface IScaleAdapter : IDeviceAdapter, IDisposable
{
    Task<ScaleReading> ReadStableAsync(
        ScaleProfile profile,
        CancellationToken cancellationToken);
}

public sealed record ScaleProfile
{
    public required string PortName { get; init; }
    public int BaudRate { get; init; } = 9600;
    public int DataStartPosition { get; init; }
    public int DataLength { get; init; }
    public required string Unit { get; init; }
    public int StableReadingCount { get; init; } = 3;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(3);
}

public sealed record ScaleReading(
    decimal Weight,
    string Unit,
    string DeviceId,
    DateTimeOffset CapturedAtUtc,
    bool IsSimulation);
