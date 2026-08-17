using System;
using System.Threading;
using System.Threading.Tasks;
using BarTenderPrinter;
using Xunit;

namespace BarTenderPrinter.Tests
{
    public sealed class SyncPagePresenterTests
    {
        [Fact]
        public async Task ExecuteRefreshesStateAndKeepsOperationMessage()
        {
            var service = new FakeSyncPageService
            {
                State = new SyncPageState { PendingEventCount = 3, StatusText = "服务状态" }
            };
            using var presenter = new SyncPagePresenter(service, new ImmediateSynchronizationContext());

            var result = await presenter.ExecuteAsync((_, _) =>
                Task.FromResult(SyncOperationResult.Success("操作完成")));

            Assert.True(result.Succeeded);
            Assert.Equal(1, service.GetStateCount);
            Assert.Equal(3, presenter.State.PendingEventCount);
            Assert.Equal("操作完成", presenter.State.StatusText);
            Assert.Equal(string.Empty, presenter.State.LastError);
        }

        [Fact]
        public async Task ExecuteRefreshesStateAndKeepsFailureAsLastError()
        {
            var service = new FakeSyncPageService
            {
                State = new SyncPageState { ConflictCount = 2, LastError = "旧错误" }
            };
            using var presenter = new SyncPagePresenter(service, new ImmediateSynchronizationContext());

            var result = await presenter.ExecuteAsync((_, _) =>
                Task.FromResult(SyncOperationResult.Failure("远端不可用")));

            Assert.False(result.Succeeded);
            Assert.Equal(2, presenter.State.ConflictCount);
            Assert.Equal("远端不可用", presenter.State.StatusText);
            Assert.Equal("远端不可用", presenter.State.LastError);
        }

        [Fact]
        public async Task CancelAndWaitCancelsCurrentLocalOperation()
        {
            var service = new FakeSyncPageService();
            using var presenter = new SyncPagePresenter(service, new ImmediateSynchronizationContext());
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var operation = presenter.ExecuteAsync(async (_, token) =>
            {
                started.TrySetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return SyncOperationResult.Success("unexpected");
            });
            await started.Task;

            var completed = await presenter.CancelAndWaitAsync(TimeSpan.FromSeconds(1));
            var result = await operation;

            Assert.True(completed);
            Assert.False(result.Succeeded);
            Assert.Contains("取消", result.Message);
        }

        private sealed class ImmediateSynchronizationContext : SynchronizationContext
        {
            public override void Post(SendOrPostCallback callback, object state) => callback(state);
        }

        private sealed class FakeSyncPageService : ISyncPageService
        {
            public SyncPageState State { get; set; } = new SyncPageState();
            public int GetStateCount { get; private set; }
            public Task<SyncPageState> GetStateAsync(CancellationToken cancellationToken)
            {
                GetStateCount++;
                return Task.FromResult(State);
            }
            public Task<SyncOperationResult> SynchronizeAsync(CancellationToken cancellationToken) => Success();
            public Task<SyncOperationResult> CancelAsync(CancellationToken cancellationToken) => Success();
            public Task<SyncOperationResult> CreateWorkspaceAsync(SyncConnectionRequest request, CancellationToken cancellationToken) => Success();
            public Task<SyncOperationResult> ImportConnectionAsync(string filePath, string sharedPassword, CancellationToken cancellationToken) => Success();
            public Task<SyncOperationResult> ExportConnectionAsync(string filePath, string sharedPassword, CancellationToken cancellationToken) => Success();
            public Task<SyncOperationResult> TestWebDavAsync(SyncConnectionRequest request, CancellationToken cancellationToken) => Success();
            public Task<SyncOperationResult> ConfigureDirectSyncAsync(bool enabled, int port, CancellationToken cancellationToken) => Success();
            public Task<SyncOperationResult> PublishDirectEndpointAsync(CancellationToken cancellationToken) => Success();
            public Task<SyncOperationResult> TestDirectConnectionAsync(string deviceId, CancellationToken cancellationToken) => Success();
            public Task<SyncOperationResult> ResolveConflictAsync(string conflictId, string resolution, CancellationToken cancellationToken) => Success();
            public Task<SyncOperationResult> ExportDiagnosticsAsync(string filePath, CancellationToken cancellationToken) => Success();
            private static Task<SyncOperationResult> Success() => Task.FromResult(SyncOperationResult.Success("ok"));
        }
    }
}
