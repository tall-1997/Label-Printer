using System.Globalization;
using BarTenderPrinter.Domain.Common;

namespace BarTenderPrinter.Devices;

public enum SimulatedScaleScenario
{
    StableReading,
    Timeout,
    FormatError,
    OutOfRange,
    ConnectionFailure
}

public sealed record SimulatedScaleOptions
{
    public string AdapterId { get; init; } = "simulated-scale";
    public SimulatedScaleScenario Scenario { get; init; } = SimulatedScaleScenario.StableReading;
    public string RawReading { get; init; } = "12.500";
    public IReadOnlyList<string>? RawReadings { get; init; }
    public TimeSpan ReadingInterval { get; init; }
    public decimal OutOfRangeWeight { get; init; } = decimal.MaxValue;
}

public sealed class SimulatedScaleAdapter : IScaleAdapter
{
    private static readonly TimeSpan MaximumDelay = TimeSpan.FromMilliseconds(uint.MaxValue - 1);
    private readonly SimulatedScaleScenario _scenario;
    private readonly string _rawReading;
    private readonly IReadOnlyList<string>? _rawReadings;
    private readonly TimeSpan _readingInterval;
    private readonly decimal _outOfRangeWeight;
    private readonly IUtcClock _clock;
    private bool _disposed;

    public SimulatedScaleAdapter(SimulatedScaleOptions options, IUtcClock? clock = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _clock = clock ?? SystemUtcClock.Instance;
        AdapterId = RequireValue(options.AdapterId, nameof(options.AdapterId));
        _scenario = options.Scenario;
        _rawReading = options.RawReading;
        _rawReadings = options.RawReadings?.ToArray();
        _readingInterval = options.ReadingInterval;
        _outOfRangeWeight = options.OutOfRangeWeight;
    }

    public string AdapterId { get; }
    public bool IsSimulation => true;

    public async Task<ScaleReading> ReadStableAsync(
        ScaleProfile profile,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateProfile(profile);
        ValidateOptions();
        cancellationToken.ThrowIfCancellationRequested();

        if (_scenario == SimulatedScaleScenario.ConnectionFailure)
        {
            throw Error(
                DeviceErrorCodes.ConnectionFailed,
                "电子称连接失败。",
                true,
                "SIM_SCALE_CONNECTION_FAILED");
        }

        if (_scenario == SimulatedScaleScenario.Timeout)
        {
            await Task.Delay(profile.Timeout, cancellationToken).ConfigureAwait(false);
            throw Error(DeviceErrorCodes.Timeout, "电子称稳定读数超时。", true, "SIM_SCALE_TIMEOUT");
        }

        if (_scenario == SimulatedScaleScenario.OutOfRange)
        {
            return new ScaleReading(
                _outOfRangeWeight,
                profile.Unit.Trim(),
                AdapterId,
                UtcNow(),
                true);
        }

        var readings = _scenario == SimulatedScaleScenario.FormatError
            ? new[] { "invalid" }
            : _rawReadings ?? Enumerable.Repeat(_rawReading, profile.StableReadingCount);
        decimal? previousWeight = null;
        var stableCount = 0;
        var remaining = profile.Timeout;

        foreach (var rawReading in readings)
        {
            if (stableCount > 0 && _readingInterval > TimeSpan.Zero)
            {
                if (_readingInterval >= remaining)
                {
                    await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
                    throw Error(DeviceErrorCodes.Timeout, "电子称稳定读数超时。", true, "SIM_SCALE_TIMEOUT");
                }

                await Task.Delay(_readingInterval, cancellationToken).ConfigureAwait(false);
                remaining -= _readingInterval;
            }

            var weight = ParseWeight(rawReading, profile);
            stableCount = previousWeight == weight ? stableCount + 1 : 1;
            previousWeight = weight;
            if (stableCount >= profile.StableReadingCount)
            {
                return new ScaleReading(weight, profile.Unit.Trim(), AdapterId, UtcNow(), true);
            }
        }

        throw Error(DeviceErrorCodes.Timeout, "电子称读数序列未达到稳定条件。", true, "SIM_SCALE_UNSTABLE");
    }

    public void Dispose() => _disposed = true;

    private static decimal ParseWeight(string rawReading, ScaleProfile profile)
    {
        if (profile.DataStartPosition > rawReading.Length ||
            profile.DataLength > rawReading.Length - profile.DataStartPosition)
        {
            throw Error(DeviceErrorCodes.ProtocolError, "电子称读数长度不符合配置。", false, "SIM_SCALE_LENGTH");
        }

        var value = profile.DataLength == 0
            ? rawReading
            : rawReading.Substring(profile.DataStartPosition, profile.DataLength);

        if (!decimal.TryParse(value.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var weight))
        {
            throw Error(DeviceErrorCodes.ProtocolError, "电子称读数格式错误。", false, "SIM_SCALE_FORMAT");
        }

        return weight;
    }

    private static void ValidateProfile(ScaleProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (string.IsNullOrWhiteSpace(profile.PortName) ||
            string.IsNullOrWhiteSpace(profile.Unit) ||
            profile.BaudRate <= 0 ||
            profile.DataStartPosition < 0 ||
            profile.DataLength < 0 ||
            profile.StableReadingCount <= 0 ||
            profile.Timeout <= TimeSpan.Zero ||
            profile.Timeout > MaximumDelay)
        {
            throw Error(
                DeviceErrorCodes.InvalidConfiguration,
                "电子称配置无效。",
                false,
                "SIM_SCALE_PROFILE");
        }
    }

    private void ValidateOptions()
    {
        if (!Enum.IsDefined(_scenario) ||
            _rawReading is null ||
            _rawReadings is { Count: 0 } ||
            _rawReadings?.Any(reading => reading is null) == true ||
            _readingInterval < TimeSpan.Zero ||
            _readingInterval > MaximumDelay)
        {
            throw Error(
                DeviceErrorCodes.InvalidConfiguration,
                "电子称模拟器配置无效。",
                false,
                "SIM_SCALE_OPTIONS");
        }
    }

    private DateTimeOffset UtcNow()
    {
        var value = _clock.UtcNow;
        if (value.Offset != TimeSpan.Zero)
        {
            throw new InvalidOperationException("设备时钟必须返回 UTC 时间。");
        }

        return value;
    }

    private static string RequireValue(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("值不能为空。", parameterName)
            : value.Trim();

    private static DeviceAdapterException Error(
        string code,
        string message,
        bool retryable,
        string diagnosticCode) =>
        new(new DeviceError(code, message, retryable, diagnosticCode));
}
