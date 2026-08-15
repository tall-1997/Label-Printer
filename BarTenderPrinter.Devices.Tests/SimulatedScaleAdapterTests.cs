using BarTenderPrinter.Devices;
using BarTenderPrinter.Domain.Common;
using Xunit;

namespace BarTenderPrinter.Devices.Tests;

public sealed class SimulatedScaleAdapterTests
{
    private static readonly DateTimeOffset CapturedAtUtc =
        new(2026, 8, 15, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task StableReading_ReturnsNormalizedSimulatedReadingWithUtcTimestamp()
    {
        using var adapter = new SimulatedScaleAdapter(
            new SimulatedScaleOptions { AdapterId = "scale-01", RawReading = "WT: 12.500kg" },
            new FixedClock(CapturedAtUtc));

        var reading = await adapter.ReadStableAsync(
            Profile(start: 4, length: 6),
            CancellationToken.None);

        Assert.Equal(12.500m, reading.Weight);
        Assert.Equal("kg", reading.Unit);
        Assert.Equal("scale-01", reading.DeviceId);
        Assert.True(reading.IsSimulation);
        Assert.Equal(CapturedAtUtc, reading.CapturedAtUtc);
        Assert.Equal(TimeSpan.Zero, reading.CapturedAtUtc.Offset);
    }

    [Fact]
    public async Task StableReading_RequiresConfiguredConsecutiveEqualReadings()
    {
        using var adapter = new SimulatedScaleAdapter(new SimulatedScaleOptions
        {
            RawReadings = new[] { "1.0", "1.0", "2.0", "2.0", "2.0" }
        });

        var reading = await adapter.ReadStableAsync(Profile(), CancellationToken.None);

        Assert.Equal(2.0m, reading.Weight);
    }

    [Fact]
    public async Task UnstableSequence_ReturnsStructuredTimeout()
    {
        using var adapter = new SimulatedScaleAdapter(new SimulatedScaleOptions
        {
            RawReadings = new[] { "1.0", "1.0", "2.0", "2.0" }
        });

        var exception = await Assert.ThrowsAsync<DeviceAdapterException>(() =>
            adapter.ReadStableAsync(Profile(), CancellationToken.None));

        Assert.Equal(DeviceErrorCodes.Timeout, exception.Error.Code);
        Assert.Equal("SIM_SCALE_UNSTABLE", exception.Error.DiagnosticCode);
        Assert.True(exception.Error.Retryable);
    }

    [Fact]
    public async Task Timeout_ThrowsStructuredRetryableDeviceError()
    {
        using var adapter = new SimulatedScaleAdapter(
            new SimulatedScaleOptions { Scenario = SimulatedScaleScenario.Timeout });

        var exception = await Assert.ThrowsAsync<DeviceAdapterException>(() =>
            adapter.ReadStableAsync(Profile(timeout: TimeSpan.FromMilliseconds(1)), CancellationToken.None));

        Assert.Equal(DeviceErrorCodes.Timeout, exception.Error.Code);
        Assert.True(exception.Error.Retryable);
        Assert.Equal("SIM_SCALE_TIMEOUT", exception.Error.DiagnosticCode);
    }

    [Fact]
    public async Task ConnectionFailure_ThrowsStructuredRetryableConnectionError()
    {
        using var adapter = new SimulatedScaleAdapter(
            new SimulatedScaleOptions { Scenario = SimulatedScaleScenario.ConnectionFailure });

        var exception = await Assert.ThrowsAsync<DeviceAdapterException>(() =>
            adapter.ReadStableAsync(Profile(), CancellationToken.None));

        Assert.Equal(DeviceErrorCodes.ConnectionFailed, exception.Error.Code);
        Assert.Equal("SIM_SCALE_CONNECTION_FAILED", exception.Error.DiagnosticCode);
        Assert.True(exception.Error.Retryable);
    }

    [Fact]
    public async Task FormatError_ThrowsStructuredProtocolErrorWithoutRawInput()
    {
        using var adapter = new SimulatedScaleAdapter(
            new SimulatedScaleOptions { Scenario = SimulatedScaleScenario.FormatError });

        var exception = await Assert.ThrowsAsync<DeviceAdapterException>(() =>
            adapter.ReadStableAsync(Profile(), CancellationToken.None));

        Assert.Equal(DeviceErrorCodes.ProtocolError, exception.Error.Code);
        Assert.False(exception.Error.Retryable);
        Assert.DoesNotContain("invalid", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OutOfRange_ReturnsConfiguredValueForDomainValidation()
    {
        using var adapter = new SimulatedScaleAdapter(new SimulatedScaleOptions
        {
            Scenario = SimulatedScaleScenario.OutOfRange,
            OutOfRangeWeight = 999.25m
        });

        var reading = await adapter.ReadStableAsync(Profile(), CancellationToken.None);

        Assert.Equal(999.25m, reading.Weight);
        Assert.True(reading.IsSimulation);
    }

    [Fact]
    public async Task Cancellation_IsPropagatedInsteadOfConvertedToDeviceFailure()
    {
        using var adapter = new SimulatedScaleAdapter(
            new SimulatedScaleOptions { Scenario = SimulatedScaleScenario.Timeout });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            adapter.ReadStableAsync(Profile(), cancellation.Token));
    }

    [Theory]
    [MemberData(nameof(InvalidProfiles))]
    public async Task InvalidProfile_ThrowsStructuredConfigurationError(ScaleProfile profile)
    {
        using var adapter = new SimulatedScaleAdapter(new SimulatedScaleOptions());

        var exception = await Assert.ThrowsAsync<DeviceAdapterException>(() =>
            adapter.ReadStableAsync(profile, CancellationToken.None));

        Assert.Equal(DeviceErrorCodes.InvalidConfiguration, exception.Error.Code);
    }

    [Fact]
    public async Task DisposedAdapter_RejectsFurtherReads()
    {
        var adapter = new SimulatedScaleAdapter(new SimulatedScaleOptions());
        adapter.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            adapter.ReadStableAsync(Profile(), CancellationToken.None));
    }

    [Fact]
    public async Task UnknownScenario_ThrowsStructuredConfigurationError()
    {
        using var adapter = new SimulatedScaleAdapter(new SimulatedScaleOptions
        {
            Scenario = (SimulatedScaleScenario)999
        });

        var exception = await Assert.ThrowsAsync<DeviceAdapterException>(() =>
            adapter.ReadStableAsync(Profile(), CancellationToken.None));

        Assert.Equal(DeviceErrorCodes.InvalidConfiguration, exception.Error.Code);
        Assert.Equal("SIM_SCALE_OPTIONS", exception.Error.DiagnosticCode);
    }

    [Fact]
    public async Task Constructor_CopiesMutableReadingSequence()
    {
        var readings = new List<string> { "3.0", "3.0", "3.0" };
        using var adapter = new SimulatedScaleAdapter(
            new SimulatedScaleOptions { RawReadings = readings });
        readings[1] = "4.0";
        readings.Clear();

        var reading = await adapter.ReadStableAsync(Profile(), CancellationToken.None);

        Assert.Equal(3.0m, reading.Weight);
    }

    public static TheoryData<ScaleProfile> InvalidProfiles => new()
    {
        Profile() with { PortName = " " },
        Profile() with { BaudRate = 0 },
        Profile() with { StableReadingCount = 0 },
        Profile() with { Timeout = TimeSpan.Zero },
        Profile() with { Timeout = TimeSpan.FromTicks(-1) },
        Profile() with { Timeout = TimeSpan.MaxValue }
    };

    private static ScaleProfile Profile(
        int start = 0,
        int length = 0,
        TimeSpan? timeout = null) => new()
    {
        PortName = "SIM1",
        BaudRate = 9600,
        DataStartPosition = start,
        DataLength = length,
        Unit = "kg",
        StableReadingCount = 3,
        Timeout = timeout ?? TimeSpan.FromSeconds(1)
    };

    private sealed class FixedClock(DateTimeOffset utcNow) : IUtcClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
