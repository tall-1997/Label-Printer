using System.Collections.ObjectModel;
using BarTenderPrinter.Domain.Common;

namespace BarTenderPrinter.Devices;

public enum SimulatedIdentifierWriteScenario
{
    Success,
    Failure,
    ReadBackMismatch,
    Uncertain
}

public sealed record SimulatedIdentifierWriterOptions
{
    public string AdapterId { get; init; } = "simulated-identifier-writer";
    public string ToolVersion { get; init; } = "simulator-1.0";
    public SimulatedIdentifierWriteScenario Scenario { get; init; }
        = SimulatedIdentifierWriteScenario.Success;
    public IReadOnlyDictionary<string, string>? ReadBackIdentifiers { get; init; }
    public TimeSpan ExecutionDelay { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
}

public sealed class SimulatedIdentifierWriter : IIdentifierWriter
{
    private static readonly TimeSpan MaximumDelay = TimeSpan.FromMilliseconds(uint.MaxValue - 1);
    private readonly SimulatedIdentifierWriteScenario _scenario;
    private readonly IReadOnlyDictionary<string, string>? _readBackIdentifiers;
    private readonly TimeSpan _executionDelay;
    private readonly TimeSpan _timeout;
    private readonly IUtcClock _clock;
    private bool _disposed;

    public SimulatedIdentifierWriter(
        SimulatedIdentifierWriterOptions options,
        IUtcClock? clock = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _clock = clock ?? SystemUtcClock.Instance;
        AdapterId = RequireValue(options.AdapterId, nameof(options.AdapterId));
        ToolVersion = RequireValue(options.ToolVersion, nameof(options.ToolVersion));
        _scenario = options.Scenario;
        _readBackIdentifiers = options.ReadBackIdentifiers is null
            ? null
            : Snapshot(options.ReadBackIdentifiers);
        _executionDelay = options.ExecutionDelay;
        _timeout = options.Timeout;
    }

    public string AdapterId { get; }
    public string ToolVersion { get; }
    public bool IsSimulation => true;

    public async Task<IdentifierWriteResult> WriteAndVerifyAsync(
        IdentifierWriteTask task,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateTask(task);
        ValidateOptions();

        var startedAtUtc = UtcNow();
        var requested = Snapshot(task.Identifiers);
        if (_executionDelay >= _timeout)
        {
            await Task.Delay(_timeout, cancellationToken).ConfigureAwait(false);
            return CreateResult(
                task,
                requested,
                Snapshot(new Dictionary<string, string>()),
                IdentifierWriteStatus.Failed,
                new DeviceError(
                    DeviceErrorCodes.Timeout,
                    "写号工具执行超时。",
                    true,
                    "SIM_WRITER_TIMEOUT"),
                startedAtUtc);
        }

        await Task.Delay(_executionDelay, cancellationToken).ConfigureAwait(false);
        var readBack = CreateReadBack(requested);
        var status = _scenario switch
        {
            SimulatedIdentifierWriteScenario.Success => IdentifierWriteStatus.Succeeded,
            SimulatedIdentifierWriteScenario.Uncertain => IdentifierWriteStatus.Uncertain,
            SimulatedIdentifierWriteScenario.Failure or
            SimulatedIdentifierWriteScenario.ReadBackMismatch => IdentifierWriteStatus.Failed,
            _ => throw InvalidOptions("SIM_WRITER_SCENARIO")
        };
        var error = CreateError();
        var result = CreateResult(task, requested, readBack, status, error, startedAtUtc);

        if (result.Status == IdentifierWriteStatus.Succeeded && !result.IsReadBackConsistent)
        {
            result = CreateResult(
                task,
                requested,
                readBack,
                IdentifierWriteStatus.Failed,
                new DeviceError(
                    DeviceErrorCodes.ReadBackMismatch,
                    "写号回读结果与任务快照不一致。",
                    false,
                    "SIM_WRITER_MISMATCH"),
                startedAtUtc);
        }

        return result;
    }

    public void Dispose() => _disposed = true;

    private IReadOnlyDictionary<string, string> CreateReadBack(
        IReadOnlyDictionary<string, string> requested)
    {
        if (_scenario == SimulatedIdentifierWriteScenario.Failure ||
            _scenario == SimulatedIdentifierWriteScenario.Uncertain)
        {
            return Snapshot(_readBackIdentifiers ?? new Dictionary<string, string>());
        }

        if (_readBackIdentifiers is not null)
        {
            return Snapshot(_readBackIdentifiers);
        }

        if (_scenario == SimulatedIdentifierWriteScenario.ReadBackMismatch)
        {
            var mismatched = requested.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            var firstKey = mismatched.Keys.First();
            mismatched[firstKey] += "-mismatch";
            return Snapshot(mismatched);
        }

        return Snapshot(requested);
    }

    private DeviceError? CreateError() => _scenario switch
    {
        SimulatedIdentifierWriteScenario.Failure => new DeviceError(
            DeviceErrorCodes.ExecutionFailed,
            "写号工具执行失败。",
            true,
            "SIM_WRITER_FAILED"),
        SimulatedIdentifierWriteScenario.ReadBackMismatch => new DeviceError(
            DeviceErrorCodes.ReadBackMismatch,
            "写号回读结果与任务快照不一致。",
            false,
            "SIM_WRITER_MISMATCH"),
        SimulatedIdentifierWriteScenario.Uncertain => new DeviceError(
            DeviceErrorCodes.Uncertain,
            "写号工具结果未知，需要人工核查。",
            false,
            "SIM_WRITER_UNCERTAIN"),
        _ => null
    };

    private IdentifierWriteResult CreateResult(
        IdentifierWriteTask task,
        IReadOnlyDictionary<string, string> requested,
        IReadOnlyDictionary<string, string> readBack,
        IdentifierWriteStatus status,
        DeviceError? error,
        DateTimeOffset startedAtUtc)
    {
        if ((status == IdentifierWriteStatus.Succeeded) != (error is null))
        {
            throw new InvalidOperationException("写号结果状态与错误信息不一致。");
        }

        var completedAtUtc = UtcNow();
        if (completedAtUtc < startedAtUtc)
        {
            completedAtUtc = startedAtUtc;
        }

        return new IdentifierWriteResult
        {
            TaskId = task.TaskId.Trim(),
            Status = status,
            RequestedIdentifiers = requested,
            ReadBackIdentifiers = readBack,
            ToolVersion = ToolVersion,
            DevicePlatform = task.DevicePlatform.Trim(),
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            IsSimulation = true,
            Error = error
        };
    }

    private void ValidateOptions()
    {
        if (!Enum.IsDefined(_scenario) ||
            _executionDelay < TimeSpan.Zero ||
            _executionDelay > MaximumDelay ||
            _timeout <= TimeSpan.Zero ||
            _timeout > MaximumDelay)
        {
            throw InvalidOptions("SIM_WRITER_OPTIONS");
        }
    }

    private static DeviceAdapterException InvalidOptions(string diagnosticCode) =>
        new(new DeviceError(
            DeviceErrorCodes.InvalidConfiguration,
            "写号模拟器配置无效。",
            false,
            diagnosticCode));

    private static void ValidateTask(IdentifierWriteTask task)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (string.IsNullOrWhiteSpace(task.TaskId) ||
            string.IsNullOrWhiteSpace(task.DevicePlatform) ||
            task.Identifiers is null ||
            task.Identifiers.Count == 0 ||
            task.Identifiers.Any(pair =>
                string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value)))
        {
            throw new DeviceAdapterException(new DeviceError(
                DeviceErrorCodes.InvalidConfiguration,
                "写号任务配置无效。",
                false,
                "SIM_WRITER_TASK"));
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

    private static IReadOnlyDictionary<string, string> Snapshot(
        IReadOnlyDictionary<string, string> values) =>
        new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(values, StringComparer.Ordinal));

    private static string RequireValue(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("值不能为空。", parameterName)
            : value.Trim();
}
