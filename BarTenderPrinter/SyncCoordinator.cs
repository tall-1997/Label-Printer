using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BarTenderPrinter
{
    public sealed class SyncEvent
    {
        public int SchemaVersion { get; init; } = 1;
        public string EventId { get; init; } = "";
        public string DeviceId { get; init; } = "";
        public long Sequence { get; init; }
        public string EntityType { get; init; } = "";
        public string EntityId { get; init; } = "";
        public string Operation { get; init; } = "Upsert";
        public long BaseVersion { get; init; }
        public long NewVersion { get; init; }
        public DateTime OccurredAtUtc { get; init; }
        public string PayloadJson { get; init; } = "";
        public string ResolvesConflictId { get; init; } = "";
    }

    public enum SyncEventApplyResult
    {
        Applied,
        AlreadyApplied,
        Conflict
    }

    public sealed class SyncApplyOutcome
    {
        public SyncEventApplyResult Result { get; init; }
        public SyncConflict Conflict { get; init; }
    }

    public interface ISyncEventApplier
    {
        Task<IReadOnlyDictionary<string, long>> GetCursorsAsync(SyncStore store, CancellationToken cancellationToken);
        Task<SyncApplyOutcome> ApplyAtomicallyAsync(SyncEvent syncEvent, SyncStore store, CancellationToken cancellationToken);
    }

    public interface ISyncObjectCodec
    {
        SyncEvent DecodeEvent(SyncConnectionProfile profile, string objectPath, byte[] encryptedBlob);
    }

    public interface IDirectSyncTransport
    {
        Task<DirectSyncResult> TrySynchronizeAsync(
            SyncConnectionProfile profile,
            string localDeviceId,
            IReadOnlyDictionary<string, long> cursors,
            IReadOnlyList<SyncOutboxItem> outbox,
            CancellationToken cancellationToken);
    }

    public sealed class DirectSyncResult
    {
        public bool Succeeded { get; init; }
        public bool AuthenticationFailed { get; init; }
        public string SafeErrorCode { get; init; } = "";
        public IReadOnlyList<RemoteSyncObject> DownloadedObjects { get; init; } = Array.Empty<RemoteSyncObject>();
        public IReadOnlyCollection<string> UploadedEventIds { get; init; } = Array.Empty<string>();
    }

    public sealed class RemoteSyncObject
    {
        public string Path { get; init; } = "";
        public string Sha256 { get; init; } = "";
        public byte[] Content { get; init; } = Array.Empty<byte>();
    }

    public sealed class SyncRunResult
    {
        public int AppliedEvents { get; init; }
        public int DuplicateEvents { get; init; }
        public int Conflicts { get; init; }
        public int UploadedEvents { get; init; }
        public bool UsedDirectSync { get; init; }
        public bool FellBackToWebDav { get; init; }
        public int QuarantinedObjects { get; init; }
        public int BlockedUploads { get; init; }
    }

    public sealed class SyncCoordinator
    {
        private readonly SyncConnectionProfile _profile;
        private readonly string _localDeviceId;
        private readonly ICloudObjectStore _cloud;
        private readonly SyncStore _store;
        private readonly ISyncObjectCodec _codec;
        private readonly ISyncEventApplier _eventApplier;
        private readonly IDirectSyncTransport _directSync;
        private readonly Func<DateTimeOffset> _utcNow;
        private readonly Func<double> _random;
        private readonly TimeSpan _initialRetryDelay;
        private readonly TimeSpan _maximumRetryDelay;
        private readonly SemaphoreSlim _syncLock = new SemaphoreSlim(1, 1);

        public SyncCoordinator(
            SyncConnectionProfile profile,
            string localDeviceId,
            ICloudObjectStore cloud,
            SyncStore store,
            ISyncObjectCodec codec,
            ISyncEventApplier eventApplier,
            IDirectSyncTransport directSync = null,
            Func<DateTimeOffset> utcNow = null,
            Func<double> random = null,
            TimeSpan? initialRetryDelay = null,
            TimeSpan? maximumRetryDelay = null)
        {
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _localDeviceId = localDeviceId;
            _cloud = cloud ?? throw new ArgumentNullException(nameof(cloud));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _codec = codec ?? throw new ArgumentNullException(nameof(codec));
            _eventApplier = eventApplier ?? throw new ArgumentNullException(nameof(eventApplier));
            _directSync = directSync;
            _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
            _random = random ?? Random.Shared.NextDouble;
            _initialRetryDelay = initialRetryDelay ?? TimeSpan.FromSeconds(5);
            _maximumRetryDelay = maximumRetryDelay ?? TimeSpan.FromMinutes(30);
            if (string.IsNullOrWhiteSpace(profile.SpaceId)) throw new ArgumentException("空间标识不能为空。", nameof(profile));
            if (string.IsNullOrWhiteSpace(localDeviceId)) throw new ArgumentException("设备标识不能为空。", nameof(localDeviceId));
            if (_initialRetryDelay <= TimeSpan.Zero || _maximumRetryDelay < _initialRetryDelay) throw new ArgumentOutOfRangeException(nameof(initialRetryDelay));
        }

        public async Task<SyncRunResult> SynchronizeAsync(bool enableDirectSync, CancellationToken cancellationToken, bool uploadPending = true)
        {
            await _syncLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var cursors = await _eventApplier.GetCursorsAsync(_store, cancellationToken).ConfigureAwait(false);
                var spaceEventPrefix = $"BarTenderPrinterSync/spaces/{_profile.SpaceId}/events/";
                var outbox = _store.GetPendingOutbox(1000, _utcNow())
                    .Where(item => item.ObjectPath.StartsWith(spaceEventPrefix, StringComparison.Ordinal))
                    .OrderBy(item => item.Sequence).ToArray();
                var remoteObjects = new List<RemoteSyncObject>();
                var usedDirect = false;
                var fellBack = false;

                if (enableDirectSync && _directSync != null)
                {
                    var localOutbox = uploadPending
                        ? outbox.Where(item => string.Equals(item.DeviceId, _localDeviceId, StringComparison.Ordinal)).ToArray()
                        : Array.Empty<SyncOutboxItem>();
                    var directResult = await _directSync.TrySynchronizeAsync(_profile, _localDeviceId, cursors, localOutbox, cancellationToken).ConfigureAwait(false);
                    usedDirect = directResult.Succeeded;
                    remoteObjects.AddRange(directResult.DownloadedObjects ?? Array.Empty<RemoteSyncObject>());
                    fellBack = !directResult.Succeeded;
                }

                var cloudObjects = await DownloadMissingCloudEventsAsync(cursors, remoteObjects, cancellationToken).ConfigureAwait(false);
                remoteObjects.AddRange(cloudObjects);
                var apply = await ApplyEventsAsync(remoteObjects, cancellationToken).ConfigureAwait(false);

                var uploaded = 0;
                var blocked = 0;
                foreach (var item in uploadPending ? outbox : Array.Empty<SyncOutboxItem>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        await UploadCloudObjectIdempotentlyAsync(item, cancellationToken).ConfigureAwait(false);
                        _store.MarkOutboxUploaded(item.EventId);
                        uploaded++;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (WebDavException ex) when (ex.ErrorCode == SyncErrorCodes.WebDavAuthenticationFailed)
                    {
                        _store.MarkOutboxBlocked(item.EventId, ex.ErrorCode);
                        blocked++;
                    }
                    catch (SyncSecurityException ex)
                    {
                        _store.MarkOutboxBlocked(item.EventId, ex.ErrorCode);
                        blocked++;
                    }
                    catch (WebDavException ex)
                    {
                        _store.MarkOutboxFailed(item.EventId, ex.ErrorCode, GetNextAttempt(item, ex.RetryAfter));
                    }
                    catch (IOException)
                    {
                        _store.MarkOutboxFailed(item.EventId, SyncErrorCodes.NetworkUnavailable, GetNextAttempt(item, null));
                    }
                }

                return new SyncRunResult
                {
                    AppliedEvents = apply.applied,
                    DuplicateEvents = apply.duplicates,
                    Conflicts = apply.conflicts,
                    UploadedEvents = uploaded,
                    UsedDirectSync = usedDirect,
                    FellBackToWebDav = fellBack,
                    QuarantinedObjects = apply.quarantined,
                    BlockedUploads = blocked
                };
            }
            finally
            {
                _syncLock.Release();
            }
        }

        private async Task<IReadOnlyList<RemoteSyncObject>> DownloadMissingCloudEventsAsync(
            IReadOnlyDictionary<string, long> cursors,
            IReadOnlyCollection<RemoteSyncObject> alreadyDownloaded,
            CancellationToken cancellationToken)
        {
            var prefix = $"BarTenderPrinterSync/spaces/{_profile.SpaceId}/events/";
            var rootObjects = await _cloud.ListAsync(prefix, cancellationToken).ConfigureAwait(false);
            var listed = new List<CloudObjectMetadata>();
            listed.AddRange(rootObjects.Where(item => !item.IsCollection));
            foreach (var deviceDirectory in rootObjects.Where(item => item.IsCollection))
            {
                cancellationToken.ThrowIfCancellationRequested();
                listed.AddRange(await _cloud.ListAsync(deviceDirectory.Path, cancellationToken).ConfigureAwait(false));
            }
            var missing = new List<(CloudObjectMetadata info, string deviceId, long sequence)>();
            foreach (var item in listed)
            {
                if (!TryParseEventPath(prefix, item.Path, out var deviceId, out var sequence)) continue;
                cursors.TryGetValue(deviceId, out var cursor);
                if (sequence > cursor) missing.Add((item, deviceId, sequence));
            }

            var contiguous = new List<(CloudObjectMetadata info, string deviceId, long sequence)>();
            var downloadedIdentities = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in alreadyDownloaded ?? Array.Empty<RemoteSyncObject>())
            {
                if (TryParseEventPath(prefix, item.Path, out var deviceId, out var sequence))
                    downloadedIdentities.Add($"{deviceId}:{sequence}");
            }
            foreach (var deviceId in missing.Select(item => item.deviceId)
                         .Concat(downloadedIdentities.Select(item => item.Substring(0, item.LastIndexOf(':'))))
                         .Distinct(StringComparer.Ordinal))
            {
                cursors.TryGetValue(deviceId, out var cursor);
                var expected = cursor + 1;
                var cloudBySequence = missing.Where(item => string.Equals(item.deviceId, deviceId, StringComparison.Ordinal))
                    .GroupBy(item => item.sequence).ToDictionary(group => group.Key, group => group.First());
                while (true)
                {
                    if (downloadedIdentities.Contains($"{deviceId}:{expected}"))
                    {
                        expected++;
                        continue;
                    }
                    if (!cloudBySequence.TryGetValue(expected, out var item)) break;
                    contiguous.Add(item);
                    expected++;
                }
            }

            var result = new List<RemoteSyncObject>();
            foreach (var item in contiguous.OrderBy(value => value.deviceId, StringComparer.Ordinal).ThenBy(value => value.sequence))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var cloudObject = await _cloud.GetAsync(item.info.Path, cancellationToken).ConfigureAwait(false);
                result.Add(new RemoteSyncObject { Path = item.info.Path, Content = cloudObject.Content });
            }
            return result;
        }

        private async Task<(int applied, int duplicates, int conflicts, int quarantined)> ApplyEventsAsync(
            IEnumerable<RemoteSyncObject> objects,
            CancellationToken cancellationToken)
        {
            var candidates = objects
                .GroupBy(item => item.Path, StringComparer.Ordinal)
                .Select(group => group.First())
                .Select(item => TryParseEventPath($"BarTenderPrinterSync/spaces/{_profile.SpaceId}/events/", item.Path, out var deviceId, out var sequence)
                    ? (item, deviceId, sequence, valid: true) : (item, deviceId: "", sequence: 0L, valid: false))
                .ToArray();
            var applied = 0;
            var duplicates = 0;
            var conflicts = 0;
            var quarantined = 0;
            var cursors = await _eventApplier.GetCursorsAsync(_store, cancellationToken).ConfigureAwait(false);
            foreach (var invalid in candidates.Where(item => !item.valid))
            {
                _store.RecordQuarantinedObject(invalid.item.Path, SyncErrorCodes.ObjectCorrupted, _utcNow());
                quarantined++;
            }
            foreach (var deviceObjects in candidates.Where(item => item.valid).GroupBy(item => item.deviceId, StringComparer.Ordinal))
            {
                cursors.TryGetValue(deviceObjects.Key, out var cursor);
                var expected = cursor + 1;
                foreach (var candidate in deviceObjects.OrderBy(item => item.sequence))
                {
                    if (candidate.sequence < expected) continue;
                    if (candidate.sequence != expected) break;
                    try
                    {
                        VerifyHash(candidate.item.Sha256, candidate.item.Content, candidate.item.Path);
                        var syncEvent = _codec.DecodeEvent(_profile, candidate.item.Path, candidate.item.Content);
                        ValidateEvent(syncEvent);
                        var outcome = await _eventApplier.ApplyAtomicallyAsync(syncEvent, _store, cancellationToken).ConfigureAwait(false);
                        _store.RecordAppliedEncryptedBytes(syncEvent.EventId, candidate.item.Content?.LongLength ?? 0);
                        switch (outcome.Result)
                        {
                            case SyncEventApplyResult.Applied: applied++; break;
                            case SyncEventApplyResult.AlreadyApplied: duplicates++; break;
                            case SyncEventApplyResult.Conflict: conflicts++; break;
                            default: throw new InvalidOperationException("未知同步应用结果。");
                        }
                        expected++;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) when (TryGetQuarantineError(ex, out var safeErrorCode))
                    {
                        _store.RecordQuarantinedObject(candidate.item.Path, safeErrorCode, _utcNow());
                        quarantined++;
                        break;
                    }
                }
            }
            return (applied, duplicates, conflicts, quarantined);
        }

        private async Task UploadCloudObjectIdempotentlyAsync(SyncOutboxItem item, CancellationToken cancellationToken)
        {
            if (item.EncryptedBlob == null || item.EncryptedBlob.Length == 0) throw new InvalidDataException("Outbox 事件内容为空。");
            var expectedHash = SyncDataAdapter.ComputeSha256(item.EncryptedBlob);
            try
            {
                await _cloud.PutAsync(item.ObjectPath, item.EncryptedBlob, createOnly: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (WebDavPreconditionFailedException)
            {
                var existing = await _cloud.GetAsync(item.ObjectPath, cancellationToken).ConfigureAwait(false);
                var existingHash = SyncDataAdapter.ComputeSha256(existing.Content);
                if (!string.Equals(existingHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new SyncSecurityException(SyncErrorCodes.ObjectCorrupted, "远端同名事件摘要不一致，上传已永久阻断。");
            }
        }

        private DateTimeOffset GetNextAttempt(SyncOutboxItem item, TimeSpan? retryAfter)
        {
            if (retryAfter.HasValue) return _utcNow().Add(retryAfter.Value < TimeSpan.Zero ? TimeSpan.Zero : retryAfter.Value);
            var exponent = Math.Min(item.RetryCount, 30);
            var ticks = Math.Min(_maximumRetryDelay.Ticks, _initialRetryDelay.Ticks * Math.Pow(2, exponent));
            var jitter = 0.8 + Math.Clamp(_random(), 0, 1) * 0.4;
            return _utcNow().AddTicks((long)Math.Min(_maximumRetryDelay.Ticks, ticks * jitter));
        }

        private static bool TryGetQuarantineError(Exception exception, out string safeErrorCode)
        {
            if (exception is SyncSecurityException security)
            {
                safeErrorCode = security.ErrorCode == SyncErrorCodes.SchemaTooNew ? SyncErrorCodes.SchemaTooNew : SyncErrorCodes.ObjectCorrupted;
                return true;
            }
            if (exception is InvalidDataException || exception is FormatException || exception is System.Text.Json.JsonException)
            {
                safeErrorCode = SyncErrorCodes.ObjectCorrupted;
                return true;
            }
            safeErrorCode = "";
            return false;
        }

        private static void ValidateEvent(SyncEvent syncEvent)
        {
            if (syncEvent.SchemaVersion > 1) throw new SyncSecurityException(SyncErrorCodes.SchemaTooNew, "同步事件 schema 版本高于当前客户端支持版本。");
            if (syncEvent.SchemaVersion != 1) throw new InvalidDataException("同步事件 schema 版本不受支持。");
            if (string.IsNullOrWhiteSpace(syncEvent.DeviceId) || syncEvent.Sequence <= 0 ||
                !string.Equals(syncEvent.EventId, $"{syncEvent.DeviceId}:{syncEvent.Sequence}", StringComparison.Ordinal))
                throw new InvalidDataException("同步事件身份无效。");
            if (string.IsNullOrWhiteSpace(syncEvent.EntityType) || string.IsNullOrWhiteSpace(syncEvent.EntityId))
                throw new InvalidDataException("同步事件实体身份无效。");
            if (syncEvent.NewVersion <= syncEvent.BaseVersion) throw new InvalidDataException("同步事件版本无效。");
        }

        private static bool TryParseEventPath(string prefix, string path, out string deviceId, out long sequence)
        {
            deviceId = "";
            sequence = 0;
            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith(prefix, StringComparison.Ordinal)) return false;
            var relative = path.Substring(prefix.Length).Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (relative.Length != 2 || !relative[1].EndsWith(".evt", StringComparison.OrdinalIgnoreCase)) return false;
            deviceId = relative[0];
            return !deviceId.Contains("..", StringComparison.Ordinal) &&
                long.TryParse(Path.GetFileNameWithoutExtension(relative[1]), out sequence) && sequence > 0;
        }

        private static void VerifyHash(string expected, byte[] content, string objectPath)
        {
            if (string.IsNullOrWhiteSpace(expected)) return;
            var actual = SyncDataAdapter.ComputeSha256(content ?? Array.Empty<byte>());
            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"同步对象摘要校验失败: {objectPath}");
        }

    }
}
