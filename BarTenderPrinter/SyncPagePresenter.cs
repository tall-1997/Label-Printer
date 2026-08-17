using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BarTenderPrinter
{
    internal enum SyncConnectionState
    {
        NotConfigured,
        Ready,
        Running,
        NeedsAttention
    }

    internal sealed class SyncPageState
    {
        public SyncConnectionState ConnectionState { get; init; } = SyncConnectionState.NotConfigured;
        public string WorkspaceName { get; init; } = "尚未配置协作空间";
        public string DeviceName { get; init; } = Environment.MachineName;
        public string ActiveChannel { get; init; } = "WebDAV";
        public DateTimeOffset? LastSuccessfulSyncUtc { get; init; }
        public int PendingEventCount { get; init; }
        public long PendingBytes { get; init; }
        public int DeviceCount { get; init; }
        public int DirectDeviceCount { get; init; }
        public int ConflictCount { get; init; }
        public int QuarantinedObjectCount { get; init; }
        public int BlockedOutboxCount { get; init; }
        public bool IsBusy { get; init; }
        public bool DirectSyncEnabled { get; init; }
        public int DirectSyncPort { get; init; } = 45873;
        public string StatusText { get; init; } = "配置连接后可开始加密同步。";
        public string LastError { get; init; } = string.Empty;
        public IReadOnlyList<SyncDeviceState> Devices { get; init; } = Array.Empty<SyncDeviceState>();
        public IReadOnlyList<SyncConflictStateItem> Conflicts { get; init; } = Array.Empty<SyncConflictStateItem>();
        public SyncUsage Usage { get; init; } = new SyncUsage();
        public IReadOnlyList<SyncActivityState> RecentActivities { get; init; } = Array.Empty<SyncActivityState>();
    }

    internal sealed class SyncDeviceState
    {
        public string DeviceId { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public bool DirectSyncEnabled { get; init; }
        public int AddressCount { get; init; }
        public string LastResult { get; init; } = string.Empty;
        public DateTimeOffset UpdatedAtUtc { get; init; }
        public override string ToString() => $"{DisplayName} · {(DirectSyncEnabled ? $"可直连，{AddressCount} 个地址" : "WebDAV")} · {LastResult}";
    }

    internal sealed class SyncConflictStateItem
    {
        public string ConflictId { get; init; } = string.Empty;
        public string EntityType { get; init; } = string.Empty;
        public string EntityId { get; init; } = string.Empty;
        public DateTimeOffset CreatedAtUtc { get; init; }
        public override string ToString() => $"{EntityType} / {EntityId} · {CreatedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}";
    }

    internal sealed class SyncActivityState
    {
        public string Description { get; init; } = string.Empty;
        public DateTimeOffset OccurredAtUtc { get; init; }
        public override string ToString() => $"{OccurredAtUtc.ToLocalTime():MM-dd HH:mm}  {Description}";
    }

    internal sealed class SyncOperationResult
    {
        private SyncOperationResult(bool succeeded, string message)
        {
            Succeeded = succeeded;
            Message = message ?? string.Empty;
        }

        public bool Succeeded { get; }
        public string Message { get; }

        public static SyncOperationResult Success(string message) => new SyncOperationResult(true, message);
        public static SyncOperationResult Failure(string message) => new SyncOperationResult(false, message);
    }

    internal sealed class SyncConnectionRequest
    {
        public string WebDavUrl { get; init; } = string.Empty;
        public string Account { get; init; } = string.Empty;
        public string ApplicationPassword { get; init; } = string.Empty;
        public string WorkspaceName { get; init; } = string.Empty;
        public string SharedPassword { get; init; } = string.Empty;
    }

    internal interface ISyncPageService
    {
        Task<SyncPageState> GetStateAsync(CancellationToken cancellationToken);
        Task<SyncOperationResult> SynchronizeAsync(CancellationToken cancellationToken);
        Task<SyncOperationResult> CancelAsync(CancellationToken cancellationToken);
        Task<SyncOperationResult> CreateWorkspaceAsync(SyncConnectionRequest request, CancellationToken cancellationToken);
        Task<SyncOperationResult> ImportConnectionAsync(string filePath, string sharedPassword, CancellationToken cancellationToken);
        Task<SyncOperationResult> ExportConnectionAsync(string filePath, string sharedPassword, CancellationToken cancellationToken);
        Task<SyncOperationResult> TestWebDavAsync(SyncConnectionRequest request, CancellationToken cancellationToken);
        Task<SyncOperationResult> ConfigureDirectSyncAsync(bool enabled, int port, CancellationToken cancellationToken);
        Task<SyncOperationResult> PublishDirectEndpointAsync(CancellationToken cancellationToken);
        Task<SyncOperationResult> TestDirectConnectionAsync(string deviceId, CancellationToken cancellationToken);
        Task<SyncOperationResult> ResolveConflictAsync(string conflictId, string resolution, CancellationToken cancellationToken);
        Task<SyncOperationResult> ExportDiagnosticsAsync(string filePath, CancellationToken cancellationToken);
    }

    internal interface ISyncLifecycleService
    {
        bool IsConfigured { get; }
        event EventHandler<SharedDataChangedEventArgs> SharedDataChanged;
        Task StartAsync(CancellationToken cancellationToken);
        Task QueueLocalChangesAsync(CancellationToken cancellationToken);
        Task<bool> CancelAndWaitAsync(TimeSpan timeout);
        Task<bool> FlushAndStopAsync(TimeSpan timeout);
    }

    internal sealed class SharedDataChangedEventArgs : EventArgs
    {
        public SharedDataChangedEventArgs(IReadOnlyCollection<string> entityTypes) => EntityTypes = entityTypes ?? Array.Empty<string>();
        public IReadOnlyCollection<string> EntityTypes { get; }
    }

    internal sealed class SyncPagePresenter : IDisposable
    {
        private readonly ISyncPageService _service;
        private readonly SynchronizationContext _uiContext;
        private readonly object _operationGate = new object();
        private CancellationTokenSource _operationCancellation;
        private Task _currentOperation = Task.CompletedTask;
        private bool _disposed;
        private string _lastOperationMessage = string.Empty;
        private string _lastOperationError = string.Empty;

        public SyncPagePresenter(ISyncPageService service, SynchronizationContext uiContext = null)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _uiContext = uiContext ?? SynchronizationContext.Current ?? new SynchronizationContext();
        }

        public SyncPageState State { get; private set; } = new SyncPageState();
        public event Action<SyncPageState> StateChanged;

        public async Task InitializeAsync()
        {
            if (_service is ISyncLifecycleService lifecycle)
                await lifecycle.StartAsync(CancellationToken.None).ConfigureAwait(false);
            State = await _service.GetStateAsync(CancellationToken.None).ConfigureAwait(false) ?? new SyncPageState();
            PublishState();
        }

        public async Task RefreshAsync()
        {
            var refreshed = await _service.GetStateAsync(CancellationToken.None).ConfigureAwait(false) ?? new SyncPageState();
            State = CopyState(refreshed, refreshed.IsBusy,
                string.IsNullOrWhiteSpace(_lastOperationMessage) ? refreshed.StatusText : _lastOperationMessage,
                string.IsNullOrWhiteSpace(_lastOperationError) ? refreshed.LastError : _lastOperationError);
            PublishState();
        }

        public async Task<SyncOperationResult> ExecuteAsync(
            Func<ISyncPageService, CancellationToken, Task<SyncOperationResult>> operation)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            CancellationTokenSource localCancellation;
            Task<SyncOperationResult> currentTask;
            lock (_operationGate)
            {
                if (_disposed) return SyncOperationResult.Failure("同步中心已关闭。");
                if (!_currentOperation.IsCompleted) return SyncOperationResult.Failure("已有同步操作正在运行。可先取消当前操作。");
                localCancellation = new CancellationTokenSource();
                _operationCancellation = localCancellation;
                currentTask = ExecuteOperationCoreAsync(operation, localCancellation.Token);
                _currentOperation = currentTask;
            }
            State = CopyState(isBusy: true, statusText: "正在处理同步操作...", lastError: string.Empty);
            PublishState();
            var result = await currentTask.ConfigureAwait(false);
            SyncPageState refreshed;
            try
            {
                refreshed = await _service.GetStateAsync(CancellationToken.None).ConfigureAwait(false) ?? new SyncPageState();
            }
            catch (Exception ex)
            {
                refreshed = State;
                if (result.Succeeded) result = SyncOperationResult.Failure($"刷新同步状态失败：{ex.Message}");
            }
            lock (_operationGate)
            {
                if (ReferenceEquals(_currentOperation, currentTask))
                {
                    _currentOperation = Task.CompletedTask;
                    _operationCancellation = null;
                }
            }
            localCancellation.Dispose();
            _lastOperationMessage = result.Message;
            _lastOperationError = result.Succeeded ? string.Empty : result.Message;
            State = CopyState(refreshed, false, result.Message, result.Succeeded ? refreshed.LastError : result.Message);
            PublishState();
            return result;
        }

        private async Task<SyncOperationResult> ExecuteOperationCoreAsync(
            Func<ISyncPageService, CancellationToken, Task<SyncOperationResult>> operation, CancellationToken cancellationToken)
        {
            try { return await operation(_service, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { return SyncOperationResult.Failure("操作已取消，已提交的本地状态保持不变。"); }
            catch (Exception ex) { return SyncOperationResult.Failure($"同步操作失败：{ex.Message}"); }
        }

        public async Task<SyncOperationResult> CancelAsync()
        {
            CancellationTokenSource cancellation;
            lock (_operationGate) cancellation = _operationCancellation;
            try { cancellation?.Cancel(); } catch (ObjectDisposedException) { }
            var result = await _service.CancelAsync(CancellationToken.None).ConfigureAwait(false);
            var refreshed = await _service.GetStateAsync(CancellationToken.None).ConfigureAwait(false) ?? new SyncPageState();
            _lastOperationMessage = result.Message;
            _lastOperationError = result.Succeeded ? string.Empty : result.Message;
            State = CopyState(refreshed, refreshed.IsBusy, result.Message,
                result.Succeeded ? refreshed.LastError : result.Message);
            PublishState();
            return result;
        }

        public async Task<bool> CancelAndWaitAsync(TimeSpan timeout)
        {
            Task operation;
            CancellationTokenSource cancellation;
            lock (_operationGate)
            {
                operation = _currentOperation;
                cancellation = _operationCancellation;
            }
            try { cancellation?.Cancel(); } catch (ObjectDisposedException) { }
            if (_service is ISyncLifecycleService lifecycle) await lifecycle.CancelAndWaitAsync(timeout).ConfigureAwait(false);
            if (operation.IsCompleted) return true;
            return ReferenceEquals(await Task.WhenAny(operation, Task.Delay(timeout)).ConfigureAwait(false), operation);
        }

        private SyncPageState CopyState(bool isBusy, string statusText, string lastError) => CopyState(State, isBusy, statusText, lastError);

        private static SyncPageState CopyState(SyncPageState source, bool isBusy, string statusText, string lastError) => new SyncPageState
        {
            ConnectionState = isBusy ? SyncConnectionState.Running : source.ConnectionState,
            WorkspaceName = source.WorkspaceName, DeviceName = source.DeviceName, ActiveChannel = source.ActiveChannel,
            LastSuccessfulSyncUtc = source.LastSuccessfulSyncUtc, PendingEventCount = source.PendingEventCount,
            PendingBytes = source.PendingBytes, DeviceCount = source.DeviceCount, DirectDeviceCount = source.DirectDeviceCount,
            ConflictCount = source.ConflictCount, QuarantinedObjectCount = source.QuarantinedObjectCount, BlockedOutboxCount = source.BlockedOutboxCount,
            IsBusy = isBusy,
            DirectSyncEnabled = source.DirectSyncEnabled, DirectSyncPort = source.DirectSyncPort,
            StatusText = statusText,
            LastError = lastError,
            Devices = source.Devices, Conflicts = source.Conflicts, Usage = source.Usage, RecentActivities = source.RecentActivities
        };

        private void PublishState()
        {
            lock (_operationGate) if (_disposed) return;
            var state = State;
            _uiContext.Post(_ => StateChanged?.Invoke(state), null);
        }

        public void Dispose()
        {
            CancellationTokenSource cancellation;
            lock (_operationGate)
            {
                if (_disposed) return;
                _disposed = true;
                cancellation = _operationCancellation;
            }
            try { cancellation?.Cancel(); } catch (ObjectDisposedException) { }
            if (_service is IDisposable disposable) disposable.Dispose();
        }
    }

    internal static class SyncLayoutPolicy
    {
        public static int GetMetricColumnCount(int availableWidth, int dpi)
        {
            if (dpi <= 0) throw new ArgumentOutOfRangeException(nameof(dpi));
            if (availableWidth >= Scale(920, dpi)) return 4;
            if (availableWidth >= Scale(620, dpi)) return 2;
            return 1;
        }

        public static bool UseTwoColumnContent(int availableWidth, int dpi)
        {
            if (dpi <= 0) throw new ArgumentOutOfRangeException(nameof(dpi));
            return availableWidth >= Scale(920, dpi);
        }

        private static int Scale(int value, int dpi) => (int)Math.Round(value * dpi / 96F);
    }
}
