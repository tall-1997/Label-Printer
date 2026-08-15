using BarTenderPrinter.Devices;
using BarTenderPrinter.Domain.Common;
using Xunit;

namespace BarTenderPrinter.Devices.Tests;

public sealed class SimulatedIdentifierWriterTests
{
    private static readonly DateTimeOffset StartedAtUtc =
        new(2026, 8, 15, 11, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Success_PreservesSnapshotAndReturnsConsistentReadBack()
    {
        var identifiers = new Dictionary<string, string>
        {
            ["IMEI1"] = "860000000000001",
            ["SN"] = "SN0001"
        };
        var writer = Writer(SimulatedIdentifierWriteScenario.Success);

        var result = await writer.WriteAndVerifyAsync(Task(identifiers), CancellationToken.None);
        identifiers["SN"] = "changed-after-call";

        Assert.Equal(IdentifierWriteStatus.Succeeded, result.Status);
        Assert.True(result.IsReadBackConsistent);
        Assert.Null(result.Error);
        Assert.Equal("SN0001", result.RequestedIdentifiers["SN"]);
        Assert.Equal("SN0001", result.ReadBackIdentifiers["SN"]);
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<string, string>)result.RequestedIdentifiers)["SN"] = "mutated");
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<string, string>)result.ReadBackIdentifiers)["SN"] = "mutated");
        Assert.Equal("simulator-2.1", result.ToolVersion);
        Assert.Equal("android", result.DevicePlatform);
        Assert.True(result.IsSimulation);
        Assert.Equal(StartedAtUtc, result.StartedAtUtc);
        Assert.Equal(TimeSpan.Zero, result.CompletedAtUtc.Offset);
        Assert.True(result.CompletedAtUtc >= result.StartedAtUtc);
    }

    [Fact]
    public async Task Failure_ReturnsStructuredRetryableExecutionError()
    {
        var result = await Writer(SimulatedIdentifierWriteScenario.Failure)
            .WriteAndVerifyAsync(Task(), CancellationToken.None);

        Assert.Equal(IdentifierWriteStatus.Failed, result.Status);
        Assert.Equal(DeviceErrorCodes.ExecutionFailed, result.Error?.Code);
        Assert.True(result.Error?.Retryable);
        Assert.False(result.IsReadBackConsistent);
    }

    [Fact]
    public async Task ReadBackMismatch_ReturnsFailureAndMismatchDiagnostic()
    {
        var result = await Writer(SimulatedIdentifierWriteScenario.ReadBackMismatch)
            .WriteAndVerifyAsync(Task(), CancellationToken.None);

        Assert.Equal(IdentifierWriteStatus.Failed, result.Status);
        Assert.Equal(DeviceErrorCodes.ReadBackMismatch, result.Error?.Code);
        Assert.False(result.IsReadBackConsistent);
        Assert.Equal("SIM_WRITER_MISMATCH", result.Error?.DiagnosticCode);
    }

    [Fact]
    public async Task ConfiguredMismatchedReadBack_CannotProduceSuccess()
    {
        var writer = new SimulatedIdentifierWriter(
            new SimulatedIdentifierWriterOptions
            {
                Scenario = SimulatedIdentifierWriteScenario.Success,
                ReadBackIdentifiers = new Dictionary<string, string> { ["SN"] = "other" }
            },
            new FixedClock(StartedAtUtc));

        var result = await writer.WriteAndVerifyAsync(Task(), CancellationToken.None);

        Assert.Equal(IdentifierWriteStatus.Failed, result.Status);
        Assert.Equal(DeviceErrorCodes.ReadBackMismatch, result.Error?.Code);
    }

    [Fact]
    public async Task Uncertain_ReturnsUnknownResultForManualReview()
    {
        var result = await Writer(SimulatedIdentifierWriteScenario.Uncertain)
            .WriteAndVerifyAsync(Task(), CancellationToken.None);

        Assert.Equal(IdentifierWriteStatus.Uncertain, result.Status);
        Assert.Equal(DeviceErrorCodes.Uncertain, result.Error?.Code);
        Assert.False(result.Error?.Retryable);
        Assert.False(result.IsReadBackConsistent);
    }

    [Theory]
    [MemberData(nameof(InvalidTasks))]
    public async Task InvalidTask_ThrowsStructuredConfigurationError(IdentifierWriteTask task)
    {
        var exception = await Assert.ThrowsAsync<DeviceAdapterException>(() =>
            Writer(SimulatedIdentifierWriteScenario.Success)
                .WriteAndVerifyAsync(task, CancellationToken.None));

        Assert.Equal(DeviceErrorCodes.InvalidConfiguration, exception.Error.Code);
    }

    [Fact]
    public async Task Cancellation_IsPropagatedBeforeExecution()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Writer(SimulatedIdentifierWriteScenario.Success)
                .WriteAndVerifyAsync(Task(), cancellation.Token));
    }

    [Fact]
    public async Task Cancellation_DuringExecutionIsPropagated()
    {
        using var writer = new SimulatedIdentifierWriter(new SimulatedIdentifierWriterOptions
        {
            ExecutionDelay = TimeSpan.FromSeconds(1),
            Timeout = TimeSpan.FromSeconds(2)
        });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            writer.WriteAndVerifyAsync(Task(), cancellation.Token));
    }

    [Fact]
    public async Task ExecutionTimeout_ReturnsFailedResultWithRetryableTimeoutError()
    {
        using var writer = new SimulatedIdentifierWriter(new SimulatedIdentifierWriterOptions
        {
            ExecutionDelay = TimeSpan.FromMilliseconds(50),
            Timeout = TimeSpan.FromMilliseconds(1)
        });

        var result = await writer.WriteAndVerifyAsync(Task(), CancellationToken.None);

        Assert.Equal(IdentifierWriteStatus.Failed, result.Status);
        Assert.Equal(DeviceErrorCodes.Timeout, result.Error?.Code);
        Assert.Equal("SIM_WRITER_TIMEOUT", result.Error?.DiagnosticCode);
        Assert.True(result.Error?.Retryable);
        Assert.True(result.CompletedAtUtc >= result.StartedAtUtc);
    }

    [Fact]
    public async Task UnknownScenario_ThrowsStructuredConfigurationError()
    {
        using var writer = new SimulatedIdentifierWriter(new SimulatedIdentifierWriterOptions
        {
            Scenario = (SimulatedIdentifierWriteScenario)999
        });

        var exception = await Assert.ThrowsAsync<DeviceAdapterException>(() =>
            writer.WriteAndVerifyAsync(Task(), CancellationToken.None));

        Assert.Equal(DeviceErrorCodes.InvalidConfiguration, exception.Error.Code);
        Assert.Equal("SIM_WRITER_OPTIONS", exception.Error.DiagnosticCode);
    }

    [Fact]
    public async Task Constructor_CopiesMutableReadBackConfiguration()
    {
        var readBack = new Dictionary<string, string>
        {
            ["IMEI1"] = "860000000000001",
            ["SN"] = "SN0001"
        };
        using var writer = new SimulatedIdentifierWriter(new SimulatedIdentifierWriterOptions
        {
            ReadBackIdentifiers = readBack
        });
        readBack["SN"] = "changed";
        readBack.Clear();

        var result = await writer.WriteAndVerifyAsync(Task(), CancellationToken.None);

        Assert.Equal(IdentifierWriteStatus.Succeeded, result.Status);
        Assert.Equal("SN0001", result.ReadBackIdentifiers["SN"]);
    }

    [Theory]
    [InlineData(-1, 100)]
    [InlineData(0, 0)]
    public async Task InvalidTimingOptions_ThrowStructuredConfigurationError(
        int delayMilliseconds,
        int timeoutMilliseconds)
    {
        using var writer = new SimulatedIdentifierWriter(new SimulatedIdentifierWriterOptions
        {
            ExecutionDelay = TimeSpan.FromMilliseconds(delayMilliseconds),
            Timeout = TimeSpan.FromMilliseconds(timeoutMilliseconds)
        });

        var exception = await Assert.ThrowsAsync<DeviceAdapterException>(() =>
            writer.WriteAndVerifyAsync(Task(), CancellationToken.None));

        Assert.Equal(DeviceErrorCodes.InvalidConfiguration, exception.Error.Code);
    }

    [Fact]
    public async Task ExcessiveTimingOptions_ThrowStructuredConfigurationError()
    {
        using var writer = new SimulatedIdentifierWriter(new SimulatedIdentifierWriterOptions
        {
            Timeout = TimeSpan.MaxValue
        });

        var exception = await Assert.ThrowsAsync<DeviceAdapterException>(() =>
            writer.WriteAndVerifyAsync(Task(), CancellationToken.None));

        Assert.Equal(DeviceErrorCodes.InvalidConfiguration, exception.Error.Code);
    }

    [Fact]
    public async Task Dispose_RejectsFurtherWritesThroughInterfaceContract()
    {
        IIdentifierWriter writer = Writer(SimulatedIdentifierWriteScenario.Success);
        writer.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            writer.WriteAndVerifyAsync(Task(), CancellationToken.None));
    }

    [Fact]
    public async Task ClockMovingBackward_StillProducesOrderedUtcTimestamps()
    {
        var writer = new SimulatedIdentifierWriter(
            new SimulatedIdentifierWriterOptions(),
            new SequenceClock(
                StartedAtUtc,
                StartedAtUtc.AddSeconds(-1)));

        var result = await writer.WriteAndVerifyAsync(Task(), CancellationToken.None);

        Assert.Equal(TimeSpan.Zero, result.StartedAtUtc.Offset);
        Assert.Equal(TimeSpan.Zero, result.CompletedAtUtc.Offset);
        Assert.Equal(result.StartedAtUtc, result.CompletedAtUtc);
    }

    public static TheoryData<IdentifierWriteTask> InvalidTasks => new()
    {
        Task() with { TaskId = " " },
        Task() with { DevicePlatform = " " },
        Task(new Dictionary<string, string>()),
        Task(new Dictionary<string, string> { ["SN"] = " " })
    };

    private static SimulatedIdentifierWriter Writer(SimulatedIdentifierWriteScenario scenario) =>
        new(
            new SimulatedIdentifierWriterOptions
            {
                AdapterId = "writer-01",
                ToolVersion = "simulator-2.1",
                Scenario = scenario
            },
            new FixedClock(StartedAtUtc));

    private static IdentifierWriteTask Task(IReadOnlyDictionary<string, string>? identifiers = null) =>
        new()
        {
            TaskId = "write-task-1",
            DevicePlatform = "android",
            Identifiers = identifiers ?? new Dictionary<string, string>
            {
                ["IMEI1"] = "860000000000001",
                ["SN"] = "SN0001"
            }
        };

    private sealed class FixedClock(DateTimeOffset utcNow) : IUtcClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class SequenceClock(params DateTimeOffset[] values) : IUtcClock
    {
        private int _index;

        public DateTimeOffset UtcNow => values[Math.Min(_index++, values.Length - 1)];
    }
}
