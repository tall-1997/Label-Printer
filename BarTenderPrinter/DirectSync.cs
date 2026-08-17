using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BarTenderPrinter
{
    public sealed class PublishedDirectEndpoint
    {
        public string DeviceId { get; init; } = "";
        public string Address { get; init; } = "";
        public int Port { get; init; }
        public int Priority { get; init; }
        public string CertificateSha256 { get; init; } = "";
        public DateTime ExpiresAtUtc { get; init; }
        public bool Enabled { get; init; }
    }

    public interface IPublishedEndpointSource
    {
        Task<IReadOnlyList<PublishedDirectEndpoint>> GetPublishedEndpointsAsync(
            string spaceId,
            string localDeviceId,
            CancellationToken cancellationToken);
    }

    public interface IDirectSyncSession : IAsyncDisposable
    {
        Task<DirectSyncResult> SynchronizeAsync(
            SyncConnectionProfile profile,
            IReadOnlyDictionary<string, long> cursors,
            IReadOnlyList<SyncOutboxItem> outbox,
            CancellationToken cancellationToken);
    }

    public interface IDirectSyncConnector
    {
        Task<IDirectSyncSession> ConnectAsync(
            PublishedDirectEndpoint endpoint,
            SyncConnectionProfile profile,
            string localDeviceId,
            CancellationToken cancellationToken);
    }

    public sealed class DirectSyncClient : IDirectSyncTransport
    {
        private readonly IPublishedEndpointSource _endpointSource;
        private readonly IDirectSyncConnector _connector;
        private readonly TimeSpan _endpointTimeout;

        public DirectSyncClient(
            IPublishedEndpointSource endpointSource,
            IDirectSyncConnector connector = null,
            TimeSpan? endpointTimeout = null)
        {
            _endpointSource = endpointSource ?? throw new ArgumentNullException(nameof(endpointSource));
            _connector = connector ?? new TlsDirectSyncConnector();
            _endpointTimeout = endpointTimeout ?? TimeSpan.FromSeconds(3);
            if (_endpointTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(endpointTimeout));
        }

        public async Task<DirectSyncResult> TrySynchronizeAsync(
            SyncConnectionProfile profile,
            string localDeviceId,
            IReadOnlyDictionary<string, long> cursors,
            IReadOnlyList<SyncOutboxItem> outbox,
            CancellationToken cancellationToken)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (profile.DataKey == null || profile.DataKey.Length != 32)
                return new DirectSyncResult { AuthenticationFailed = true };

            var endpoints = await _endpointSource.GetPublishedEndpointsAsync(profile.SpaceId, localDeviceId, cancellationToken).ConfigureAwait(false);
            var candidates = (endpoints ?? Array.Empty<PublishedDirectEndpoint>())
                .Where(endpoint => IsValidPublishedEndpoint(endpoint, localDeviceId, DateTime.UtcNow))
                .OrderByDescending(endpoint => endpoint.Priority)
                .ToArray();
            foreach (var endpoint in candidates)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(_endpointTimeout);
                try
                {
                    await using var session = await _connector.ConnectAsync(endpoint, profile, localDeviceId, timeout.Token).ConfigureAwait(false);
                    return await session.SynchronizeAsync(profile, cursors, outbox, timeout.Token).ConfigureAwait(false);
                }
                catch (DirectSyncAuthenticationException)
                {
                    return new DirectSyncResult { AuthenticationFailed = true };
                }
                catch (Exception ex) when (IsEndpointFailure(ex, cancellationToken))
                {
                    // Continue only through addresses explicitly published for this device.
                }
            }
            return new DirectSyncResult();
        }

        internal static bool IsValidPublishedEndpoint(PublishedDirectEndpoint endpoint, string localDeviceId, DateTime nowUtc)
        {
            if (endpoint == null || !endpoint.Enabled || endpoint.ExpiresAtUtc <= nowUtc || endpoint.Port < 1 || endpoint.Port > 65535)
                return false;
            if (string.IsNullOrWhiteSpace(endpoint.DeviceId) || string.Equals(endpoint.DeviceId, localDeviceId, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(endpoint.Address)) return false;
            return IsSha256(endpoint.CertificateSha256);
        }

        private static bool IsEndpointFailure(Exception exception, CancellationToken outerToken)
        {
            return exception is SocketException || exception is IOException || exception is AuthenticationException ||
                exception is TimeoutException || (exception is OperationCanceledException && !outerToken.IsCancellationRequested);
        }

        private static bool IsSha256(string value)
        {
            return value?.Length == 64 && value.All(character =>
                (character >= '0' && character <= '9') ||
                (character >= 'a' && character <= 'f') ||
                (character >= 'A' && character <= 'F'));
        }
    }

    public sealed class DirectSyncAuthenticationException : Exception
    {
        public DirectSyncAuthenticationException(string message) : base(message) { }
        public DirectSyncAuthenticationException(string message, Exception innerException) : base(message, innerException) { }
    }

    public sealed class TlsDirectSyncConnector : IDirectSyncConnector
    {
        public async Task<IDirectSyncSession> ConnectAsync(
            PublishedDirectEndpoint endpoint,
            SyncConnectionProfile profile,
            string localDeviceId,
            CancellationToken cancellationToken)
        {
            var client = new TcpClient();
            try
            {
                await client.ConnectAsync(endpoint.Address, endpoint.Port, cancellationToken).ConfigureAwait(false);
                var fingerprintMatched = false;
                var ssl = new SslStream(client.GetStream(), false, (_, certificate, _, _) =>
                {
                    if (certificate == null) return false;
                    using var sha256 = SHA256.Create();
                    var actual = Convert.ToHexString(sha256.ComputeHash(certificate.GetRawCertData()));
                    fingerprintMatched = CryptographicOperations.FixedTimeEquals(
                        Encoding.ASCII.GetBytes(actual),
                        Encoding.ASCII.GetBytes(endpoint.CertificateSha256.ToUpperInvariant()));
                    return fingerprintMatched;
                });
                await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = endpoint.DeviceId,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                }, cancellationToken).ConfigureAwait(false);
                if (!fingerprintMatched) throw new DirectSyncAuthenticationException("直连 TLS 证书指纹不匹配。");
                var session = new TlsDirectSyncSession(client, ssl, endpoint);
                await session.AuthenticateAsync(profile, localDeviceId, cancellationToken).ConfigureAwait(false);
                return session;
            }
            catch (Exception ex)
            {
                client.Dispose();
                if (ex is DirectSyncAuthenticationException) throw;
                if (ex is AuthenticationException) throw new DirectSyncAuthenticationException("直连 TLS 身份验证失败。", ex);
                throw;
            }
        }
    }

    internal sealed class TlsDirectSyncSession : IDirectSyncSession
    {
        private const int MaxControlFrameLength = 1024 * 1024;
        private const int MaxObjectLength = 500 * 1024 * 1024;
        private const int ChunkLength = 512 * 1024;
        private readonly TcpClient _client;
        private readonly SslStream _stream;
        private readonly PublishedDirectEndpoint _endpoint;
        private bool _authenticated;

        public TlsDirectSyncSession(TcpClient client, SslStream stream, PublishedDirectEndpoint endpoint)
        {
            _client = client;
            _stream = stream;
            _endpoint = endpoint;
        }

        public async Task AuthenticateAsync(SyncConnectionProfile profile, string localDeviceId, CancellationToken cancellationToken)
        {
            var clientNonce = RandomNumberGenerator.GetBytes(32);
            await WriteMessageAsync(new WireMessage
            {
                Type = "Hello",
                SpaceId = profile.SpaceId,
                DeviceId = localDeviceId,
                Nonce = Convert.ToBase64String(clientNonce)
            }, cancellationToken).ConfigureAwait(false);
            var hello = await ReadMessageAsync(cancellationToken).ConfigureAwait(false);
            if (!string.Equals(hello.Type, "Hello", StringComparison.Ordinal) ||
                !string.Equals(hello.SpaceId, profile.SpaceId, StringComparison.Ordinal) ||
                !string.Equals(hello.DeviceId, _endpoint.DeviceId, StringComparison.Ordinal))
                throw new DirectSyncAuthenticationException("直连设备或空间身份不匹配。");

            byte[] serverNonce;
            byte[] serverProof;
            try
            {
                serverNonce = Convert.FromBase64String(hello.Nonce ?? "");
                serverProof = Convert.FromBase64String(hello.Proof ?? "");
            }
            catch (FormatException ex)
            {
                throw new DirectSyncAuthenticationException("直连认证响应格式无效。", ex);
            }
            var authenticationKey = DeriveAuthenticationKey(profile.DataKey, profile.SpaceId);
            byte[] clientProof;
            try
            {
                if (serverNonce.Length != 32 || !VerifyProof(authenticationKey, "server", profile, localDeviceId, _endpoint.DeviceId, clientNonce, serverNonce, serverProof))
                    throw new DirectSyncAuthenticationException("直连空间共享密钥认证失败。");
                clientProof = CreateProof(authenticationKey, "client", profile, localDeviceId, _endpoint.DeviceId, clientNonce, serverNonce);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(authenticationKey);
            }
            await WriteMessageAsync(new WireMessage
            {
                Type = "Authenticate",
                SpaceId = profile.SpaceId,
                DeviceId = localDeviceId,
                Proof = Convert.ToBase64String(clientProof)
            }, cancellationToken).ConfigureAwait(false);
            var authenticated = await ReadMessageAsync(cancellationToken).ConfigureAwait(false);
            if (!string.Equals(authenticated.Type, "Authenticated", StringComparison.Ordinal))
                throw new DirectSyncAuthenticationException("远端拒绝直连设备认证。");
            _authenticated = true;
        }

        public async Task<DirectSyncResult> SynchronizeAsync(
            SyncConnectionProfile profile,
            IReadOnlyDictionary<string, long> cursors,
            IReadOnlyList<SyncOutboxItem> outbox,
            CancellationToken cancellationToken)
        {
            if (!_authenticated) throw new InvalidOperationException("直连会话尚未认证。");
            var requestId = Guid.NewGuid().ToString("N");
            await WriteMessageAsync(new WireMessage
            {
                Type = "InventoryRequest",
                RequestId = requestId,
                Cursors = new Dictionary<string, long>(cursors ?? new Dictionary<string, long>(), StringComparer.Ordinal),
                Objects = (outbox ?? Array.Empty<SyncOutboxItem>()).Select(item => new WireObject
                {
                    Id = item.ObjectPath,
                    Sha256 = SyncDataAdapter.ComputeSha256(item.EncryptedBlob ?? Array.Empty<byte>()),
                    Length = item.EncryptedBlob?.LongLength ?? 0,
                    EventId = item.EventId
                }).ToArray()
            }, cancellationToken).ConfigureAwait(false);
            var inventory = await ReadMessageAsync(cancellationToken).ConfigureAwait(false);
            if (!string.Equals(inventory.Type, "InventoryResponse", StringComparison.Ordinal) ||
                !string.Equals(inventory.RequestId, requestId, StringComparison.Ordinal))
                throw new IOException("直连清单响应无效。");
            if (inventory.Objects?.Length > 1000 || inventory.RequestedObjectIds?.Length > 1000 ||
                inventory.Objects?.Any(item => item == null) == true)
                throw new IOException("直连清单响应超过限制。");

            var downloaded = new List<RemoteSyncObject>();
            foreach (var item in inventory.Objects ?? Array.Empty<WireObject>())
            {
                ValidateObjectMetadata(item, profile.SpaceId, _endpoint.DeviceId);
                await WriteMessageAsync(new WireMessage { Type = "ObjectRequest", RequestId = requestId, ObjectId = item.Id }, cancellationToken).ConfigureAwait(false);
                var content = await ReadObjectAsync(item, requestId, cancellationToken).ConfigureAwait(false);
                downloaded.Add(new RemoteSyncObject { Path = item.Id, Sha256 = item.Sha256, Content = content });
            }

            var uploaded = new List<string>();
            var requestedIds = new HashSet<string>(inventory.RequestedObjectIds ?? Array.Empty<string>(), StringComparer.Ordinal);
            foreach (var item in (outbox ?? Array.Empty<SyncOutboxItem>()).Where(value => requestedIds.Contains(value.ObjectPath)))
            {
                ValidateUpload(item);
                await WriteObjectAsync(item, requestId, cancellationToken).ConfigureAwait(false);
                uploaded.Add(item.EventId);
            }
            await WriteMessageAsync(new WireMessage { Type = "SyncReceipt", RequestId = requestId }, cancellationToken).ConfigureAwait(false);
            return new DirectSyncResult { Succeeded = true, DownloadedObjects = downloaded, UploadedEventIds = uploaded };
        }

        public async ValueTask DisposeAsync()
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
            _client.Dispose();
        }

        private async Task<byte[]> ReadObjectAsync(WireObject metadata, string requestId, CancellationToken cancellationToken)
        {
            using var buffer = new MemoryStream((int)metadata.Length);
            while (true)
            {
                var frame = await ReadMessageAsync(cancellationToken).ConfigureAwait(false);
                if (!string.Equals(frame.RequestId, requestId, StringComparison.Ordinal) || !string.Equals(frame.ObjectId, metadata.Id, StringComparison.Ordinal))
                    throw new IOException("直连对象响应身份无效。");
                if (string.Equals(frame.Type, "ObjectComplete", StringComparison.Ordinal))
                {
                    if (frame.Length != metadata.Length || !string.Equals(frame.Sha256, metadata.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("直连对象完成帧无效。");
                    break;
                }
                if (!string.Equals(frame.Type, "ObjectChunk", StringComparison.Ordinal)) throw new IOException("直连对象帧类型无效。");
                byte[] chunk;
                try { chunk = Convert.FromBase64String(frame.Content ?? ""); }
                catch (FormatException ex) { throw new IOException("直连对象分块格式无效。", ex); }
                if (chunk.Length == 0 || chunk.Length > ChunkLength || buffer.Length + chunk.Length > metadata.Length)
                    throw new IOException("直连对象分块大小无效。");
                await buffer.WriteAsync(chunk, cancellationToken).ConfigureAwait(false);
            }
            var content = buffer.ToArray();
            if (content.LongLength != metadata.Length || !string.Equals(SyncDataAdapter.ComputeSha256(content), metadata.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("直连对象摘要校验失败。");
            return content;
        }

        private async Task WriteObjectAsync(SyncOutboxItem item, string requestId, CancellationToken cancellationToken)
        {
            for (var offset = 0; offset < item.EncryptedBlob.Length; offset += ChunkLength)
            {
                var length = Math.Min(ChunkLength, item.EncryptedBlob.Length - offset);
                await WriteMessageAsync(new WireMessage
                {
                    Type = "ObjectChunk",
                    RequestId = requestId,
                    ObjectId = item.ObjectPath,
                    Content = Convert.ToBase64String(item.EncryptedBlob, offset, length)
                }, cancellationToken).ConfigureAwait(false);
            }
            await WriteMessageAsync(new WireMessage
            {
                Type = "ObjectComplete",
                RequestId = requestId,
                ObjectId = item.ObjectPath,
                Sha256 = SyncDataAdapter.ComputeSha256(item.EncryptedBlob),
                Length = item.EncryptedBlob.LongLength
            }, cancellationToken).ConfigureAwait(false);
        }

        private async Task WriteMessageAsync(WireMessage message, CancellationToken cancellationToken)
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(message);
            if (payload.Length == 0 || payload.Length > MaxControlFrameLength) throw new IOException("直连控制帧超过限制。");
            var prefix = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(prefix, payload.Length);
            await _stream.WriteAsync(prefix, cancellationToken).ConfigureAwait(false);
            await _stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task<WireMessage> ReadMessageAsync(CancellationToken cancellationToken)
        {
            var prefix = new byte[4];
            await ReadExactlyAsync(prefix, cancellationToken).ConfigureAwait(false);
            var length = BinaryPrimitives.ReadInt32BigEndian(prefix);
            if (length <= 0 || length > MaxControlFrameLength) throw new IOException("直连控制帧长度无效。");
            var payload = new byte[length];
            await ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
            try { return JsonSerializer.Deserialize<WireMessage>(payload) ?? throw new IOException("直连控制帧为空。"); }
            catch (JsonException ex) { throw new IOException("直连控制帧格式无效。", ex); }
        }

        private async Task ReadExactlyAsync(byte[] buffer, CancellationToken cancellationToken)
        {
            var offset = 0;
            while (offset < buffer.Length)
            {
                var read = await _stream.ReadAsync(buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false);
                if (read == 0) throw new EndOfStreamException("直连会话意外关闭。");
                offset += read;
            }
        }

        private static byte[] CreateProof(
            byte[] key,
            string role,
            SyncConnectionProfile profile,
            string localDeviceId,
            string remoteDeviceId,
            byte[] clientNonce,
            byte[] serverNonce)
        {
            var context = string.Join("\n", "BTP-DIRECT-1", role, profile.SpaceId, localDeviceId, remoteDeviceId,
                Convert.ToBase64String(clientNonce), Convert.ToBase64String(serverNonce));
            using var hmac = new HMACSHA256(key);
            return hmac.ComputeHash(Encoding.UTF8.GetBytes(context));
        }

        private static byte[] DeriveAuthenticationKey(byte[] dataKey, string spaceId)
        {
            var salt = SHA256.HashData(Encoding.UTF8.GetBytes("BarTenderPrinter.DirectSync.v1\n" + spaceId));
            using var extract = new HMACSHA256(salt);
            var pseudoRandomKey = extract.ComputeHash(dataKey);
            try
            {
                using var expand = new HMACSHA256(pseudoRandomKey);
                return expand.ComputeHash(Encoding.UTF8.GetBytes("space-auth\u0001"));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(pseudoRandomKey);
            }
        }

        private static bool VerifyProof(
            byte[] key,
            string role,
            SyncConnectionProfile profile,
            string localDeviceId,
            string remoteDeviceId,
            byte[] clientNonce,
            byte[] serverNonce,
            byte[] proof)
        {
            var expected = CreateProof(key, role, profile, localDeviceId, remoteDeviceId, clientNonce, serverNonce);
            return proof?.Length == expected.Length && CryptographicOperations.FixedTimeEquals(expected, proof);
        }

        private static void ValidateObjectMetadata(WireObject item, string spaceId, string deviceId)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Id) || item.Id.Contains("..", StringComparison.Ordinal) ||
                item.Length <= 0 || item.Length > MaxObjectLength || !IsSha256(item.Sha256))
                throw new IOException("直连对象清单包含无效对象。");
            var prefix = $"BarTenderPrinterSync/spaces/{spaceId}/events/{deviceId}/";
            if (!item.Id.StartsWith(prefix, StringComparison.Ordinal) || !item.Id.EndsWith(".evt", StringComparison.Ordinal) ||
                item.Id.Substring(prefix.Length).Contains('/'))
                throw new IOException("直连对象路径无效。");
        }

        private static void ValidateUpload(SyncOutboxItem item)
        {
            if (item == null || item.EncryptedBlob == null || item.EncryptedBlob.Length == 0 || item.EncryptedBlob.Length > MaxObjectLength ||
                item.ObjectPath.Contains("..", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(item.ObjectPath))
                throw new IOException("直连上传对象无效。");
        }

        private static bool IsSha256(string value)
        {
            return value?.Length == 64 && value.All(character =>
                (character >= '0' && character <= '9') ||
                (character >= 'a' && character <= 'f') ||
                (character >= 'A' && character <= 'F'));
        }

        private sealed class WireMessage
        {
            public string Type { get; set; } = "";
            public string RequestId { get; set; } = "";
            public string SpaceId { get; set; } = "";
            public string DeviceId { get; set; } = "";
            public string Nonce { get; set; } = "";
            public string Proof { get; set; } = "";
            public string ObjectId { get; set; } = "";
            public string Content { get; set; } = "";
            public string Sha256 { get; set; } = "";
            public long Length { get; set; }
            public Dictionary<string, long> Cursors { get; set; }
            public WireObject[] Objects { get; set; }
            public string[] RequestedObjectIds { get; set; }
        }

        private sealed class WireObject
        {
            public string Id { get; set; } = "";
            public string EventId { get; set; } = "";
            public string Sha256 { get; set; } = "";
            public long Length { get; set; }
        }
    }
}
