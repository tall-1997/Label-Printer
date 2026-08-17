using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BarTenderPrinter
{
    internal sealed class DeviceEndpointRecord
    {
        public int SchemaVersion { get; set; } = 1;
        public string SpaceId { get; set; } = "";
        public string DeviceId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public long EndpointVersion { get; set; }
        public bool DirectSyncEnabled { get; set; }
        public int Port { get; set; }
        public string CertificateSha256 { get; set; } = "";
        public LocalEndpointAddress[] Addresses { get; set; } = Array.Empty<LocalEndpointAddress>();
        public DateTimeOffset PublishedAtUtc { get; set; }
        public DateTimeOffset ExpiresAtUtc { get; set; }
    }

    internal sealed class DeviceEndpointRegistry : IPublishedEndpointSource
    {
        private const string Root = "BarTenderPrinterSync";
        private readonly ICloudObjectStore _cloud;
        private readonly SyncConnectionProfile _profile;
        private readonly SyncStore _store;

        public DeviceEndpointRegistry(ICloudObjectStore cloud, SyncConnectionProfile profile, SyncStore store)
        {
            _cloud = cloud ?? throw new ArgumentNullException(nameof(cloud));
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public async Task PublishAsync(DeviceEndpointRecord record, CancellationToken cancellationToken)
        {
            ValidateRecord(record, record.DeviceId, DateTimeOffset.UtcNow, allowExpired: false);
            if (!string.Equals(record.SpaceId, _profile.SpaceId, StringComparison.Ordinal)) throw new InvalidDataException("端点空间身份无效。");
            var plaintext = JsonSerializer.SerializeToUtf8Bytes(record);
            var encrypted = SyncCrypto.EncryptObject(plaintext, _profile.DataKey, _profile.SpaceId, "device", record.DeviceId);
            var path = DevicePath(record.DeviceId);
            await _cloud.PutAsync(path, encrypted, cancellationToken: cancellationToken).ConfigureAwait(false);
            _store.UpsertKnownDevice(new KnownSyncDevice
            {
                DeviceId = record.DeviceId, EndpointVersion = record.EndpointVersion,
                EndpointJson = JsonSerializer.Serialize(record), LastResult = record.DirectSyncEnabled ? "published" : "disabled"
            });
        }

        public async Task<IReadOnlyList<PublishedDirectEndpoint>> GetPublishedEndpointsAsync(
            string spaceId, string localDeviceId, CancellationToken cancellationToken)
        {
            if (!string.Equals(spaceId, _profile.SpaceId, StringComparison.Ordinal)) throw new ArgumentException("端点空间身份无效。", nameof(spaceId));
            var prefix = $"{Root}/spaces/{spaceId}/devices/";
            var metadata = await _cloud.ListAsync(prefix, cancellationToken).ConfigureAwait(false);
            var endpoints = new List<PublishedDirectEndpoint>();
            foreach (var item in metadata.Where(value => !value.IsCollection && value.Path.EndsWith(".enc", StringComparison.Ordinal)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var expectedDeviceId = Path.GetFileNameWithoutExtension(item.Path);
                if (!IsSafeId(expectedDeviceId) || !string.Equals(item.Path.Replace('\\', '/'), prefix + expectedDeviceId + ".enc", StringComparison.Ordinal)) continue;
                try
                {
                    var remote = await _cloud.GetAsync(item.Path, cancellationToken).ConfigureAwait(false);
                    var plaintext = SyncCrypto.DecryptObject(remote.Content, _profile.DataKey, spaceId, "device", expectedDeviceId);
                    try
                    {
                        var record = JsonSerializer.Deserialize<DeviceEndpointRecord>(plaintext);
                        ValidateRecord(record, expectedDeviceId, DateTimeOffset.UtcNow, allowExpired: false);
                        if (string.Equals(record.DeviceId, localDeviceId, StringComparison.Ordinal)) continue;
                        _store.UpsertKnownDevice(new KnownSyncDevice
                        {
                            DeviceId = record.DeviceId, EndpointVersion = record.EndpointVersion,
                            EndpointJson = JsonSerializer.Serialize(record), LastResult = "discovered"
                        });
                        endpoints.AddRange(record.Addresses.Select(address => new PublishedDirectEndpoint
                        {
                            DeviceId = record.DeviceId, Address = address.Value, Port = record.Port, Priority = address.Priority,
                            CertificateSha256 = record.CertificateSha256, ExpiresAtUtc = record.ExpiresAtUtc.UtcDateTime,
                            Enabled = record.DirectSyncEnabled
                        }));
                    }
                    finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(plaintext); }
                }
                catch (Exception ex) when (ex is InvalidDataException || ex is JsonException || ex is SyncException ||
                    ex is System.Security.Cryptography.CryptographicException || ex is ArgumentException)
                {
                }
            }
            return endpoints;
        }

        private void ValidateRecord(DeviceEndpointRecord record, string expectedDeviceId, DateTimeOffset nowUtc, bool allowExpired)
        {
            if (record == null || record.SchemaVersion != 1 || !string.Equals(record.SpaceId, _profile.SpaceId, StringComparison.Ordinal) ||
                !string.Equals(record.DeviceId, expectedDeviceId, StringComparison.Ordinal) || !IsSafeId(record.DeviceId) ||
                record.EndpointVersion < 1 || record.Port < 1 || record.Port > 65535 || !IsSha256(record.CertificateSha256) ||
                record.PublishedAtUtc == default || record.PublishedAtUtc > nowUtc.AddMinutes(5) || record.ExpiresAtUtc <= record.PublishedAtUtc ||
                record.ExpiresAtUtc - record.PublishedAtUtc > TimeSpan.FromHours(24) || !allowExpired && record.ExpiresAtUtc <= nowUtc ||
                record.Addresses == null || record.Addresses.Length == 0 || record.Addresses.Length > 64)
                throw new InvalidDataException("设备端点记录无效。");
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var address in record.Addresses)
            {
                if (address == null || !IPAddress.TryParse(address.Value, out var parsed) || !LocalEndpointCollector.IsPublishable(parsed) ||
                    !unique.Add(parsed.ToString()) || address.Priority < 0 ||
                    address.Family != (parsed.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? "IPv4" : "IPv6"))
                    throw new InvalidDataException("设备端点地址无效。");
            }
        }

        private string DevicePath(string deviceId) => $"{Root}/spaces/{_profile.SpaceId}/devices/{deviceId}.enc";
        private static bool IsSafeId(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128 && value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_');
        private static bool IsSha256(string value) => value?.Length == 64 && value.All(Uri.IsHexDigit);
    }
}
