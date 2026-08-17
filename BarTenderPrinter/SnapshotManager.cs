using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BarTenderPrinter
{
    internal sealed class EncryptedSnapshotEntry
    {
        public string EntityType { get; set; } = "";
        public string EntityId { get; set; } = "";
        public long Version { get; set; }
        public string PayloadJson { get; set; } = "";
    }

    internal sealed class EncryptedSnapshotPayload
    {
        public int SchemaVersion { get; set; } = 1;
        public string SpaceId { get; set; } = "";
        public string SnapshotId { get; set; } = "";
        public DateTimeOffset CreatedAtUtc { get; set; }
        public Dictionary<string, long> Cursors { get; set; } = new Dictionary<string, long>(StringComparer.Ordinal);
        public List<EncryptedSnapshotEntry> Entries { get; set; } = new List<EncryptedSnapshotEntry>();
    }

    internal sealed class EncryptedSnapshotPointer
    {
        public int SchemaVersion { get; set; } = 1;
        public string SpaceId { get; set; } = "";
        public string SnapshotId { get; set; } = "";
        public string EncryptedSha256 { get; set; } = "";
        public Dictionary<string, long> Cursors { get; set; } = new Dictionary<string, long>(StringComparer.Ordinal);
    }

    internal sealed class SnapshotManager
    {
        private const string Root = "BarTenderPrinterSync";
        private readonly SyncConnectionProfile _profile;
        private readonly SyncStore _store;
        private readonly FileSnapshotSyncEventApplier _applier;
        private readonly long _eventThreshold;
        private readonly long _byteThreshold;
        private readonly Func<DateTimeOffset> _utcNow;

        public SnapshotManager(SyncConnectionProfile profile, SyncStore store, FileSnapshotSyncEventApplier applier,
            long eventThreshold = 500, long byteThreshold = 20 * 1024 * 1024, Func<DateTimeOffset> utcNow = null)
        {
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _applier = applier ?? throw new ArgumentNullException(nameof(applier));
            if (eventThreshold < 1 || byteThreshold < 1) throw new ArgumentOutOfRangeException(nameof(eventThreshold));
            _eventThreshold = eventThreshold;
            _byteThreshold = byteThreshold;
            _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        }

        public async Task<bool> TryRestoreInitialAsync(ICloudObjectStore cloud, CancellationToken cancellationToken)
        {
            if (_store.HasLocalSyncState()) return false;
            var pointerPath = PointerPath();
            CloudObject pointerObject;
            try { pointerObject = await cloud.GetAsync(pointerPath, cancellationToken).ConfigureAwait(false); }
            catch (WebDavNotFoundException) { return false; }

            EncryptedSnapshotPointer pointer;
            try { pointer = DecryptPointer(pointerObject.Content); }
            catch (Exception ex) when (IsQuarantinable(ex))
            {
                _store.RecordQuarantinedObject(pointerPath, ErrorCode(ex), _utcNow());
                return false;
            }

            var snapshotPath = SnapshotPath(pointer.SnapshotId);
            try
            {
                var snapshotObject = await cloud.GetAsync(snapshotPath, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(SyncCrypto.ComputeSha256Hex(snapshotObject.Content), pointer.EncryptedSha256, StringComparison.OrdinalIgnoreCase))
                    throw new SyncSecurityException(SyncErrorCodes.ObjectCorrupted, "快照密文摘要无效。");
                var plaintext = SyncCrypto.DecryptObject(snapshotObject.Content, _profile.DataKey, _profile.SpaceId, "snapshot", pointer.SnapshotId);
                try
                {
                    var payload = JsonSerializer.Deserialize<EncryptedSnapshotPayload>(plaintext) ?? throw new InvalidDataException("快照内容为空。");
                    ValidatePayload(payload, pointer);
                    foreach (var entry in payload.Entries) _applier.ApplySnapshotEntry(entry, _store);
                    foreach (var cursor in payload.Cursors) _store.AdvanceCursor(cursor.Key, cursor.Value);
                    return true;
                }
                finally { CryptographicOperations.ZeroMemory(plaintext); }
            }
            catch (Exception ex) when (IsQuarantinable(ex))
            {
                _store.RecordQuarantinedObject(snapshotPath, ErrorCode(ex), _utcNow());
                return false;
            }
        }

        public async Task<bool> TryCreateAsync(ICloudObjectStore cloud, SyncDataSnapshot snapshot, CancellationToken cancellationToken)
        {
            var progress = _store.GetSnapshotProgress();
            if (progress.EventCount - progress.LastSnapshotEventCount < _eventThreshold &&
                progress.EncryptedBytes - progress.LastSnapshotEncryptedBytes < _byteThreshold) return false;

            var snapshotId = Guid.NewGuid().ToString("N");
            var payload = BuildPayload(snapshotId, snapshot);
            var encrypted = SyncCrypto.EncryptObject(JsonSerializer.SerializeToUtf8Bytes(payload), _profile.DataKey, _profile.SpaceId, "snapshot", snapshotId);
            await cloud.PutAsync(SnapshotPath(snapshotId), encrypted, createOnly: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            var pointer = new EncryptedSnapshotPointer
            {
                SpaceId = _profile.SpaceId, SnapshotId = snapshotId,
                EncryptedSha256 = SyncCrypto.ComputeSha256Hex(encrypted), Cursors = payload.Cursors
            };
            try { await UpdatePointerAsync(cloud, pointer, cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) when (IsQuarantinable(ex))
            {
                _store.RecordQuarantinedObject(PointerPath(), ErrorCode(ex), _utcNow());
                return false;
            }
            _store.MarkSnapshotCreated(snapshotId, progress.EventCount, progress.EncryptedBytes);
            return true;
        }

        private async Task UpdatePointerAsync(ICloudObjectStore cloud, EncryptedSnapshotPointer pointer, CancellationToken cancellationToken)
        {
            var path = PointerPath();
            for (var attempt = 0; attempt < 2; attempt++)
            {
                CloudObject existing = null;
                try { existing = await cloud.GetAsync(path, cancellationToken).ConfigureAwait(false); }
                catch (WebDavNotFoundException) { }
                if (existing != null)
                {
                    var current = DecryptPointer(existing.Content);
                    if (Covers(current.Cursors, pointer.Cursors)) return;
                }
                var encrypted = EncryptPointer(pointer);
                try
                {
                    await cloud.PutAsync(path, encrypted, existing?.Metadata?.ETag, existing == null, cancellationToken).ConfigureAwait(false);
                    return;
                }
                catch (WebDavPreconditionFailedException) when (attempt == 0) { }
            }
            throw new WebDavPreconditionFailedException(System.Net.HttpStatusCode.PreconditionFailed);
        }

        private EncryptedSnapshotPayload BuildPayload(string snapshotId, SyncDataSnapshot snapshot)
        {
            var templatePaths = snapshot.Templates.ToDictionary(item => item.SourcePath, item => item.Sha256, StringComparer.OrdinalIgnoreCase);
            var payload = new EncryptedSnapshotPayload
            {
                SpaceId = _profile.SpaceId, SnapshotId = snapshotId, CreatedAtUtc = _utcNow(),
                Cursors = _store.GetSnapshotCursors().ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal)
            };
            foreach (var file in snapshot.Files)
            {
                var entityType = EntityType(file.Kind);
                var state = _store.GetEntityState(entityType, file.ObjectId);
                var content = file.Kind is SyncSnapshotKind.Orders or SyncSnapshotKind.TemplateSettings
                    ? SyncApplicationService.CanonicalizeTemplatePaths(file.Content, templatePaths)
                    : file.Content;
                payload.Entries.Add(new EncryptedSnapshotEntry
                {
                    EntityType = entityType, EntityId = file.ObjectId, Version = Math.Max(1, state.Version),
                    PayloadJson = JsonSerializer.Serialize(new SyncSnapshotPayload
                    {
                        Kind = file.Kind.ToString(), Sha256 = SyncCrypto.ComputeSha256Hex(content), ContentBase64 = Convert.ToBase64String(content),
                        TemplatePaths = templatePaths.Concat(templatePaths.Select(item => new KeyValuePair<string, string>("btpsync-template:" + item.Value, item.Value)))
                            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First().Value, StringComparer.OrdinalIgnoreCase)
                    })
                });
            }
            return payload;
        }

        private byte[] EncryptPointer(EncryptedSnapshotPointer pointer) => SyncCrypto.EncryptObject(
            JsonSerializer.SerializeToUtf8Bytes(pointer), _profile.DataKey, _profile.SpaceId, "snapshot-pointer", _profile.SpaceId);

        private EncryptedSnapshotPointer DecryptPointer(byte[] encrypted)
        {
            var plaintext = SyncCrypto.DecryptObject(encrypted, _profile.DataKey, _profile.SpaceId, "snapshot-pointer", _profile.SpaceId);
            try
            {
                var pointer = JsonSerializer.Deserialize<EncryptedSnapshotPointer>(plaintext) ?? throw new InvalidDataException("快照指针为空。");
                if (pointer.SchemaVersion != 1) throw new SyncSecurityException(SyncErrorCodes.SchemaTooNew, "快照指针 schema 版本不受支持。");
                if (!string.Equals(pointer.SpaceId, _profile.SpaceId, StringComparison.Ordinal) ||
                    !Guid.TryParseExact(pointer.SnapshotId, "N", out _) || pointer.EncryptedSha256?.Length != 64 ||
                    pointer.EncryptedSha256.Any(character => !Uri.IsHexDigit(character)) || pointer.Cursors == null ||
                    pointer.Cursors.Any(item => string.IsNullOrWhiteSpace(item.Key) || item.Key.Length > 512 || item.Value < 0))
                    throw new SyncSecurityException(SyncErrorCodes.ObjectCorrupted, "快照指针空间绑定无效。");
                return pointer;
            }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
        }

        private void ValidatePayload(EncryptedSnapshotPayload payload, EncryptedSnapshotPointer pointer)
        {
            if (payload.SchemaVersion != 1) throw new SyncSecurityException(SyncErrorCodes.SchemaTooNew, "快照 schema 版本不受支持。");
            if (!string.Equals(payload.SpaceId, _profile.SpaceId, StringComparison.Ordinal) || !string.Equals(payload.SnapshotId, pointer.SnapshotId, StringComparison.Ordinal) ||
                payload.Cursors == null || payload.Entries == null || payload.Cursors.Any(item => string.IsNullOrWhiteSpace(item.Key) || item.Key.Length > 512 || item.Value < 0) ||
                payload.Entries.Any(item => item == null || string.IsNullOrWhiteSpace(item.EntityType) || string.IsNullOrWhiteSpace(item.EntityId) || item.Version < 1 || string.IsNullOrWhiteSpace(item.PayloadJson)))
                throw new SyncSecurityException(SyncErrorCodes.ObjectCorrupted, "快照空间或身份绑定无效。");
            if (!Covers(payload.Cursors, pointer.Cursors)) throw new SyncSecurityException(SyncErrorCodes.ObjectCorrupted, "快照游标覆盖范围无效。");
        }

        private static bool Covers(IReadOnlyDictionary<string, long> left, IReadOnlyDictionary<string, long> right) =>
            right.All(item => left.TryGetValue(item.Key, out var sequence) && sequence >= item.Value);

        private string PointerPath() => $"{Root}/spaces/{_profile.SpaceId}/snapshot-pointer.enc";
        private string SnapshotPath(string snapshotId) => $"{Root}/spaces/{_profile.SpaceId}/snapshots/{snapshotId}.snap";
        private static bool IsQuarantinable(Exception ex) => ex is SyncSecurityException || ex is InvalidDataException || ex is JsonException;
        private static string ErrorCode(Exception ex) => ex is SyncSecurityException security ? security.ErrorCode : SyncErrorCodes.ObjectCorrupted;
        private static string EntityType(SyncSnapshotKind kind) => kind switch
        {
            SyncSnapshotKind.Orders => "Orders", SyncSnapshotKind.TemplateSettings => "TemplateSettings",
            SyncSnapshotKind.PrintRecord => "PrintRecord", SyncSnapshotKind.PrintJobEvent => "PrintJobEvent",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }
}
