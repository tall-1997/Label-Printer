using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BarTenderPrinter;
using Xunit;

namespace BarTenderPrinter.Tests
{
    public sealed class SyncApplicationServiceTests
    {
        [Fact]
        public async Task CreateWorkspaceCreatesRemoteContractAndPersistsProfile()
        {
            var fixture = new Fixture(false);
            var result = await fixture.Service.CreateWorkspaceAsync(new SyncConnectionRequest
            {
                WebDavUrl = "https://dav.example.test/root/", Account = "account",
                ApplicationPassword = "application-password", WorkspaceName = "Production", SharedPassword = "offline-password"
            }, CancellationToken.None);

            Assert.True(result.Succeeded, result.Message);
            Assert.NotNull(fixture.Profiles.Saved);
            Assert.False(string.IsNullOrWhiteSpace(fixture.Profiles.Saved.DeviceId));
            Assert.Contains($"BarTenderPrinterSync/spaces/{fixture.Profiles.Saved.SpaceId}/space.enc", fixture.Cloud.Objects.Keys);
            Assert.Contains($"BarTenderPrinterSync/spaces/{fixture.Profiles.Saved.SpaceId}/snapshots", fixture.Cloud.Collections);
        }

        [Fact]
        public async Task SynchronizeQueuesChangedSnapshotOnlyOnce()
        {
            var content = JsonSerializer.SerializeToUtf8Bytes(new[] { new { Id = "order-1" } });
            var fixture = new Fixture(true, Snapshot(content));

            var first = await fixture.Service.SynchronizeAsync(CancellationToken.None);
            var firstCount = fixture.Cloud.Objects.Keys.Count(path => path.EndsWith(".evt", StringComparison.Ordinal));
            var second = await fixture.Service.SynchronizeAsync(CancellationToken.None);

            Assert.True(first.Succeeded, first.Message);
            Assert.True(second.Succeeded, second.Message);
            Assert.Equal(1, firstCount);
            Assert.Equal(firstCount, fixture.Cloud.Objects.Keys.Count(path => path.EndsWith(".evt", StringComparison.Ordinal)));
            Assert.Empty(fixture.Store.GetPendingOutbox(10));
        }

        [Fact]
        public async Task WorkspaceCreatorFirstSynchronizationUploadsLocalBaseline()
        {
            var content = JsonSerializer.SerializeToUtf8Bytes(new[] { new { Id = "creator-order" } });
            var fixture = new Fixture(false, Snapshot(content));
            Assert.True((await fixture.Service.CreateWorkspaceAsync(new SyncConnectionRequest
            {
                WebDavUrl = "https://dav.example.test/root/", Account = "account", ApplicationPassword = "password",
                WorkspaceName = "Workspace", SharedPassword = "offline-password"
            }, CancellationToken.None)).Succeeded);

            var result = await fixture.Service.SynchronizeAsync(CancellationToken.None);

            Assert.True(result.Succeeded, result.Message);
            Assert.Single(fixture.Cloud.Objects.Keys, path => path.EndsWith(".evt", StringComparison.Ordinal));
            Assert.True(fixture.Profiles.Saved.RemoteBaselineEstablished == true);
        }

        [Fact]
        public async Task CancelAndWaitSafelyCompletesActiveSynchronization()
        {
            var adapter = new BlockingDataAdapter();
            var fixture = new Fixture(true, dataAdapter: adapter);
            var synchronization = fixture.Service.SynchronizeAsync(CancellationToken.None);
            await adapter.Started.Task;

            var completed = await fixture.Service.CancelAndWaitAsync(TimeSpan.FromSeconds(1));

            Assert.True(completed);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => synchronization);
        }

        [Fact]
        public async Task DisposeDuringSynchronizationDoesNotRaceOperationCleanup()
        {
            var adapter = new BlockingDataAdapter();
            var fixture = new Fixture(true, dataAdapter: adapter);
            var synchronization = fixture.Service.SynchronizeAsync(CancellationToken.None);
            await adapter.Started.Task;

            fixture.Service.Dispose();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => synchronization);
        }

        [Fact]
        public async Task SynchronizeAppliesRemoteOrdersIdempotently()
        {
            var fixture = new Fixture();
            var content = JsonSerializer.SerializeToUtf8Bytes(new[] { new { Id = "remote-order" } });
            var payload = JsonSerializer.Serialize(new SyncSnapshotPayload
            {
                Kind = "Orders", Sha256 = SyncCrypto.ComputeSha256Hex(content), ContentBase64 = Convert.ToBase64String(content)
            });
            var syncEvent = new SyncEvent
            {
                EventId = "remote-device:1", DeviceId = "remote-device", Sequence = 1, EntityType = "Orders", EntityId = "orders",
                BaseVersion = 0, NewVersion = 1, OccurredAtUtc = DateTime.UtcNow, PayloadJson = payload
            };
            var plaintext = JsonSerializer.SerializeToUtf8Bytes(syncEvent);
            fixture.Cloud.Objects[$"BarTenderPrinterSync/spaces/{fixture.Profile.SpaceId}/events/remote-device/1.evt"] =
                SyncCrypto.EncryptObject(plaintext, fixture.Profile.DataKey, fixture.Profile.SpaceId, "event", syncEvent.EventId);

            var first = await fixture.Service.SynchronizeAsync(CancellationToken.None);
            var second = await fixture.Service.SynchronizeAsync(CancellationToken.None);

            Assert.True(first.Succeeded, first.Message);
            Assert.True(second.Succeeded, second.Message);
            Assert.Equal(content, await File.ReadAllBytesAsync(fixture.OrdersPath));
            Assert.True(fixture.Store.IsEventApplied(syncEvent.EventId));
            Assert.Equal(1, fixture.Store.GetCursor("remote-device"));
        }

        [Fact]
        public async Task SynchronizeUploadsTemplateTwiceAndRepairsTamperedCache()
        {
            var template = new byte[] { 1, 3, 5, 7 };
            var hash = SyncCrypto.ComputeSha256Hex(template);
            var fixture = new Fixture(true, new SyncDataSnapshot
            {
                Templates = new[] { new SyncTemplateObject { Content = template, Length = template.Length, Sha256 = hash, SourcePath = "label.btw" } }
            });

            var first = await fixture.Service.SynchronizeAsync(CancellationToken.None);
            var cachePath = Path.Combine(fixture.TemplateCachePath, hash + ".btw");
            await File.WriteAllBytesAsync(cachePath, new byte[] { 9, 9, 9 });
            var second = await fixture.Service.SynchronizeAsync(CancellationToken.None);

            Assert.True(first.Succeeded, first.Message);
            Assert.True(second.Succeeded, second.Message);
            Assert.Equal(template, await File.ReadAllBytesAsync(cachePath));
        }

        [Fact]
        public async Task SynchronizeCreatesEncryptedSnapshotAtInjectedThreshold()
        {
            var content = JsonSerializer.SerializeToUtf8Bytes(new[] { new { Id = "order-1" } });
            var fixture = new Fixture(true, Snapshot(content), snapshotEventThreshold: 1);

            var result = await fixture.Service.SynchronizeAsync(CancellationToken.None);

            Assert.True(result.Succeeded, result.Message);
            Assert.Single(fixture.Cloud.Objects.Keys, path => path.EndsWith(".snap", StringComparison.Ordinal));
            Assert.Contains(fixture.Cloud.Objects.Keys, path => path.EndsWith("snapshot-pointer.enc", StringComparison.Ordinal));
            Assert.DoesNotContain(content, fixture.Cloud.Objects.Values, ByteArrayComparer.Instance);
        }

        [Fact]
        public async Task SnapshotPointerConflictRereadsWinningPointer()
        {
            var fixture = new Fixture(true, Snapshot(JsonSerializer.SerializeToUtf8Bytes(new[] { new { Id = "order-1" } })), snapshotEventThreshold: 1);
            fixture.Cloud.ConflictPointerOnce = true;

            var result = await fixture.Service.SynchronizeAsync(CancellationToken.None);

            Assert.True(result.Succeeded, result.Message);
            Assert.True(fixture.Cloud.PointerGetCount >= 2);
            Assert.Equal(1, fixture.Cloud.PointerConflictCount);
        }

        [Fact]
        public async Task InitialSnapshotRestoreThenAppliesIncrementalEvent()
        {
            var initial = JsonSerializer.SerializeToUtf8Bytes(new[] { new { Id = "snapshot-order" } });
            var source = new Fixture(true, Snapshot(initial), snapshotEventThreshold: 1);
            Assert.True((await source.Service.SynchronizeAsync(CancellationToken.None)).Succeeded);

            var incremental = JsonSerializer.SerializeToUtf8Bytes(new[] { new { Id = "incremental-order" } });
            var payload = JsonSerializer.Serialize(new SyncSnapshotPayload
            {
                Kind = "Orders", Sha256 = SyncCrypto.ComputeSha256Hex(incremental), ContentBase64 = Convert.ToBase64String(incremental)
            });
            var syncEvent = new SyncEvent
            {
                EventId = "remote-device:1", DeviceId = "remote-device", Sequence = 1, EntityType = "Orders", EntityId = "orders",
                BaseVersion = 1, NewVersion = 2, OccurredAtUtc = DateTime.UtcNow, PayloadJson = payload
            };
            source.Cloud.Objects[$"BarTenderPrinterSync/spaces/{source.Profile.SpaceId}/events/remote-device/1.evt"] =
                SyncCrypto.EncryptObject(JsonSerializer.SerializeToUtf8Bytes(syncEvent), source.Profile.DataKey, source.Profile.SpaceId, "event", syncEvent.EventId);
            var target = new Fixture(true, cloud: source.Cloud, profile: source.Profile);

            var result = await target.Service.SynchronizeAsync(CancellationToken.None);

            Assert.True(result.Succeeded, result.Message);
            Assert.Equal(incremental, await File.ReadAllBytesAsync(target.OrdersPath));
            Assert.Equal(1, target.Store.GetCursor("remote-device"));
        }

        [Fact]
        public void EventCodecRejectsPathAndPayloadIdentityMismatch()
        {
            var profile = new SyncConnectionProfile { SpaceId = "space", DataKey = SyncCrypto.GenerateDataKey() };
            var syncEvent = new SyncEvent { EventId = "other:1", DeviceId = "other", Sequence = 1 };
            var encrypted = SyncCrypto.EncryptObject(JsonSerializer.SerializeToUtf8Bytes(syncEvent), profile.DataKey, profile.SpaceId, "event", "device:1");

            Assert.Throws<InvalidDataException>(() => new SyncEventObjectCodec().DecodeEvent(profile,
                "BarTenderPrinterSync/spaces/space/events/device/1.evt", encrypted));
        }

        [Fact]
        public async Task ImportValidatesRemoteDescriptorBeforeReplacingProfile()
        {
            var fixture = new Fixture();
            var imported = new SyncConnectionProfile
            {
                WebDavUrl = fixture.Profile.WebDavUrl, UserName = "account", ApplicationPassword = "password",
                SpaceId = "imported-space", WorkspaceName = "Imported", DataKey = SyncCrypto.GenerateDataKey()
            };
            var filePath = await WriteConnectionFileAsync(imported);
            var descriptor = JsonSerializer.SerializeToUtf8Bytes(new SyncSpaceDescriptor { SchemaVersion = 1, SpaceId = imported.SpaceId, WorkspaceName = imported.WorkspaceName });
            fixture.Cloud.Objects[$"BarTenderPrinterSync/spaces/{imported.SpaceId}/space.enc"] =
                SyncCrypto.EncryptObject(descriptor, imported.DataKey, imported.SpaceId, "space", imported.SpaceId);

            var result = await fixture.Service.ImportConnectionAsync(filePath, "password", CancellationToken.None);

            Assert.True(result.Succeeded, result.Message);
            Assert.Equal(imported.SpaceId, fixture.Profiles.Saved.SpaceId);
            Assert.True(fixture.Profiles.Saved.RemoteBaselineEstablished == true);
            Assert.Contains("首次同步", result.Message);
        }

        [Fact]
        public async Task ImportFirstSynchronizationPullsRemoteWithoutUploadingExistingLocalOrders()
        {
            var local = JsonSerializer.SerializeToUtf8Bytes(new[] { new { Id = "local-default" } });
            var remote = JsonSerializer.SerializeToUtf8Bytes(new[] { new { Id = "remote-order" } });
            var fixture = new Fixture(dataAdapter: new FakeDataAdapter(Snapshot(local)));
            var imported = NewImportedProfile(fixture, "imported-space");
            var filePath = await WriteConnectionFileAsync(imported);
            AddDescriptor(fixture.Cloud, imported);
            AddRemoteOrdersEvent(fixture.Cloud, imported, remote);
            await File.WriteAllBytesAsync(fixture.OrdersPath, local);

            var result = await fixture.Service.ImportConnectionAsync(filePath, "password", CancellationToken.None);

            Assert.True(result.Succeeded, result.Message);
            Assert.Equal(remote, await File.ReadAllBytesAsync(fixture.OrdersPath));
            Assert.DoesNotContain(fixture.Cloud.Objects.Keys, path => path.Contains($"events/{fixture.Profiles.Saved.DeviceId}/", StringComparison.Ordinal));
            Assert.True(fixture.Profiles.Saved.RemoteBaselineEstablished == true);
            Assert.True(fixture.Profiles.Saved.LocalCaptureEnabled == false);
        }

        [Fact]
        public async Task FailedImportedInitialSynchronizationKeepsProfileForRetry()
        {
            var fixture = new Fixture();
            var imported = NewImportedProfile(fixture, "imported-space");
            var filePath = await WriteConnectionFileAsync(imported);
            AddDescriptor(fixture.Cloud, imported);
            fixture.Cloud.FailEventListing = true;

            var result = await fixture.Service.ImportConnectionAsync(filePath, "password", CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Contains("已加入协作空间，但首次同步失败", result.Message);
            Assert.Equal(imported.SpaceId, fixture.Profiles.Saved.SpaceId);
            Assert.True(fixture.Profiles.Saved.RemoteBaselineEstablished == false);
            fixture.Cloud.FailEventListing = false;
            Assert.True((await fixture.Service.SynchronizeAsync(CancellationToken.None)).Succeeded);
            Assert.Empty(fixture.Store.GetPendingOutbox(10));
        }

        [Fact]
        public async Task ResolveConflictRejectsEmptyId()
        {
            var fixture = new Fixture();

            var result = await fixture.Service.ResolveConflictAsync(" ", "保留本地版本", CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Contains("请选择", result.Message);
        }

        [Theory]
        [InlineData("保留本地版本")]
        [InlineData("采用远端版本")]
        public async Task ConflictResolutionCreatesOutboxAndConvergesOnSecondDevice(string resolution)
        {
            var local = JsonSerializer.SerializeToUtf8Bytes(new[] { new { Id = "local-order" } });
            var remote = JsonSerializer.SerializeToUtf8Bytes(new[] { new { Id = "remote-order" } });
            var source = new Fixture(true, Snapshot(local));
            source.Store.UpsertEntityState("Orders", "orders", 1, SyncCrypto.ComputeSha256Hex(local));
            await File.WriteAllBytesAsync(source.OrdersPath, local);
            var remoteEvent = CreateOrdersEvent(source.Profile, "remote-device", 1, remote, 0, 1);
            source.Store.AddConflict(new SyncConflict
            {
                ConflictId = remoteEvent.EventId, EntityType = "Orders", EntityId = "orders",
                LocalJson = JsonSerializer.Serialize(source.Store.GetEntityState("Orders", "orders")),
                RemoteJson = JsonSerializer.Serialize(remoteEvent), CreatedAtUtc = DateTimeOffset.UtcNow
            });

            var resolved = await source.Service.ResolveConflictAsync(remoteEvent.EventId, resolution, CancellationToken.None);

            Assert.True(resolved.Succeeded, resolved.Message);
            var resolutionEvent = Assert.Single(source.Store.GetPendingOutbox(10));
            source.Cloud.Objects[resolutionEvent.ObjectPath] = resolutionEvent.EncryptedBlob;
            var expected = resolution == "采用远端版本" ? remote : local;
            var targetProfile = CloneProfile(source.Profile, "target-device");
            var target = new Fixture(true, Snapshot(expected), cloud: source.Cloud, profile: targetProfile);
            target.Store.UpsertEntityState("Orders", "orders", 1, SyncCrypto.ComputeSha256Hex(remote));
            await File.WriteAllBytesAsync(target.OrdersPath, remote);

            var convergence = await target.Service.SynchronizeAsync(CancellationToken.None);

            Assert.True(convergence.Succeeded, convergence.Message);
            Assert.Equal(expected, await File.ReadAllBytesAsync(target.OrdersPath));
            Assert.Empty(target.Store.GetPendingConflicts());
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task ImportRejectsWrongKeyOrSpaceAndKeepsOldProfile(bool wrongKey)
        {
            var fixture = new Fixture();
            var imported = new SyncConnectionProfile
            {
                WebDavUrl = fixture.Profile.WebDavUrl, UserName = "account", ApplicationPassword = "password",
                SpaceId = "imported-space", WorkspaceName = "Imported", DataKey = SyncCrypto.GenerateDataKey()
            };
            var filePath = await WriteConnectionFileAsync(imported);
            var descriptorSpace = wrongKey ? imported.SpaceId : "different-space";
            var descriptor = JsonSerializer.SerializeToUtf8Bytes(new SyncSpaceDescriptor { SchemaVersion = 1, SpaceId = descriptorSpace, WorkspaceName = imported.WorkspaceName });
            var encryptionKey = wrongKey ? SyncCrypto.GenerateDataKey() : imported.DataKey;
            fixture.Cloud.Objects[$"BarTenderPrinterSync/spaces/{imported.SpaceId}/space.enc"] =
                SyncCrypto.EncryptObject(descriptor, encryptionKey, imported.SpaceId, "space", imported.SpaceId);

            var result = await fixture.Service.ImportConnectionAsync(filePath, "password", CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal(0, fixture.Profiles.SaveCount);
            Assert.Empty(fixture.Cloud.Collections);
        }

        private static async Task<string> WriteConnectionFileAsync(SyncConnectionProfile profile)
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".btpsync");
            await File.WriteAllBytesAsync(path, JsonSerializer.SerializeToUtf8Bytes(profile));
            return path;
        }

        private static SyncConnectionProfile NewImportedProfile(Fixture fixture, string spaceId) => new SyncConnectionProfile
        {
            WebDavUrl = fixture.Profile.WebDavUrl, UserName = "account", ApplicationPassword = "password",
            SpaceId = spaceId, WorkspaceName = "Imported", DataKey = SyncCrypto.GenerateDataKey()
        };

        private static SyncConnectionProfile CloneProfile(SyncConnectionProfile profile, string deviceId) => new SyncConnectionProfile
        {
            WebDavUrl = profile.WebDavUrl, UserName = profile.UserName, ApplicationPassword = profile.ApplicationPassword,
            SpaceId = profile.SpaceId, WorkspaceName = profile.WorkspaceName, DataKey = profile.DataKey.ToArray(), DeviceId = deviceId,
            RemoteBaselineEstablished = true
        };

        private static void AddDescriptor(FakeCloudObjectStore cloud, SyncConnectionProfile profile)
        {
            var descriptor = JsonSerializer.SerializeToUtf8Bytes(new SyncSpaceDescriptor { SchemaVersion = 1, SpaceId = profile.SpaceId, WorkspaceName = profile.WorkspaceName });
            cloud.Objects[$"BarTenderPrinterSync/spaces/{profile.SpaceId}/space.enc"] =
                SyncCrypto.EncryptObject(descriptor, profile.DataKey, profile.SpaceId, "space", profile.SpaceId);
        }

        private static void AddRemoteOrdersEvent(FakeCloudObjectStore cloud, SyncConnectionProfile profile, byte[] content)
        {
            var syncEvent = CreateOrdersEvent(profile, "remote-device", 1, content, 0, 1);
            cloud.Objects[$"BarTenderPrinterSync/spaces/{profile.SpaceId}/events/remote-device/1.evt"] =
                SyncCrypto.EncryptObject(JsonSerializer.SerializeToUtf8Bytes(syncEvent), profile.DataKey, profile.SpaceId, "event", syncEvent.EventId);
        }

        private static SyncEvent CreateOrdersEvent(SyncConnectionProfile profile, string deviceId, long sequence, byte[] content, long baseVersion, long newVersion) => new SyncEvent
        {
            EventId = $"{deviceId}:{sequence}", DeviceId = deviceId, Sequence = sequence, EntityType = "Orders", EntityId = "orders",
            BaseVersion = baseVersion, NewVersion = newVersion, OccurredAtUtc = DateTime.UtcNow,
            PayloadJson = JsonSerializer.Serialize(new SyncSnapshotPayload
            {
                Kind = "Orders", Sha256 = SyncCrypto.ComputeSha256Hex(content), ContentBase64 = Convert.ToBase64String(content)
            })
        };

        private static SyncDataSnapshot Snapshot(byte[] content) => new SyncDataSnapshot
        {
            Files = new[] { new SyncFileSnapshot { Kind = SyncSnapshotKind.Orders, ObjectId = SyncDataAdapter.GetObjectId(SyncSnapshotKind.Orders), Content = content, Sha256 = SyncCrypto.ComputeSha256Hex(content) } }
        };

        private sealed class Fixture
        {
            public Fixture(bool loadProfile = true, SyncDataSnapshot snapshot = null, ISyncDataAdapter dataAdapter = null,
                long snapshotEventThreshold = 500, FakeCloudObjectStore cloud = null, SyncConnectionProfile profile = null)
            {
                var directory = Path.Combine(Path.GetTempPath(), "BarTenderPrinterSyncTests", Guid.NewGuid().ToString("N"));
                System.IO.Directory.CreateDirectory(directory);
                OrdersPath = Path.Combine(directory, "orders.json");
                Profile = profile ?? new SyncConnectionProfile { WebDavUrl = "https://dav.example.test/root/", UserName = "account", ApplicationPassword = "password", SpaceId = "space-1", DeviceId = "local-device", WorkspaceName = "Workspace", DataKey = SyncCrypto.GenerateDataKey() };
                Profiles = new FakeProfileStore(loadProfile ? Profile : null);
                Store = new SyncStore(Path.Combine(directory, "sync.db"));
                Cloud = cloud ?? new FakeCloudObjectStore();
                TemplateCachePath = Path.Combine(directory, "templates");
                Service = new SyncApplicationService(Profiles, Store, dataAdapter ?? new FakeDataAdapter(snapshot), _ => Cloud, OrdersPath,
                    Path.Combine(directory, "settings.json"), Path.Combine(directory, "incoming"), TemplateCachePath, urlPolicy: _ => true,
                    snapshotEventThreshold: snapshotEventThreshold);
            }
            public string OrdersPath { get; }
            public SyncConnectionProfile Profile { get; }
            public FakeProfileStore Profiles { get; }
            public SyncStore Store { get; }
            public string TemplateCachePath { get; }
            public FakeCloudObjectStore Cloud { get; }
            public SyncApplicationService Service { get; }
        }

        private sealed class FakeDataAdapter : ISyncDataAdapter
        {
            private readonly SyncDataSnapshot _snapshot;
            public FakeDataAdapter(SyncDataSnapshot snapshot) { _snapshot = snapshot ?? new SyncDataSnapshot(); }
            public Task<SyncDataSnapshot> CaptureAsync(CancellationToken cancellationToken) => Task.FromResult(_snapshot);
        }

        private sealed class BlockingDataAdapter : ISyncDataAdapter
        {
            public TaskCompletionSource<bool> Started { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public async Task<SyncDataSnapshot> CaptureAsync(CancellationToken cancellationToken)
            {
                Started.TrySetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new SyncDataSnapshot();
            }
        }

        private sealed class FakeProfileStore : ISyncConnectionProfileStore
        {
            private readonly SyncConnectionProfile _loaded;
            public FakeProfileStore(SyncConnectionProfile loaded) { _loaded = loaded; }
            public SyncConnectionProfile Saved { get; private set; }
            public int SaveCount { get; private set; }
            public byte[] Export(SyncConnectionProfile profile, string password) => JsonSerializer.SerializeToUtf8Bytes(profile);
            public SyncConnectionProfile Import(byte[] content, string password) => JsonSerializer.Deserialize<SyncConnectionProfile>(content);
            public void SaveLocal(SyncConnectionProfile profile) { Saved = profile; SaveCount++; }
            public SyncConnectionProfile LoadLocal() => _loaded ?? throw new IOException("missing");
        }

        private sealed class FakeCloudObjectStore : ICloudObjectStore
        {
            public HashSet<string> Collections { get; } = new HashSet<string>(StringComparer.Ordinal);
            public Dictionary<string, byte[]> Objects { get; } = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            public bool ConflictPointerOnce { get; set; }
            public bool FailEventListing { get; set; }
            public int PointerConflictCount { get; private set; }
            public int PointerGetCount { get; private set; }
            public Task EnsureCollectionAsync(string path, CancellationToken token = default) { Collections.Add(path.Trim('/')); return Task.CompletedTask; }
            public Task<IReadOnlyList<CloudObjectMetadata>> ListAsync(string path, CancellationToken token = default)
            {
                if (FailEventListing && path.Contains("/events", StringComparison.Ordinal)) throw new IOException("event listing failed");
                var prefix = path.Trim('/') + "/";
                var result = new Dictionary<string, CloudObjectMetadata>(StringComparer.Ordinal);
                foreach (var item in Objects.Where(item => item.Key.StartsWith(prefix, StringComparison.Ordinal)))
                {
                    var relative = item.Key.Substring(prefix.Length);
                    var slash = relative.IndexOf('/');
                    var child = slash < 0 ? item.Key : prefix + relative.Substring(0, slash);
                    result[child] = new CloudObjectMetadata { Path = child, IsCollection = slash >= 0, ContentLength = slash < 0 ? item.Value.LongLength : 0 };
                }
                return Task.FromResult<IReadOnlyList<CloudObjectMetadata>>(result.Values.ToArray());
            }
            public Task<CloudObject> GetAsync(string path, CancellationToken token = default)
            {
                if (path.EndsWith("snapshot-pointer.enc", StringComparison.Ordinal)) PointerGetCount++;
                if (!Objects.TryGetValue(path, out var content)) throw new WebDavNotFoundException(System.Net.HttpStatusCode.NotFound);
                return Task.FromResult(new CloudObject { Content = content, Metadata = new CloudObjectMetadata { Path = path, ETag = SyncCrypto.ComputeSha256Hex(content) } });
            }
            public Task<CloudObjectMetadata> HeadAsync(string path, CancellationToken token = default) => Task.FromResult(new CloudObjectMetadata { Path = path, ContentLength = Objects[path].LongLength });
            public Task<CloudObjectMetadata> PutAsync(string path, byte[] content, string ifMatch = null, bool createOnly = false, CancellationToken token = default)
            {
                if (createOnly && Objects.ContainsKey(path)) throw new WebDavPreconditionFailedException(System.Net.HttpStatusCode.PreconditionFailed);
                if (ConflictPointerOnce && path.EndsWith("snapshot-pointer.enc", StringComparison.Ordinal) && PointerConflictCount == 0)
                {
                    Objects[path] = content.ToArray();
                    PointerConflictCount++;
                    throw new WebDavPreconditionFailedException(System.Net.HttpStatusCode.PreconditionFailed);
                }
                if (!string.IsNullOrEmpty(ifMatch) && Objects.TryGetValue(path, out var existing) && !string.Equals(ifMatch, SyncCrypto.ComputeSha256Hex(existing), StringComparison.Ordinal))
                    throw new WebDavPreconditionFailedException(System.Net.HttpStatusCode.PreconditionFailed);
                Objects[path] = content.ToArray();
                return Task.FromResult(new CloudObjectMetadata { Path = path, ContentLength = content.LongLength, ETag = SyncCrypto.ComputeSha256Hex(content) });
            }
            public void Dispose() { }
        }

        private sealed class ByteArrayComparer : IEqualityComparer<byte[]>
        {
            public static ByteArrayComparer Instance { get; } = new ByteArrayComparer();
            public bool Equals(byte[] x, byte[] y) => x != null && y != null && x.SequenceEqual(y);
            public int GetHashCode(byte[] obj) => obj?.Length ?? 0;
        }
    }
}
