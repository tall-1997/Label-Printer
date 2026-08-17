using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BarTenderPrinter;
using Xunit;

namespace BarTenderPrinter.Tests
{
    public sealed class DirectSyncTests
    {
        [Fact]
        public async Task ClientOnlyConnectsValidPublishedEndpointsInPriorityOrder()
        {
            var connector = new RecordingConnector();
            var endpoints = new FakeEndpointSource(new[]
            {
                Endpoint("expired", "10.0.0.1", 900, DateTime.UtcNow.AddMinutes(-1)),
                Endpoint("peer-low", "10.0.0.2", 10, DateTime.UtcNow.AddHours(1)),
                Endpoint("peer-high", "10.0.0.3", 100, DateTime.UtcNow.AddHours(1))
            });
            var client = new DirectSyncClient(endpoints, connector, TimeSpan.FromSeconds(1));

            var result = await client.TrySynchronizeAsync(
                new SyncConnectionProfile
                {
                    SpaceId = "space",
                    DataKey = new byte[32]
                },
                "local",
                new Dictionary<string, long>(),
                Array.Empty<SyncOutboxItem>(),
                CancellationToken.None);

            Assert.True(result.Succeeded, result.SafeErrorCode);
            Assert.Single(connector.Attempts);
            Assert.Equal("10.0.0.3", connector.Attempts[0]);
        }

        [Fact]
        public async Task AuthenticationFailureStopsFurtherEndpointAttempts()
        {
            var connector = new RecordingConnector { AuthenticationFailure = true };
            var endpoints = new FakeEndpointSource(new[]
            {
                Endpoint("peer", "10.0.0.3", 100, DateTime.UtcNow.AddHours(1)),
                Endpoint("peer", "10.0.0.4", 90, DateTime.UtcNow.AddHours(1))
            });
            var client = new DirectSyncClient(endpoints, connector);

            var result = await client.TrySynchronizeAsync(
                new SyncConnectionProfile { SpaceId = "space", DataKey = new byte[32] },
                "local",
                new Dictionary<string, long>(), Array.Empty<SyncOutboxItem>(), CancellationToken.None);

            Assert.True(result.AuthenticationFailed, result.SafeErrorCode);
            Assert.Single(connector.Attempts);
        }

        [Fact]
        public async Task HostAndClientExchangeMissingEncryptedEventsOverLoopback()
        {
            var directory = TestDirectory();
            var key = SyncCrypto.GenerateDataKey();
            var hostProfile = Profile("host", key);
            var clientProfile = Profile("client", key);
            var hostStore = new SyncStore(Path.Combine(directory, "host.db"));
            var clientStore = new SyncStore(Path.Combine(directory, "client.db"));
            var hostEvent = EventItem(hostProfile, 1, Encoding.UTF8.GetBytes("host"));
            var clientEvent = EventItem(clientProfile, 1, Encoding.UTF8.GetBytes("client"));
            hostStore.Enqueue(hostEvent);
            clientStore.Enqueue(clientEvent);
            await using var host = new DirectSyncHost(hostProfile, hostStore, CreateCertificate("host"), new[] { IPAddress.Loopback });
            await host.StartAsync(0, CancellationToken.None);
            var endpoint = new PublishedDirectEndpoint
            {
                DeviceId = "host", Address = IPAddress.Loopback.ToString(), Port = host.Port, Priority = 1,
                CertificateSha256 = host.CertificateSha256, ExpiresAtUtc = DateTime.UtcNow.AddHours(1), Enabled = true
            };
            var client = new DirectSyncClient(new FakeEndpointSource(new[] { endpoint }));

            var result = await client.TrySynchronizeAsync(clientProfile, "client", clientStore.GetCursors(),
                clientStore.GetPendingOutbox(10), CancellationToken.None);

            Assert.True(result.Succeeded, result.SafeErrorCode);
            Assert.Single(result.DownloadedObjects);
            Assert.Equal(hostEvent.ObjectPath, result.DownloadedObjects[0].Path);
            Assert.NotNull(hostStore.GetOutbox(clientEvent.EventId));
        }

        [Fact]
        public async Task HostRejectsClientWithDifferentSpaceKey()
        {
            var directory = TestDirectory();
            var hostProfile = Profile("host", SyncCrypto.GenerateDataKey());
            await using var host = new DirectSyncHost(hostProfile, new SyncStore(Path.Combine(directory, "host.db")),
                CreateCertificate("host"), new[] { IPAddress.Loopback });
            await host.StartAsync(0, CancellationToken.None);
            var endpoint = Endpoint("host", IPAddress.Loopback.ToString(), 1, DateTime.UtcNow.AddHours(1));
            endpoint = new PublishedDirectEndpoint
            {
                DeviceId = endpoint.DeviceId, Address = endpoint.Address, Port = host.Port, Priority = endpoint.Priority,
                CertificateSha256 = host.CertificateSha256, ExpiresAtUtc = endpoint.ExpiresAtUtc, Enabled = true
            };

            var result = await new DirectSyncClient(new FakeEndpointSource(new[] { endpoint })).TrySynchronizeAsync(
                Profile("client", SyncCrypto.GenerateDataKey()), "client", new Dictionary<string, long>(),
                Array.Empty<SyncOutboxItem>(), CancellationToken.None);

            Assert.True(result.AuthenticationFailed, result.SafeErrorCode);
        }

        [Fact]
        public void CertificateStorePersistsProtectedPrivateKey()
        {
            var path = Path.Combine(TestDirectory(), "certificate.dat");
            var protector = new RecordingProtector();
            using var first = new DirectSyncCertificateStore(path, protector).LoadOrCreate("device");
            using var second = new DirectSyncCertificateStore(path, protector).LoadOrCreate("device");

            Assert.True(first.HasPrivateKey);
            Assert.Equal(first.Thumbprint, second.Thumbprint);
            Assert.True(protector.ProtectCalls > 0);
            Assert.True(protector.UnprotectCalls > 0);
            Assert.False(first.Export(X509ContentType.Pfx).SequenceEqual(File.ReadAllBytes(path)));
        }

        [Fact]
        public async Task EndpointRegistryDecryptsEveryValidDeviceAndRejectsExpiredRecord()
        {
            var profile = Profile("local", SyncCrypto.GenerateDataKey());
            var cloud = new EndpointCloud();
            var store = new SyncStore(Path.Combine(TestDirectory(), "sync.db"));
            AddEndpoint(cloud, profile, "valid", DateTimeOffset.UtcNow.AddHours(1));
            AddEndpoint(cloud, profile, "expired", DateTimeOffset.UtcNow.AddMinutes(-1));
            var source = new DeviceEndpointRegistry(cloud, profile, store);

            var endpoints = await source.GetPublishedEndpointsAsync(profile.SpaceId, profile.DeviceId, CancellationToken.None);

            Assert.Single(endpoints);
            Assert.Equal("valid", endpoints[0].DeviceId);
            Assert.NotNull(store.GetKnownDevice("valid"));
            Assert.Null(store.GetKnownDevice("expired"));
        }

        private static SyncConnectionProfile Profile(string deviceId, byte[] key) => new SyncConnectionProfile
        {
            SpaceId = "space", DeviceId = deviceId, DataKey = key
        };

        private static SyncOutboxItem EventItem(SyncConnectionProfile profile, long sequence, byte[] payload)
        {
            var syncEvent = new SyncEvent
            {
                EventId = $"{profile.DeviceId}:{sequence}", DeviceId = profile.DeviceId, Sequence = sequence,
                EntityType = "Orders", EntityId = "orders", BaseVersion = 0, NewVersion = 1,
                OccurredAtUtc = DateTime.UtcNow, PayloadJson = Convert.ToBase64String(payload)
            };
            return new SyncOutboxItem
            {
                EventId = syncEvent.EventId, DeviceId = profile.DeviceId, Sequence = sequence,
                ObjectPath = $"BarTenderPrinterSync/spaces/{profile.SpaceId}/events/{profile.DeviceId}/{sequence}.evt",
                EncryptedBlob = SyncCrypto.EncryptObject(JsonSerializer.SerializeToUtf8Bytes(syncEvent), profile.DataKey,
                    profile.SpaceId, "event", syncEvent.EventId), CreatedAtUtc = DateTimeOffset.UtcNow
            };
        }

        private static X509Certificate2 CreateCertificate(string deviceId)
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest($"CN={deviceId}", key, HashAlgorithmName.SHA256);
            using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
            return new X509Certificate2(generated.Export(X509ContentType.Pfx), (string)null,
                X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
        }

        private static string TestDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), "BarTenderPrinterDirectSyncTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void AddEndpoint(EndpointCloud cloud, SyncConnectionProfile profile, string deviceId, DateTimeOffset expiresAt)
        {
            var publishedAt = expiresAt.AddHours(-1);
            var record = new DeviceEndpointRecord
            {
                SpaceId = profile.SpaceId, DeviceId = deviceId, DisplayName = deviceId, EndpointVersion = 1,
                DirectSyncEnabled = true, Port = 45873, CertificateSha256 = new string('A', 64),
                Addresses = new[] { new LocalEndpointAddress { Value = "10.0.0.1", Family = "IPv4", InterfaceType = "Ethernet", Priority = 1 } },
                PublishedAtUtc = publishedAt, ExpiresAtUtc = expiresAt
            };
            var plaintext = JsonSerializer.SerializeToUtf8Bytes(record);
            cloud.Objects[$"BarTenderPrinterSync/spaces/{profile.SpaceId}/devices/{deviceId}.enc"] =
                SyncCrypto.EncryptObject(plaintext, profile.DataKey, profile.SpaceId, "device", deviceId);
        }

        private static PublishedDirectEndpoint Endpoint(string deviceId, string address, int priority, DateTime expiresAtUtc) =>
            new PublishedDirectEndpoint
            {
                DeviceId = deviceId,
                Address = address,
                Port = 45873,
                Priority = priority,
                CertificateSha256 = new string('A', 64),
                ExpiresAtUtc = expiresAtUtc,
                Enabled = true
            };

        private sealed class FakeEndpointSource : IPublishedEndpointSource
        {
            private readonly IReadOnlyList<PublishedDirectEndpoint> _endpoints;
            public FakeEndpointSource(IReadOnlyList<PublishedDirectEndpoint> endpoints) => _endpoints = endpoints;
            public Task<IReadOnlyList<PublishedDirectEndpoint>> GetPublishedEndpointsAsync(string spaceId, string localDeviceId, CancellationToken cancellationToken) =>
                Task.FromResult(_endpoints);
        }

        private sealed class RecordingConnector : IDirectSyncConnector
        {
            public List<string> Attempts { get; } = new List<string>();
            public bool AuthenticationFailure { get; init; }

            public Task<IDirectSyncSession> ConnectAsync(PublishedDirectEndpoint endpoint, SyncConnectionProfile profile, string localDeviceId, CancellationToken cancellationToken)
            {
                Attempts.Add(endpoint.Address);
                if (AuthenticationFailure) throw new DirectSyncAuthenticationException("test");
                return Task.FromResult<IDirectSyncSession>(new SuccessfulSession());
            }
        }

        private sealed class SuccessfulSession : IDirectSyncSession
        {
            public Task<DirectSyncResult> SynchronizeAsync(SyncConnectionProfile profile, IReadOnlyDictionary<string, long> cursors,
                IReadOnlyList<SyncOutboxItem> outbox, CancellationToken cancellationToken) =>
                Task.FromResult(new DirectSyncResult { Succeeded = true });

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        private sealed class RecordingProtector : ILocalSecretProtector
        {
            public int ProtectCalls { get; private set; }
            public int UnprotectCalls { get; private set; }
            public byte[] Protect(byte[] plaintext) { ProtectCalls++; return plaintext.Select(value => (byte)(value ^ 0x5a)).ToArray(); }
            public byte[] Unprotect(byte[] protectedBytes) { UnprotectCalls++; return protectedBytes.Select(value => (byte)(value ^ 0x5a)).ToArray(); }
        }

        private sealed class EndpointCloud : ICloudObjectStore
        {
            public Dictionary<string, byte[]> Objects { get; } = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            public Task EnsureCollectionAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task<IReadOnlyList<CloudObjectMetadata>> ListAsync(string path, CancellationToken cancellationToken = default) =>
                Task.FromResult<IReadOnlyList<CloudObjectMetadata>>(Objects.Keys.Where(key => key.StartsWith(path, StringComparison.Ordinal))
                    .Select(key => new CloudObjectMetadata { Path = key }).ToArray());
            public Task<CloudObject> GetAsync(string path, CancellationToken cancellationToken = default) =>
                Task.FromResult(new CloudObject { Content = Objects[path], Metadata = new CloudObjectMetadata { Path = path } });
            public Task<CloudObjectMetadata> HeadAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task<CloudObjectMetadata> PutAsync(string path, byte[] content, string ifMatch = null, bool createOnly = false, CancellationToken cancellationToken = default)
            {
                Objects[path] = content;
                return Task.FromResult(new CloudObjectMetadata { Path = path });
            }
            public void Dispose() { }
        }
    }
}
