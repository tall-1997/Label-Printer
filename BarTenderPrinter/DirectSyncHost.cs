using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
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
    internal interface ILocalSecretProtector
    {
        byte[] Protect(byte[] plaintext);
        byte[] Unprotect(byte[] protectedBytes);
    }

    internal sealed class DpapiCurrentUserProtector : ILocalSecretProtector
    {
        private static readonly byte[] Entropy = SHA256.HashData(Encoding.UTF8.GetBytes("BarTenderPrinter.DirectSyncCertificate.v1"));

        public byte[] Protect(byte[] plaintext)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("直连证书保护需要 Windows DPAPI。");
            return ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
        }

        public byte[] Unprotect(byte[] protectedBytes)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("直连证书保护需要 Windows DPAPI。");
            return ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
        }
    }

    internal sealed class DirectSyncCertificateStore
    {
        private readonly string _path;
        private readonly ILocalSecretProtector _protector;

        public DirectSyncCertificateStore(string path, ILocalSecretProtector protector = null)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("直连证书路径不能为空。", nameof(path));
            _path = Path.GetFullPath(path);
            _protector = protector ?? new DpapiCurrentUserProtector();
        }

        public X509Certificate2 LoadOrCreate(string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId)) throw new ArgumentException("设备标识不能为空。", nameof(deviceId));
            if (File.Exists(_path)) return Load(deviceId, File.ReadAllBytes(_path));

            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest($"CN={deviceId}", key, HashAlgorithmName.SHA256);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
            using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(5));
            var pfx = generated.Export(X509ContentType.Pfx);
            try
            {
                var protectedBytes = _protector.Protect(pfx);
                WriteAtomic(_path, protectedBytes);
                return Load(deviceId, protectedBytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(pfx);
            }
        }

        private X509Certificate2 Load(string deviceId, byte[] protectedBytes)
        {
            var pfx = _protector.Unprotect(protectedBytes);
            try
            {
                var certificate = new X509Certificate2(pfx, (string)null, X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable);
                if (!certificate.HasPrivateKey || !string.Equals(certificate.GetNameInfo(X509NameType.SimpleName, false), deviceId, StringComparison.Ordinal))
                {
                    certificate.Dispose();
                    throw new CryptographicException("直连证书身份无效。");
                }
                return certificate;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(pfx);
            }
        }

        private static void WriteAtomic(string path, byte[] content)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporaryPath, content);
                File.Move(temporaryPath, path, true);
            }
            finally
            {
                try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch (IOException) { }
            }
        }
    }

    internal sealed class DirectSyncHost : IAsyncDisposable
    {
        private const int MaxControlFrameLength = 1024 * 1024;
        private const int MaxObjectLength = 500 * 1024 * 1024;
        private const int ChunkLength = 512 * 1024;
        private readonly SyncConnectionProfile _profile;
        private readonly SyncStore _store;
        private readonly X509Certificate2 _certificate;
        private readonly IReadOnlyList<IPAddress> _addresses;
        private readonly SemaphoreSlim _connections;
        private readonly List<TcpListener> _listeners = new List<TcpListener>();
        private readonly List<Task> _acceptTasks = new List<Task>();
        private readonly HashSet<Task> _clientTasks = new HashSet<Task>();
        private readonly object _gate = new object();
        private CancellationTokenSource _lifetime;
        private bool _disposed;
        private string _lastSafeErrorCode = "";

        public DirectSyncHost(SyncConnectionProfile profile, SyncStore store, X509Certificate2 certificate,
            IReadOnlyList<IPAddress> addresses, int maxConcurrentConnections = 4)
        {
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _certificate = certificate ?? throw new ArgumentNullException(nameof(certificate));
            _addresses = addresses?.Distinct().ToArray() ?? throw new ArgumentNullException(nameof(addresses));
            if (_profile.DataKey?.Length != 32 || string.IsNullOrWhiteSpace(_profile.SpaceId) || string.IsNullOrWhiteSpace(_profile.DeviceId))
                throw new ArgumentException("直连配置无效。", nameof(profile));
            if (!_certificate.HasPrivateKey) throw new ArgumentException("直连证书缺少私钥。", nameof(certificate));
            if (_addresses.Count == 0) throw new ArgumentException("直连监听地址不能为空。", nameof(addresses));
            if (maxConcurrentConnections < 1 || maxConcurrentConnections > 64) throw new ArgumentOutOfRangeException(nameof(maxConcurrentConnections));
            _connections = new SemaphoreSlim(maxConcurrentConnections, maxConcurrentConnections);
        }

        public bool IsListening { get { lock (_gate) return _listeners.Count > 0 && !_disposed; } }
        public int Port { get; private set; }
        public string CertificateSha256 => Convert.ToHexString(SHA256.HashData(_certificate.RawData));
        internal string LastSafeErrorCode => Volatile.Read(ref _lastSafeErrorCode);

        public Task StartAsync(int port, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (port < 0 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            lock (_gate)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(DirectSyncHost));
                if (_listeners.Count > 0) throw new InvalidOperationException("直连服务已经启动。");
                _lifetime = new CancellationTokenSource();
                try
                {
                    foreach (var address in _addresses)
                    {
                        var listener = new TcpListener(address, port);
                        listener.Start();
                        var actualPort = ((IPEndPoint)listener.LocalEndpoint).Port;
                        if (Port != 0 && actualPort != Port) throw new SocketException((int)SocketError.AddressAlreadyInUse);
                        Port = actualPort;
                        _listeners.Add(listener);
                        _acceptTasks.Add(AcceptLoopAsync(listener, _lifetime.Token));
                    }
                }
                catch
                {
                    foreach (var listener in _listeners) listener.Stop();
                    _listeners.Clear();
                    _acceptTasks.Clear();
                    _lifetime.Dispose();
                    _lifetime = null;
                    Port = 0;
                    throw;
                }
            }
            return Task.CompletedTask;
        }

        private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
                catch (SocketException) when (cancellationToken.IsCancellationRequested) { break; }
                Task task;
                lock (_gate)
                {
                    if (_disposed) { client.Dispose(); break; }
                    task = HandleBoundedAsync(client, cancellationToken);
                    _clientTasks.Add(task);
                }
                _ = task.ContinueWith(completed =>
                {
                    lock (_gate) _clientTasks.Remove(completed);
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }

        private async Task HandleBoundedAsync(TcpClient client, CancellationToken cancellationToken)
        {
            var entered = false;
            try
            {
                entered = await _connections.WaitAsync(0, cancellationToken).ConfigureAwait(false);
                if (!entered) return;
                await HandleClientAsync(client, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException || ex is SocketException || ex is AuthenticationException ||
                ex is CryptographicException || ex is JsonException || ex is FormatException || ex is ArgumentException ||
                ex is DirectSyncAuthenticationException ||
                ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                Volatile.Write(ref _lastSafeErrorCode, GetSafeErrorCode(ex));
            }
            finally
            {
                client.Dispose();
                if (entered) _connections.Release();
            }
        }

        private static string GetSafeErrorCode(Exception exception)
        {
            if (exception is DirectSyncAuthenticationException) return "HOST_AUTH_REJECTED";
            if (exception is AuthenticationException) return "HOST_TLS_AUTHENTICATION";
            if (exception is CryptographicException) return "HOST_CRYPTOGRAPHIC";
            if (exception is JsonException) return "HOST_JSON";
            if (exception is FormatException) return "HOST_FORMAT";
            if (exception is ArgumentException) return "HOST_ARGUMENT";
            if (exception is SocketException) return "HOST_SOCKET";
            if (exception is OperationCanceledException) return "HOST_CANCELLED";
            return exception.Message switch
            {
                "直连清单请求无效。" => "HOST_INVENTORY_REQUEST",
                "直连对象清单超过限制。" => "HOST_INVENTORY_LIMIT",
                "直连对象清单包含空对象。" => "HOST_INVENTORY_NULL",
                "直连对象清单包含无效对象。" => "HOST_OBJECT_METADATA",
                "直连事件路径无效。" => "HOST_EVENT_PATH",
                "直连事件身份无效。" => "HOST_EVENT_IDENTITY",
                "直连事件序号无效。" => "HOST_EVENT_SEQUENCE",
                "直连对象请求无效。" => "HOST_OBJECT_REQUEST",
                "直连对象响应身份无效。" => "HOST_OBJECT_RESPONSE_IDENTITY",
                "直连对象完成帧无效。" => "HOST_OBJECT_COMPLETE",
                "直连对象帧类型无效。" => "HOST_OBJECT_FRAME",
                "直连对象分块大小无效。" => "HOST_OBJECT_CHUNK",
                "直连对象摘要校验失败。" => "HOST_OBJECT_DIGEST",
                "直连同步确认无效。" => "HOST_RECEIPT",
                "直连游标无效。" => "HOST_CURSOR",
                "直连控制帧长度无效。" => "HOST_FRAME_LENGTH",
                "直连会话意外关闭。" => "HOST_CONNECTION_CLOSED",
                _ => "HOST_IO"
            };
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
        {
            await using var ssl = new SslStream(client.GetStream(), false);
            await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = _certificate,
                ClientCertificateRequired = false,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck
            }, cancellationToken).ConfigureAwait(false);

            var hello = await ReadMessageAsync(ssl, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(hello.Type, "Hello", StringComparison.Ordinal) ||
                !string.Equals(hello.SpaceId, _profile.SpaceId, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(hello.DeviceId) || string.Equals(hello.DeviceId, _profile.DeviceId, StringComparison.Ordinal))
            {
                await WriteMessageAsync(ssl, new WireMessage { Type = "Error" }, cancellationToken).ConfigureAwait(false);
                throw new DirectSyncAuthenticationException("直连设备或空间身份不匹配。");
            }
            var clientNonce = DecodeExact(hello.Nonce, 32);
            var serverNonce = RandomNumberGenerator.GetBytes(32);
            var authenticationKey = DeriveAuthenticationKey(_profile.DataKey, _profile.SpaceId);
            try
            {
                await WriteMessageAsync(ssl, new WireMessage
                {
                    Type = "Hello", SpaceId = _profile.SpaceId, DeviceId = _profile.DeviceId,
                    Nonce = Convert.ToBase64String(serverNonce),
                    Proof = Convert.ToBase64String(CreateProof(authenticationKey, "server", hello.DeviceId, _profile.DeviceId, clientNonce, serverNonce))
                }, cancellationToken).ConfigureAwait(false);
                var authentication = await ReadMessageAsync(ssl, cancellationToken).ConfigureAwait(false);
                var proof = DecodeExact(authentication.Proof, 32);
                if (!string.Equals(authentication.Type, "Authenticate", StringComparison.Ordinal) ||
                    !string.Equals(authentication.SpaceId, _profile.SpaceId, StringComparison.Ordinal) ||
                    !string.Equals(authentication.DeviceId, hello.DeviceId, StringComparison.Ordinal) ||
                    !VerifyProof(authenticationKey, "client", hello.DeviceId, _profile.DeviceId, clientNonce, serverNonce, proof))
                {
                    await WriteMessageAsync(ssl, new WireMessage { Type = "Error" }, cancellationToken).ConfigureAwait(false);
                    throw new DirectSyncAuthenticationException("直连空间共享密钥认证失败。");
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(authenticationKey);
            }
            await WriteMessageAsync(ssl, new WireMessage { Type = "Authenticated" }, cancellationToken).ConfigureAwait(false);
            await SynchronizeAsync(ssl, hello.DeviceId, cancellationToken).ConfigureAwait(false);
        }

        private async Task SynchronizeAsync(SslStream stream, string remoteDeviceId, CancellationToken cancellationToken)
        {
            var request = await ReadMessageAsync(stream, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(request.Type, "InventoryRequest", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(request.RequestId))
                throw new IOException("直连清单请求无效。");
            ValidateCursors(request.Cursors);
            if (request.Objects?.Length > 1000) throw new IOException("直连对象清单超过限制。");
            if (request.Objects?.Any(item => item == null) == true) throw new IOException("直连对象清单包含空对象。");
            var offered = (request.Objects ?? Array.Empty<WireObject>()).ToDictionary(item => item.Id, StringComparer.Ordinal);
            foreach (var item in offered.Values) ValidateEventMetadata(item, remoteDeviceId);

            var availableItems = _store.GetOutboxForDirectSync(1000)
                .Where(item => string.Equals(item.DeviceId, _profile.DeviceId, StringComparison.Ordinal)).ToArray();
            var available = availableItems
                .Where(item => !request.Cursors.TryGetValue(item.DeviceId, out var cursor) || item.Sequence > cursor)
                .Select(ToWireObject).ToArray();
            var requested = offered.Values.Where(item => !_store.IsEventApplied(item.EventId) && _store.GetOutbox(item.EventId) == null)
                .Select(item => item.Id).ToArray();
            await WriteMessageAsync(stream, new WireMessage
            {
                Type = "InventoryResponse", RequestId = request.RequestId, Objects = available, RequestedObjectIds = requested
            }, cancellationToken).ConfigureAwait(false);

            var availableById = availableItems.ToDictionary(item => item.ObjectPath, StringComparer.Ordinal);
            for (var index = 0; index < available.Length; index++)
            {
                var objectRequest = await ReadMessageAsync(stream, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(objectRequest.Type, "ObjectRequest", StringComparison.Ordinal) ||
                    !string.Equals(objectRequest.RequestId, request.RequestId, StringComparison.Ordinal) ||
                    !availableById.TryGetValue(objectRequest.ObjectId, out var item))
                    throw new IOException("直连对象请求无效。");
                await WriteObjectAsync(stream, item, request.RequestId, cancellationToken).ConfigureAwait(false);
            }
            foreach (var objectId in requested)
            {
                var metadata = offered[objectId];
                var content = await ReadObjectAsync(stream, metadata, request.RequestId, cancellationToken).ConfigureAwait(false);
                _ = new SyncEventObjectCodec().DecodeEvent(_profile, metadata.Id, content);
                _store.Enqueue(new SyncOutboxItem
                {
                    EventId = metadata.EventId, DeviceId = remoteDeviceId, Sequence = ParseSequence(metadata.Id),
                    ObjectPath = metadata.Id, EncryptedBlob = content, CreatedAtUtc = DateTimeOffset.UtcNow
                });
            }
            var receipt = await ReadMessageAsync(stream, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(receipt.Type, "SyncReceipt", StringComparison.Ordinal) || !string.Equals(receipt.RequestId, request.RequestId, StringComparison.Ordinal))
                throw new IOException("直连同步确认无效。");
        }

        private static async Task<byte[]> ReadObjectAsync(SslStream stream, WireObject metadata, string requestId, CancellationToken cancellationToken)
        {
            using var buffer = new MemoryStream((int)metadata.Length);
            while (true)
            {
                var frame = await ReadMessageAsync(stream, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(frame.RequestId, requestId, StringComparison.Ordinal) || !string.Equals(frame.ObjectId, metadata.Id, StringComparison.Ordinal))
                    throw new IOException("直连对象响应身份无效。");
                if (string.Equals(frame.Type, "ObjectComplete", StringComparison.Ordinal))
                {
                    if (frame.Length != metadata.Length || !string.Equals(frame.Sha256, metadata.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("直连对象完成帧无效。");
                    break;
                }
                if (!string.Equals(frame.Type, "ObjectChunk", StringComparison.Ordinal)) throw new IOException("直连对象帧类型无效。");
                var chunk = Convert.FromBase64String(frame.Content ?? "");
                if (chunk.Length == 0 || chunk.Length > ChunkLength || buffer.Length + chunk.Length > metadata.Length)
                    throw new IOException("直连对象分块大小无效。");
                await buffer.WriteAsync(chunk, cancellationToken).ConfigureAwait(false);
            }
            var content = buffer.ToArray();
            if (content.LongLength != metadata.Length || !string.Equals(SyncDataAdapter.ComputeSha256(content), metadata.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("直连对象摘要校验失败。");
            return content;
        }

        private static async Task WriteObjectAsync(SslStream stream, SyncOutboxItem item, string requestId, CancellationToken cancellationToken)
        {
            for (var offset = 0; offset < item.EncryptedBlob.Length; offset += ChunkLength)
            {
                var length = Math.Min(ChunkLength, item.EncryptedBlob.Length - offset);
                await WriteMessageAsync(stream, new WireMessage
                {
                    Type = "ObjectChunk", RequestId = requestId, ObjectId = item.ObjectPath,
                    Content = Convert.ToBase64String(item.EncryptedBlob, offset, length)
                }, cancellationToken).ConfigureAwait(false);
            }
            await WriteMessageAsync(stream, new WireMessage
            {
                Type = "ObjectComplete", RequestId = requestId, ObjectId = item.ObjectPath,
                Sha256 = SyncDataAdapter.ComputeSha256(item.EncryptedBlob), Length = item.EncryptedBlob.LongLength
            }, cancellationToken).ConfigureAwait(false);
        }

        private static WireObject ToWireObject(SyncOutboxItem item) => new WireObject
        {
            Id = item.ObjectPath, EventId = item.EventId, Length = item.EncryptedBlob.LongLength,
            Sha256 = SyncDataAdapter.ComputeSha256(item.EncryptedBlob)
        };

        private void ValidateEventMetadata(WireObject item, string deviceId)
        {
            if (item == null || item.Length <= 0 || item.Length > MaxObjectLength || !IsSha256(item.Sha256))
                throw new IOException("直连对象清单包含无效对象。");
            var prefix = $"BarTenderPrinterSync/spaces/{_profile.SpaceId}/events/{deviceId}/";
            if (!item.Id.StartsWith(prefix, StringComparison.Ordinal) || item.Id.Contains("..", StringComparison.Ordinal) ||
                !item.Id.EndsWith(".evt", StringComparison.Ordinal) || item.Id.Substring(prefix.Length).Contains('/'))
                throw new IOException("直连事件路径无效。");
            var sequence = ParseSequence(item.Id);
            if (!string.Equals(item.EventId, $"{deviceId}:{sequence}", StringComparison.Ordinal)) throw new IOException("直连事件身份无效。");
        }

        private static long ParseSequence(string path)
        {
            if (!long.TryParse(Path.GetFileNameWithoutExtension(path), out var sequence) || sequence < 1) throw new IOException("直连事件序号无效。");
            return sequence;
        }

        private static void ValidateCursors(Dictionary<string, long> cursors)
        {
            if (cursors == null || cursors.Count > 10000 || cursors.Any(item => string.IsNullOrWhiteSpace(item.Key) || item.Key.Length > 512 || item.Value < 0))
                throw new IOException("直连游标无效。");
        }

        private static bool IsSha256(string value) => value?.Length == 64 && value.All(Uri.IsHexDigit);

        private static byte[] DecodeExact(string value, int length)
        {
            try
            {
                var bytes = Convert.FromBase64String(value ?? "");
                if (bytes.Length != length) throw new DirectSyncAuthenticationException("直连认证挑战长度无效。");
                return bytes;
            }
            catch (FormatException ex) { throw new DirectSyncAuthenticationException("直连认证挑战格式无效。", ex); }
        }

        private byte[] CreateProof(byte[] key, string role, string clientDeviceId, string serverDeviceId, byte[] clientNonce, byte[] serverNonce)
        {
            var context = string.Join("\n", "BTP-DIRECT-1", role, _profile.SpaceId, clientDeviceId, serverDeviceId,
                Convert.ToBase64String(clientNonce), Convert.ToBase64String(serverNonce));
            using var hmac = new HMACSHA256(key);
            return hmac.ComputeHash(Encoding.UTF8.GetBytes(context));
        }

        private bool VerifyProof(byte[] key, string role, string clientDeviceId, string serverDeviceId, byte[] clientNonce, byte[] serverNonce, byte[] proof)
        {
            var expected = CreateProof(key, role, clientDeviceId, serverDeviceId, clientNonce, serverNonce);
            return CryptographicOperations.FixedTimeEquals(expected, proof);
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
            finally { CryptographicOperations.ZeroMemory(pseudoRandomKey); }
        }

        private static async Task WriteMessageAsync(SslStream stream, WireMessage message, CancellationToken cancellationToken)
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(message);
            if (payload.Length == 0 || payload.Length > MaxControlFrameLength) throw new IOException("直连控制帧超过限制。");
            var prefix = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(prefix, payload.Length);
            await stream.WriteAsync(prefix, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        private static async Task<WireMessage> ReadMessageAsync(SslStream stream, CancellationToken cancellationToken)
        {
            var prefix = new byte[4];
            await ReadExactlyAsync(stream, prefix, cancellationToken).ConfigureAwait(false);
            var length = BinaryPrimitives.ReadInt32BigEndian(prefix);
            if (length <= 0 || length > MaxControlFrameLength) throw new IOException("直连控制帧长度无效。");
            var payload = new byte[length];
            await ReadExactlyAsync(stream, payload, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<WireMessage>(payload) ?? throw new IOException("直连控制帧为空。");
        }

        private static async Task ReadExactlyAsync(SslStream stream, byte[] buffer, CancellationToken cancellationToken)
        {
            var offset = 0;
            while (offset < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false);
                if (read == 0) throw new EndOfStreamException("直连会话意外关闭。");
                offset += read;
            }
        }

        public async ValueTask DisposeAsync()
        {
            List<Task> tasks;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _lifetime?.Cancel();
                foreach (var listener in _listeners) listener.Stop();
                tasks = _acceptTasks.Concat(_clientTasks).ToList();
                _listeners.Clear();
                _acceptTasks.Clear();
                _clientTasks.Clear();
            }
            try { await Task.WhenAll(tasks).ConfigureAwait(false); }
            catch (Exception ex) when (ex is OperationCanceledException || ex is SocketException) { }
            _lifetime?.Dispose();
            _certificate.Dispose();
            _connections.Dispose();
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
