using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BarTenderPrinter;
using Xunit;

namespace BarTenderPrinter.Tests
{
    public sealed class SyncCoordinatorTests
    {
        [Fact]
        public async Task SynchronizeAppliesMissingEventsAndTreatsExistingIdenticalOutboxAsSuccess()
        {
            var remoteContent = Encoding.UTF8.GetBytes("remote-event");
            var localContent = Encoding.UTF8.GetBytes("local-event");
            var cloud = new FakeCloud
            {
                Objects =
                {
                    ["BarTenderPrinterSync/spaces/space/events/remote/1.evt"] = remoteContent,
                    ["BarTenderPrinterSync/spaces/space/events/local/1.evt"] = localContent
                }
            };
            var store = CreateStore();
            store.Enqueue(new SyncOutboxItem
            {
                EventId = "local:1",
                DeviceId = "local",
                Sequence = 1,
                ObjectPath = "BarTenderPrinterSync/spaces/space/events/local/1.evt",
                EncryptedBlob = localContent,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            var applier = new FakeApplier();
            var coordinator = new SyncCoordinator(
                new SyncConnectionProfile { SpaceId = "space", DataKey = new byte[32] },
                "local",
                cloud,
                store,
                new FakeCodec(),
                applier);

            var result = await coordinator.SynchronizeAsync(false, CancellationToken.None);

            Assert.Equal(2, result.AppliedEvents);
            Assert.Equal(1, result.UploadedEvents);
            Assert.Empty(store.GetPendingOutbox(10));
        }

        [Fact]
        public async Task SynchronizeFallsBackToCloudAfterDirectFailure()
        {
            var cloud = new FakeCloud();
            var direct = new FakeDirectSync();
            var coordinator = new SyncCoordinator(
                new SyncConnectionProfile { SpaceId = "space", DataKey = new byte[32] },
                "local",
                cloud,
                CreateStore(),
                new FakeCodec(),
                new FakeApplier(),
                direct);

            var result = await coordinator.SynchronizeAsync(true, CancellationToken.None);

            Assert.True(result.FellBackToWebDav);
            Assert.Equal(1, direct.Calls);
            Assert.Equal(1, cloud.ListCalls);
        }

        [Fact]
        public async Task SynchronizeWaitsForSequenceGapAndRecoversWhenEarlierEventArrives()
        {
            var cloud = new FakeCloud();
            cloud.Objects["BarTenderPrinterSync/spaces/space/events/remote/2.evt"] = Encoding.UTF8.GetBytes("two");
            var store = CreateStore();
            var coordinator = new SyncCoordinator(new SyncConnectionProfile { SpaceId = "space", DataKey = new byte[32] },
                "local", cloud, store, new FakeCodec(), new FakeApplier());

            var first = await coordinator.SynchronizeAsync(false, CancellationToken.None);
            cloud.Objects["BarTenderPrinterSync/spaces/space/events/remote/1.evt"] = Encoding.UTF8.GetBytes("one");
            var second = await coordinator.SynchronizeAsync(false, CancellationToken.None);

            Assert.Equal(0, first.AppliedEvents);
            Assert.Equal(0, store.GetCursor("missing-device"));
            Assert.Equal(2, second.AppliedEvents);
            Assert.Equal(2, store.GetCursor("remote"));
        }

        [Fact]
        public async Task SynchronizeMergesDirectAndCloudDuplicatesIntoContinuousSequence()
        {
            var cloud = new FakeCloud
            {
                Objects =
                {
                    ["BarTenderPrinterSync/spaces/space/events/remote/1.evt"] = Encoding.UTF8.GetBytes("one"),
                    ["BarTenderPrinterSync/spaces/space/events/remote/2.evt"] = Encoding.UTF8.GetBytes("two")
                }
            };
            var direct = new FakeDirectSync
            {
                Result = new DirectSyncResult
                {
                    Succeeded = true,
                    DownloadedObjects = new[]
                    {
                        new RemoteSyncObject { Path = "BarTenderPrinterSync/spaces/space/events/remote/2.evt", Content = Encoding.UTF8.GetBytes("two") }
                    }
                }
            };
            var store = CreateStore();
            var coordinator = new SyncCoordinator(new SyncConnectionProfile { SpaceId = "space", DataKey = new byte[32] },
                "local", cloud, store, new FakeCodec(), new FakeApplier(), direct);

            var result = await coordinator.SynchronizeAsync(true, CancellationToken.None);

            Assert.Equal(2, result.AppliedEvents);
            Assert.Equal(2, store.GetCursor("remote"));
        }

        [Fact]
        public async Task CorruptDeviceStopsAtBadSequenceWhileHealthyDeviceContinues()
        {
            var cloud = new FakeCloud();
            cloud.Objects["BarTenderPrinterSync/spaces/space/events/device-a/1.evt"] = Encoding.UTF8.GetBytes("bad");
            cloud.Objects["BarTenderPrinterSync/spaces/space/events/device-a/2.evt"] = Encoding.UTF8.GetBytes("two");
            cloud.Objects["BarTenderPrinterSync/spaces/space/events/device-b/1.evt"] = Encoding.UTF8.GetBytes("one");
            var store = CreateStore();
            var coordinator = new SyncCoordinator(new SyncConnectionProfile { SpaceId = "space", DataKey = new byte[32] },
                "local", cloud, store, new SelectiveCodec("device-a:1"), new FakeApplier());

            var result = await coordinator.SynchronizeAsync(false, CancellationToken.None);

            Assert.Equal(1, result.AppliedEvents);
            Assert.Equal(1, result.QuarantinedObjects);
            Assert.Equal(0, store.GetCursor("device-a"));
            Assert.Equal(1, store.GetCursor("device-b"));
            Assert.Single(store.GetQuarantinedObjects());
        }

        [Fact]
        public async Task UploadRetryUsesRetryAfterAndContinuesOtherItems()
        {
            var now = new DateTimeOffset(2026, 8, 17, 10, 0, 0, TimeSpan.Zero);
            var cloud = new FakeCloud { PutFailurePath = "BarTenderPrinterSync/spaces/space/events/local/1.evt", PutFailure = new WebDavException(SyncErrorCodes.WebDavRateLimited, "limited", retryAfter: TimeSpan.FromMinutes(2)) };
            var store = CreateStore();
            store.Enqueue(Outbox(1));
            store.Enqueue(Outbox(2));
            var coordinator = new SyncCoordinator(new SyncConnectionProfile { SpaceId = "space", DataKey = new byte[32] },
                "local", cloud, store, new FakeCodec(), new FakeApplier(), utcNow: () => now, random: () => 0.5);

            var result = await coordinator.SynchronizeAsync(false, CancellationToken.None);

            Assert.Equal(1, result.UploadedEvents);
            Assert.Equal(now.AddMinutes(2), store.GetOutbox("local:1").NextAttemptAtUtc);
            Assert.Empty(store.GetPendingOutbox(10, now));
        }

        [Fact]
        public async Task UploadRetryExponentialDelayIsCapped()
        {
            var now = new DateTimeOffset(2026, 8, 17, 10, 0, 0, TimeSpan.Zero);
            var cloud = new FakeCloud { PutFailurePath = "BarTenderPrinterSync/spaces/space/events/local/1.evt", PutFailure = new WebDavException(SyncErrorCodes.NetworkUnavailable, "offline") };
            var store = CreateStore();
            var item = Outbox(1);
            item.RetryCount = 20;
            store.Enqueue(item);
            var coordinator = new SyncCoordinator(new SyncConnectionProfile { SpaceId = "space", DataKey = new byte[32] },
                "local", cloud, store, new FakeCodec(), new FakeApplier(), utcNow: () => now, random: () => 1,
                initialRetryDelay: TimeSpan.FromSeconds(1), maximumRetryDelay: TimeSpan.FromMinutes(5));

            await coordinator.SynchronizeAsync(false, CancellationToken.None);

            Assert.Equal(now.AddMinutes(5), store.GetOutbox("local:1").NextAttemptAtUtc);
        }

        [Fact]
        public async Task AuthenticationFailurePermanentlyBlocksOnlyFailedItem()
        {
            var cloud = new FakeCloud { PutFailurePath = "BarTenderPrinterSync/spaces/space/events/local/1.evt", PutFailure = new WebDavException(SyncErrorCodes.WebDavAuthenticationFailed, "auth", System.Net.HttpStatusCode.Unauthorized) };
            var store = CreateStore();
            store.Enqueue(Outbox(1));
            store.Enqueue(Outbox(2));
            var coordinator = new SyncCoordinator(new SyncConnectionProfile { SpaceId = "space", DataKey = new byte[32] },
                "local", cloud, store, new FakeCodec(), new FakeApplier());

            var result = await coordinator.SynchronizeAsync(false, CancellationToken.None);

            Assert.Equal(1, result.BlockedUploads);
            Assert.True(store.GetOutbox("local:1").PermanentFailure);
            Assert.Equal(SyncOutboxState.Uploaded, store.GetOutbox("local:2").State);
        }

        [Fact]
        public async Task ExistingRemoteObjectWithDifferentDigestIsPermanentlyBlocked()
        {
            var cloud = new FakeCloud();
            cloud.Objects["BarTenderPrinterSync/spaces/space/events/local/1.evt"] = Encoding.UTF8.GetBytes("different");
            var store = CreateStore();
            store.Enqueue(Outbox(1));
            var coordinator = new SyncCoordinator(new SyncConnectionProfile { SpaceId = "space", DataKey = new byte[32] },
                "local", cloud, store, new FakeCodec(), new FakeApplier());

            var result = await coordinator.SynchronizeAsync(false, CancellationToken.None);

            Assert.Equal(1, result.BlockedUploads);
            Assert.True(store.GetOutbox("local:1").PermanentFailure);
            Assert.Equal(SyncErrorCodes.ObjectCorrupted, store.GetOutbox("local:1").LastErrorCode);
        }

        private static SyncOutboxItem Outbox(long sequence) => new SyncOutboxItem
        {
            EventId = $"local:{sequence}", DeviceId = "local", Sequence = sequence,
            ObjectPath = $"BarTenderPrinterSync/spaces/space/events/local/{sequence}.evt", EncryptedBlob = Encoding.UTF8.GetBytes($"event-{sequence}"), CreatedAtUtc = DateTimeOffset.UtcNow
        };

        private static SyncStore CreateStore()
        {
            var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BarTenderPrinterTests", Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);
            return new SyncStore(System.IO.Path.Combine(directory, "sync.db"));
        }

        private sealed class FakeCloud : ICloudObjectStore
        {
            public Dictionary<string, byte[]> Objects { get; } = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            public int ListCalls { get; private set; }
            public string PutFailurePath { get; set; }
            public Exception PutFailure { get; set; }

            public Task EnsureCollectionAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;

            public Task<IReadOnlyList<CloudObjectMetadata>> ListAsync(string prefix, CancellationToken cancellationToken = default)
            {
                ListCalls++;
                return Task.FromResult<IReadOnlyList<CloudObjectMetadata>>(Objects
                    .Where(item => item.Key.StartsWith(prefix, StringComparison.Ordinal))
                    .Select(item => new CloudObjectMetadata
                    {
                        Path = item.Key,
                        ContentLength = item.Value.LongLength
                    }).ToArray());
            }

            public Task<CloudObject> GetAsync(string path, CancellationToken cancellationToken = default) =>
                Task.FromResult(new CloudObject { Content = Objects[path], Metadata = new CloudObjectMetadata { Path = path } });

            public Task<CloudObjectMetadata> HeadAsync(string path, CancellationToken cancellationToken = default) =>
                Task.FromResult(new CloudObjectMetadata { Path = path, ContentLength = Objects[path].LongLength });

            public Task<CloudObjectMetadata> PutAsync(string path, byte[] content, string ifMatch = null, bool createOnly = false, CancellationToken cancellationToken = default)
            {
                if (string.Equals(path, PutFailurePath, StringComparison.Ordinal)) throw PutFailure;
                if (createOnly && Objects.ContainsKey(path)) throw new WebDavPreconditionFailedException(System.Net.HttpStatusCode.PreconditionFailed);
                Objects[path] = content;
                return Task.FromResult(new CloudObjectMetadata { Path = path, ContentLength = content.LongLength });
            }

            public void Dispose() { }
        }

        private sealed class FakeApplier : ISyncEventApplier
        {
            public Task<IReadOnlyDictionary<string, long>> GetCursorsAsync(SyncStore store, CancellationToken cancellationToken) =>
                Task.FromResult(store.GetCursors());

            public Task<SyncApplyOutcome> ApplyAtomicallyAsync(SyncEvent syncEvent, SyncStore store, CancellationToken cancellationToken)
            {
                var inserted = store.RecordAppliedEvent(syncEvent.EventId, syncEvent.DeviceId, syncEvent.Sequence);
                return Task.FromResult(new SyncApplyOutcome
                {
                    Result = inserted ? SyncEventApplyResult.Applied : SyncEventApplyResult.AlreadyApplied
                });
            }
        }

        private sealed class FakeCodec : ISyncObjectCodec
        {
            public SyncEvent DecodeEvent(SyncConnectionProfile profile, string objectPath, byte[] encryptedBlob)
            {
                var parts = objectPath.Split('/');
                var device = parts[^2];
                var sequence = long.Parse(System.IO.Path.GetFileNameWithoutExtension(parts[^1]));
                return new SyncEvent
                {
                    EventId = $"{device}:{sequence}",
                    DeviceId = device,
                    Sequence = sequence,
                    EntityType = "Order",
                    EntityId = $"order-{device}",
                    BaseVersion = 0,
                    NewVersion = 1,
                    OccurredAtUtc = DateTime.UtcNow,
                    PayloadJson = "{}"
                };
            }
        }

        private sealed class SelectiveCodec : ISyncObjectCodec
        {
            private readonly string _corruptEventId;
            public SelectiveCodec(string corruptEventId) { _corruptEventId = corruptEventId; }
            public SyncEvent DecodeEvent(SyncConnectionProfile profile, string objectPath, byte[] encryptedBlob)
            {
                var parts = objectPath.Split('/');
                var device = parts[^2];
                var sequence = long.Parse(System.IO.Path.GetFileNameWithoutExtension(parts[^1]));
                var eventId = $"{device}:{sequence}";
                if (eventId == _corruptEventId) throw new SyncSecurityException(SyncErrorCodes.ObjectCorrupted, "corrupt");
                return new SyncEvent { EventId = eventId, DeviceId = device, Sequence = sequence, EntityType = "Order", EntityId = eventId, BaseVersion = 0, NewVersion = 1 };
            }
        }

        private sealed class FakeDirectSync : IDirectSyncTransport
        {
            public int Calls { get; private set; }
            public DirectSyncResult Result { get; set; } = new DirectSyncResult();

            public Task<DirectSyncResult> TrySynchronizeAsync(SyncConnectionProfile profile, string localDeviceId, IReadOnlyDictionary<string, long> cursors,
                IReadOnlyList<SyncOutboxItem> outbox, CancellationToken cancellationToken)
            {
                Calls++;
                return Task.FromResult(Result);
            }
        }
    }
}
