using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace BarTenderPrinter
{
    public sealed class SyncEntityState
    {
        public string EntityType { get; set; } = "";
        public string EntityId { get; set; } = "";
        public long Version { get; set; }
        public string ContentHash { get; set; } = "";
        public DateTimeOffset UpdatedAtUtc { get; set; }
    }

    public sealed class SyncStore
    {
        private readonly string _databasePath;

        public SyncStore(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath)) throw new ArgumentException("同步数据库路径不能为空。", nameof(databasePath));
            _databasePath = databasePath;
            var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(databasePath));
            if (!string.IsNullOrEmpty(directory)) System.IO.Directory.CreateDirectory(directory);
            EnsureDatabase();
        }

        public long ReserveNextSequence(string deviceId)
        {
            RequireId(deviceId, nameof(deviceId));
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction(deferred: false);
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT OR IGNORE INTO LocalSequences (DeviceId, LastSequence) VALUES ($deviceId, 0)";
            insert.Parameters.AddWithValue("$deviceId", deviceId);
            insert.ExecuteNonQuery();
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE LocalSequences SET LastSequence = LastSequence + 1 WHERE DeviceId = $deviceId RETURNING LastSequence";
            update.Parameters.AddWithValue("$deviceId", deviceId);
            var sequence = Convert.ToInt64(update.ExecuteScalar(), CultureInfo.InvariantCulture);
            transaction.Commit();
            return sequence;
        }

        public bool Enqueue(SyncOutboxItem item)
        {
            ValidateOutbox(item);
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT OR IGNORE INTO SyncOutbox (EventId, DeviceId, Sequence, ObjectPath, EncryptedBlob, State, RetryCount, LastErrorCode, CreatedAtUtc, NextAttemptAtUtc, PermanentFailure) VALUES ($eventId, $deviceId, $sequence, $objectPath, $blob, $state, $retryCount, $errorCode, $createdAt, $nextAttempt, $permanent)";
            Add(command, "$eventId", item.EventId);
            Add(command, "$deviceId", item.DeviceId);
            Add(command, "$sequence", item.Sequence);
            Add(command, "$objectPath", item.ObjectPath);
            Add(command, "$blob", item.EncryptedBlob);
            Add(command, "$state", item.State.ToString());
            Add(command, "$retryCount", item.RetryCount);
            Add(command, "$errorCode", item.LastErrorCode ?? "");
            Add(command, "$createdAt", Format(item.CreatedAtUtc == default ? DateTimeOffset.UtcNow : item.CreatedAtUtc));
            Add(command, "$nextAttempt", item.NextAttemptAtUtc.HasValue ? Format(item.NextAttemptAtUtc.Value) : DBNull.Value);
            Add(command, "$permanent", item.PermanentFailure ? 1 : 0);
            return command.ExecuteNonQuery() == 1;
        }

        public IReadOnlyList<SyncOutboxItem> GetPendingOutbox(int limit, DateTimeOffset? nowUtc = null)
        {
            if (limit < 1 || limit > 1000) throw new ArgumentOutOfRangeException(nameof(limit));
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT EventId, DeviceId, Sequence, ObjectPath, EncryptedBlob, State, RetryCount, LastErrorCode, CreatedAtUtc, NextAttemptAtUtc, PermanentFailure FROM SyncOutbox WHERE State IN ('Pending','Failed') AND PermanentFailure = 0 AND (NextAttemptAtUtc IS NULL OR NextAttemptAtUtc <= $now) ORDER BY Sequence, CreatedAtUtc LIMIT $limit";
            Add(command, "$now", Format(nowUtc ?? DateTimeOffset.UtcNow));
            Add(command, "$limit", limit);
            using var reader = command.ExecuteReader();
            var result = new List<SyncOutboxItem>();
            while (reader.Read()) result.Add(ReadOutbox(reader));
            return result;
        }

        public SyncOutboxItem GetOutbox(string eventId)
        {
            RequireId(eventId, nameof(eventId));
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT EventId, DeviceId, Sequence, ObjectPath, EncryptedBlob, State, RetryCount, LastErrorCode, CreatedAtUtc, NextAttemptAtUtc, PermanentFailure FROM SyncOutbox WHERE EventId = $eventId LIMIT 1";
            Add(command, "$eventId", eventId);
            using var reader = command.ExecuteReader();
            return reader.Read() ? ReadOutbox(reader) : null;
        }

        public IReadOnlyList<SyncOutboxItem> GetOutboxForDirectSync(int limit)
        {
            if (limit < 1 || limit > 1000) throw new ArgumentOutOfRangeException(nameof(limit));
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT EventId, DeviceId, Sequence, ObjectPath, EncryptedBlob, State, RetryCount, LastErrorCode, CreatedAtUtc, NextAttemptAtUtc, PermanentFailure FROM SyncOutbox WHERE PermanentFailure = 0 ORDER BY DeviceId, Sequence LIMIT $limit";
            Add(command, "$limit", limit);
            using var reader = command.ExecuteReader();
            var result = new List<SyncOutboxItem>();
            while (reader.Read()) result.Add(ReadOutbox(reader));
            return result;
        }

        public void MarkOutboxUploaded(string eventId)
        {
            UpdateOutbox(eventId, SyncOutboxState.Uploaded, "", null, false);
        }

        public void MarkOutboxFailed(string eventId, string safeErrorCode, DateTimeOffset? nextAttemptAtUtc)
        {
            if (safeErrorCode != null && safeErrorCode.Length > 100) throw new ArgumentOutOfRangeException(nameof(safeErrorCode));
            UpdateOutbox(eventId, SyncOutboxState.Failed, safeErrorCode ?? "", nextAttemptAtUtc, true);
        }

        public void MarkOutboxBlocked(string eventId, string safeErrorCode)
        {
            if (safeErrorCode != null && safeErrorCode.Length > 100) throw new ArgumentOutOfRangeException(nameof(safeErrorCode));
            RequireId(eventId, nameof(eventId));
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE SyncOutbox SET State = 'Failed', LastErrorCode = $errorCode, NextAttemptAtUtc = NULL, PermanentFailure = 1 WHERE EventId = $eventId AND State <> 'Uploaded'";
            Add(command, "$errorCode", safeErrorCode ?? "");
            Add(command, "$eventId", eventId);
            if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("同步队列事件不存在或状态已确认。 ");
        }

        public void RecordQuarantinedObject(string objectPath, string safeErrorCode, DateTimeOffset? seenAtUtc = null)
        {
            if (string.IsNullOrWhiteSpace(objectPath) || objectPath.Length > 1024) throw new ArgumentException("隔离对象路径无效。", nameof(objectPath));
            if (string.IsNullOrWhiteSpace(safeErrorCode) || safeErrorCode.Length > 100) throw new ArgumentException("隔离错误码无效。", nameof(safeErrorCode));
            var seenAt = Format(seenAtUtc ?? DateTimeOffset.UtcNow);
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO QuarantinedObjects (ObjectPath, SafeErrorCode, FirstSeenAtUtc, LastSeenAtUtc, OccurrenceCount) VALUES ($path, $error, $seenAt, $seenAt, 1) ON CONFLICT(ObjectPath, SafeErrorCode) DO UPDATE SET LastSeenAtUtc = excluded.LastSeenAtUtc, OccurrenceCount = QuarantinedObjects.OccurrenceCount + 1";
            Add(command, "$path", objectPath);
            Add(command, "$error", safeErrorCode);
            Add(command, "$seenAt", seenAt);
            command.ExecuteNonQuery();
        }

        public IReadOnlyList<QuarantinedSyncObject> GetQuarantinedObjects(int limit = 100)
        {
            if (limit < 1 || limit > 1000) throw new ArgumentOutOfRangeException(nameof(limit));
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT ObjectPath, SafeErrorCode, FirstSeenAtUtc, LastSeenAtUtc, OccurrenceCount FROM QuarantinedObjects ORDER BY LastSeenAtUtc DESC LIMIT $limit";
            Add(command, "$limit", limit);
            using var reader = command.ExecuteReader();
            var result = new List<QuarantinedSyncObject>();
            while (reader.Read()) result.Add(new QuarantinedSyncObject
            {
                ObjectPath = reader.GetString(0), SafeErrorCode = reader.GetString(1), FirstSeenAtUtc = Parse(reader.GetString(2)),
                LastSeenAtUtc = Parse(reader.GetString(3)), OccurrenceCount = reader.GetInt32(4)
            });
            return result;
        }

        public int GetQuarantinedObjectCount()
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM QuarantinedObjects";
            return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        public int GetPermanentOutboxFailureCount()
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM SyncOutbox WHERE PermanentFailure = 1";
            return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        public bool HasLocalSyncState()
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT EXISTS(SELECT 1 FROM EntityVersions UNION ALL SELECT 1 FROM AppliedEvents UNION ALL SELECT 1 FROM DeviceCursors UNION ALL SELECT 1 FROM SyncOutbox)";
            return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 0;
        }

        public SyncSnapshotProgress GetSnapshotProgress()
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"SELECT (SELECT COUNT(*) FROM AppliedEvents WHERE EventId NOT IN (SELECT EventId FROM SyncOutbox)) + (SELECT COUNT(*) FROM SyncOutbox),
COALESCE((SELECT SUM(EncryptedBytes) FROM AppliedEvents WHERE EventId NOT IN (SELECT EventId FROM SyncOutbox)), 0) + COALESCE((SELECT SUM(LENGTH(EncryptedBlob)) FROM SyncOutbox), 0), LastEventCount, LastEncryptedBytes, LastSnapshotId
FROM SnapshotState WHERE SingletonId = 1";
            using var reader = command.ExecuteReader();
            reader.Read();
            return new SyncSnapshotProgress
            {
                EventCount = reader.GetInt64(0), EncryptedBytes = reader.GetInt64(1), LastSnapshotEventCount = reader.GetInt64(2),
                LastSnapshotEncryptedBytes = reader.GetInt64(3), LastSnapshotId = reader.GetString(4)
            };
        }

        public void MarkSnapshotCreated(string snapshotId, long eventCount, long encryptedBytes)
        {
            RequireId(snapshotId, nameof(snapshotId));
            if (eventCount < 0 || encryptedBytes < 0) throw new ArgumentOutOfRangeException(nameof(eventCount));
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE SnapshotState SET LastEventCount = $events, LastEncryptedBytes = $bytes, LastSnapshotId = $snapshotId WHERE SingletonId = 1";
            Add(command, "$events", eventCount);
            Add(command, "$bytes", encryptedBytes);
            Add(command, "$snapshotId", snapshotId);
            command.ExecuteNonQuery();
        }

        public long GetCursor(string remoteDeviceId)
        {
            RequireId(remoteDeviceId, nameof(remoteDeviceId));
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT LastAppliedSequence FROM DeviceCursors WHERE RemoteDeviceId = $deviceId LIMIT 1";
            Add(command, "$deviceId", remoteDeviceId);
            var value = command.ExecuteScalar();
            return value == null || value == DBNull.Value ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }

        public void AdvanceCursor(string remoteDeviceId, long sequence)
        {
            RequireId(remoteDeviceId, nameof(remoteDeviceId));
            if (sequence < 0) throw new ArgumentOutOfRangeException(nameof(sequence));
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO DeviceCursors (RemoteDeviceId, LastAppliedSequence) VALUES ($deviceId, $sequence) ON CONFLICT(RemoteDeviceId) DO UPDATE SET LastAppliedSequence = excluded.LastAppliedSequence WHERE excluded.LastAppliedSequence > DeviceCursors.LastAppliedSequence";
            Add(command, "$deviceId", remoteDeviceId);
            Add(command, "$sequence", sequence);
            command.ExecuteNonQuery();
        }

        public bool RecordAppliedEvent(string eventId, string remoteDeviceId, long sequence, DateTimeOffset? appliedAtUtc = null)
        {
            RequireId(eventId, nameof(eventId));
            RequireId(remoteDeviceId, nameof(remoteDeviceId));
            if (sequence < 1) throw new ArgumentOutOfRangeException(nameof(sequence));
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction(deferred: false);
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT OR IGNORE INTO AppliedEvents (EventId, AppliedAtUtc) VALUES ($eventId, $appliedAt)";
            Add(insert, "$eventId", eventId);
            Add(insert, "$appliedAt", Format(appliedAtUtc ?? DateTimeOffset.UtcNow));
            var inserted = insert.ExecuteNonQuery() == 1;
            if (inserted)
            {
                using var cursor = connection.CreateCommand();
                cursor.Transaction = transaction;
                cursor.CommandText = "INSERT INTO DeviceCursors (RemoteDeviceId, LastAppliedSequence) VALUES ($deviceId, $sequence) ON CONFLICT(RemoteDeviceId) DO UPDATE SET LastAppliedSequence = excluded.LastAppliedSequence WHERE excluded.LastAppliedSequence > DeviceCursors.LastAppliedSequence";
                Add(cursor, "$deviceId", remoteDeviceId);
                Add(cursor, "$sequence", sequence);
                cursor.ExecuteNonQuery();
            }
            transaction.Commit();
            return inserted;
        }

        public bool IsEventApplied(string eventId)
        {
            RequireId(eventId, nameof(eventId));
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM AppliedEvents WHERE EventId = $eventId LIMIT 1";
            Add(command, "$eventId", eventId);
            return command.ExecuteScalar() != null;
        }

        public void RecordAppliedEncryptedBytes(string eventId, long encryptedBytes)
        {
            RequireId(eventId, nameof(eventId));
            if (encryptedBytes < 0) throw new ArgumentOutOfRangeException(nameof(encryptedBytes));
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE AppliedEvents SET EncryptedBytes = CASE WHEN EncryptedBytes < $bytes THEN $bytes ELSE EncryptedBytes END WHERE EventId = $eventId";
            Add(command, "$bytes", encryptedBytes);
            Add(command, "$eventId", eventId);
            command.ExecuteNonQuery();
        }

        public bool AddConflict(SyncConflict conflict)
        {
            if (conflict == null) throw new ArgumentNullException(nameof(conflict));
            RequireId(conflict.ConflictId, nameof(conflict.ConflictId));
            RequireId(conflict.EntityType, nameof(conflict.EntityType));
            RequireId(conflict.EntityId, nameof(conflict.EntityId));
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT OR IGNORE INTO SyncConflicts (ConflictId, EntityType, EntityId, LocalJson, RemoteJson, State, ResolutionJson, CreatedAtUtc, ResolvedAtUtc) VALUES ($conflictId, $entityType, $entityId, $localJson, $remoteJson, $state, $resolutionJson, $createdAt, $resolvedAt)";
            Add(command, "$conflictId", conflict.ConflictId);
            Add(command, "$entityType", conflict.EntityType);
            Add(command, "$entityId", conflict.EntityId);
            Add(command, "$localJson", conflict.LocalJson ?? "");
            Add(command, "$remoteJson", conflict.RemoteJson ?? "");
            Add(command, "$state", conflict.State.ToString());
            Add(command, "$resolutionJson", conflict.ResolutionJson ?? "");
            Add(command, "$createdAt", Format(conflict.CreatedAtUtc == default ? DateTimeOffset.UtcNow : conflict.CreatedAtUtc));
            Add(command, "$resolvedAt", conflict.ResolvedAtUtc.HasValue ? Format(conflict.ResolvedAtUtc.Value) : DBNull.Value);
            return command.ExecuteNonQuery() == 1;
        }

        public IReadOnlyList<SyncConflict> GetPendingConflicts()
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT ConflictId, EntityType, EntityId, LocalJson, RemoteJson, State, ResolutionJson, CreatedAtUtc, ResolvedAtUtc FROM SyncConflicts WHERE State = 'Pending' ORDER BY CreatedAtUtc";
            using var reader = command.ExecuteReader();
            var result = new List<SyncConflict>();
            while (reader.Read())
            {
                result.Add(new SyncConflict
                {
                    ConflictId = reader.GetString(0), EntityType = reader.GetString(1), EntityId = reader.GetString(2),
                    LocalJson = reader.GetString(3), RemoteJson = reader.GetString(4), State = Enum.Parse<SyncConflictState>(reader.GetString(5)),
                    ResolutionJson = reader.GetString(6), CreatedAtUtc = Parse(reader.GetString(7)), ResolvedAtUtc = reader.IsDBNull(8) ? null : Parse(reader.GetString(8))
                });
            }
            return result;
        }

        public bool ResolveConflict(string conflictId, string resolutionJson, DateTimeOffset? resolvedAtUtc = null)
        {
            RequireId(conflictId, nameof(conflictId));
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE SyncConflicts SET State = 'Resolved', ResolutionJson = $resolution, ResolvedAtUtc = $resolvedAt WHERE ConflictId = $conflictId AND State = 'Pending'";
            Add(command, "$resolution", resolutionJson ?? "");
            Add(command, "$resolvedAt", Format(resolvedAtUtc ?? DateTimeOffset.UtcNow));
            Add(command, "$conflictId", conflictId);
            return command.ExecuteNonQuery() == 1;
        }

        public SyncConflict GetConflict(string conflictId)
        {
            RequireId(conflictId, nameof(conflictId));
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT ConflictId, EntityType, EntityId, LocalJson, RemoteJson, State, ResolutionJson, CreatedAtUtc, ResolvedAtUtc FROM SyncConflicts WHERE ConflictId = $conflictId LIMIT 1";
            Add(command, "$conflictId", conflictId);
            using var reader = command.ExecuteReader();
            return reader.Read() ? new SyncConflict
            {
                ConflictId = reader.GetString(0), EntityType = reader.GetString(1), EntityId = reader.GetString(2),
                LocalJson = reader.GetString(3), RemoteJson = reader.GetString(4), State = Enum.Parse<SyncConflictState>(reader.GetString(5)),
                ResolutionJson = reader.GetString(6), CreatedAtUtc = Parse(reader.GetString(7)), ResolvedAtUtc = reader.IsDBNull(8) ? null : Parse(reader.GetString(8))
            } : null;
        }

        public void UpsertKnownDevice(KnownSyncDevice device)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            RequireId(device.DeviceId, nameof(device.DeviceId));
            if (device.EndpointVersion < 0) throw new ArgumentOutOfRangeException(nameof(device.EndpointVersion));
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO KnownDevices (DeviceId, EndpointVersion, EndpointJson, LastResult, UpdatedAtUtc) VALUES ($deviceId, $version, $json, $result, $updatedAt) ON CONFLICT(DeviceId) DO UPDATE SET EndpointVersion = excluded.EndpointVersion, EndpointJson = excluded.EndpointJson, LastResult = excluded.LastResult, UpdatedAtUtc = excluded.UpdatedAtUtc WHERE excluded.EndpointVersion >= KnownDevices.EndpointVersion";
            Add(command, "$deviceId", device.DeviceId);
            Add(command, "$version", device.EndpointVersion);
            Add(command, "$json", device.EndpointJson ?? "");
            Add(command, "$result", device.LastResult ?? "");
            Add(command, "$updatedAt", Format(device.UpdatedAtUtc == default ? DateTimeOffset.UtcNow : device.UpdatedAtUtc));
            command.ExecuteNonQuery();
        }

        public KnownSyncDevice GetKnownDevice(string deviceId)
        {
            RequireId(deviceId, nameof(deviceId));
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT DeviceId, EndpointVersion, EndpointJson, LastResult, UpdatedAtUtc FROM KnownDevices WHERE DeviceId = $deviceId LIMIT 1";
            Add(command, "$deviceId", deviceId);
            using var reader = command.ExecuteReader();
            if (!reader.Read()) return null;
            return new KnownSyncDevice { DeviceId = reader.GetString(0), EndpointVersion = reader.GetInt64(1), EndpointJson = reader.GetString(2), LastResult = reader.GetString(3), UpdatedAtUtc = Parse(reader.GetString(4)) };
        }

        public void AddUsage(string period, long uploadedBytes, long downloadedBytes, long requestCount)
        {
            if (string.IsNullOrWhiteSpace(period) || period.Length > 20) throw new ArgumentException("用量周期无效。", nameof(period));
            if (uploadedBytes < 0 || downloadedBytes < 0 || requestCount < 0) throw new ArgumentOutOfRangeException(nameof(uploadedBytes));
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO SyncUsage (Period, UploadedBytes, DownloadedBytes, RequestCount) VALUES ($period, $uploaded, $downloaded, $requests) ON CONFLICT(Period) DO UPDATE SET UploadedBytes = UploadedBytes + excluded.UploadedBytes, DownloadedBytes = DownloadedBytes + excluded.DownloadedBytes, RequestCount = RequestCount + excluded.RequestCount";
            Add(command, "$period", period);
            Add(command, "$uploaded", uploadedBytes);
            Add(command, "$downloaded", downloadedBytes);
            Add(command, "$requests", requestCount);
            command.ExecuteNonQuery();
        }

        public SyncUsage GetUsage(string period)
        {
            if (string.IsNullOrWhiteSpace(period)) throw new ArgumentException("用量周期不能为空。", nameof(period));
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT UploadedBytes, DownloadedBytes, RequestCount FROM SyncUsage WHERE Period = $period LIMIT 1";
            Add(command, "$period", period);
            using var reader = command.ExecuteReader();
            return reader.Read()
                ? new SyncUsage { Period = period, UploadedBytes = reader.GetInt64(0), DownloadedBytes = reader.GetInt64(1), RequestCount = reader.GetInt64(2) }
                : new SyncUsage { Period = period };
        }

        public void AddActivity(string description, DateTimeOffset? occurredAtUtc = null)
        {
            if (string.IsNullOrWhiteSpace(description) || description.Length > 1000)
                throw new ArgumentException("同步活动描述无效。", nameof(description));
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO SyncActivities (Description, OccurredAtUtc) VALUES ($description, $occurredAt)";
            Add(command, "$description", description);
            Add(command, "$occurredAt", Format(occurredAtUtc ?? DateTimeOffset.UtcNow));
            command.ExecuteNonQuery();
        }

        public IReadOnlyList<SyncActivity> GetRecentActivities(int limit)
        {
            if (limit < 1 || limit > 100) throw new ArgumentOutOfRangeException(nameof(limit));
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT ActivityId, Description, OccurredAtUtc FROM SyncActivities ORDER BY ActivityId DESC LIMIT $limit";
            Add(command, "$limit", limit);
            using var reader = command.ExecuteReader();
            var result = new List<SyncActivity>();
            while (reader.Read()) result.Add(new SyncActivity
            {
                ActivityId = reader.GetInt64(0), Description = reader.GetString(1), OccurredAtUtc = Parse(reader.GetString(2))
            });
            return result;
        }

        public SyncEntityState GetEntityState(string entityType, string entityId)
        {
            RequireId(entityType, nameof(entityType));
            RequireId(entityId, nameof(entityId));
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Version, ContentHash, UpdatedAtUtc FROM EntityVersions WHERE EntityType = $type AND EntityId = $id LIMIT 1";
            Add(command, "$type", entityType);
            Add(command, "$id", entityId);
            using var reader = command.ExecuteReader();
            return reader.Read() ? new SyncEntityState
            {
                EntityType = entityType,
                EntityId = entityId,
                Version = reader.GetInt64(0),
                ContentHash = reader.GetString(1),
                UpdatedAtUtc = Parse(reader.GetString(2))
            } : new SyncEntityState { EntityType = entityType, EntityId = entityId };
        }

        public void UpsertEntityState(string entityType, string entityId, long version, string contentHash, DateTimeOffset? updatedAtUtc = null)
        {
            RequireId(entityType, nameof(entityType));
            RequireId(entityId, nameof(entityId));
            if (version < 0) throw new ArgumentOutOfRangeException(nameof(version));
            if (string.IsNullOrWhiteSpace(contentHash) || contentHash.Length > 128) throw new ArgumentException("内容摘要无效。", nameof(contentHash));
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO EntityVersions (EntityType, EntityId, Version, ContentHash, UpdatedAtUtc) VALUES ($type, $id, $version, $hash, $updatedAt) ON CONFLICT(EntityType, EntityId) DO UPDATE SET Version = excluded.Version, ContentHash = excluded.ContentHash, UpdatedAtUtc = excluded.UpdatedAtUtc WHERE excluded.Version >= EntityVersions.Version";
            Add(command, "$type", entityType);
            Add(command, "$id", entityId);
            Add(command, "$version", version);
            Add(command, "$hash", contentHash);
            Add(command, "$updatedAt", Format(updatedAtUtc ?? DateTimeOffset.UtcNow));
            command.ExecuteNonQuery();
        }

        public IReadOnlyDictionary<string, long> GetCursors()
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT RemoteDeviceId, LastAppliedSequence FROM DeviceCursors";
            using var reader = command.ExecuteReader();
            var result = new Dictionary<string, long>(StringComparer.Ordinal);
            while (reader.Read()) result[reader.GetString(0)] = reader.GetInt64(1);
            return result;
        }

        public IReadOnlyDictionary<string, long> GetSnapshotCursors()
        {
            var result = GetCursors().ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT DeviceId, MAX(Sequence) FROM SyncOutbox WHERE State = 'Uploaded' GROUP BY DeviceId";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var deviceId = reader.GetString(0);
                var sequence = reader.GetInt64(1);
                if (!result.TryGetValue(deviceId, out var current) || sequence > current) result[deviceId] = sequence;
            }
            return result;
        }

        public IReadOnlyList<KnownSyncDevice> GetKnownDevices()
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT DeviceId, EndpointVersion, EndpointJson, LastResult, UpdatedAtUtc FROM KnownDevices ORDER BY UpdatedAtUtc DESC";
            using var reader = command.ExecuteReader();
            var result = new List<KnownSyncDevice>();
            while (reader.Read()) result.Add(new KnownSyncDevice { DeviceId = reader.GetString(0), EndpointVersion = reader.GetInt64(1), EndpointJson = reader.GetString(2), LastResult = reader.GetString(3), UpdatedAtUtc = Parse(reader.GetString(4)) });
            return result;
        }

        public (int Count, long Bytes) GetPendingOutboxSummary()
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*), COALESCE(SUM(LENGTH(EncryptedBlob)), 0) FROM SyncOutbox WHERE State IN ('Pending','Failed') AND PermanentFailure = 0";
            using var reader = command.ExecuteReader();
            reader.Read();
            return (reader.GetInt32(0), reader.GetInt64(1));
        }

        private void UpdateOutbox(string eventId, SyncOutboxState state, string errorCode, DateTimeOffset? nextAttempt, bool incrementRetry)
        {
            RequireId(eventId, nameof(eventId));
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = incrementRetry
                ? "UPDATE SyncOutbox SET State = $state, RetryCount = RetryCount + 1, LastErrorCode = $errorCode, NextAttemptAtUtc = $nextAttempt, PermanentFailure = 0 WHERE EventId = $eventId AND State <> 'Uploaded'"
                : "UPDATE SyncOutbox SET State = $state, LastErrorCode = $errorCode, NextAttemptAtUtc = $nextAttempt, PermanentFailure = 0 WHERE EventId = $eventId";
            Add(command, "$state", state.ToString());
            Add(command, "$errorCode", errorCode);
            Add(command, "$nextAttempt", nextAttempt.HasValue ? Format(nextAttempt.Value) : DBNull.Value);
            Add(command, "$eventId", eventId);
            if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("同步队列事件不存在或状态已确认。 ");
        }

        private void EnsureDatabase()
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
PRAGMA journal_mode=WAL;
PRAGMA foreign_keys=ON;
CREATE TABLE IF NOT EXISTS LocalSequences (DeviceId TEXT PRIMARY KEY, LastSequence INTEGER NOT NULL CHECK(LastSequence >= 0));
CREATE TABLE IF NOT EXISTS SyncOutbox (EventId TEXT PRIMARY KEY, DeviceId TEXT NOT NULL, Sequence INTEGER NOT NULL CHECK(Sequence > 0), ObjectPath TEXT NOT NULL, EncryptedBlob BLOB NOT NULL, State TEXT NOT NULL CHECK(State IN ('Pending','Uploaded','Failed')), RetryCount INTEGER NOT NULL CHECK(RetryCount >= 0), LastErrorCode TEXT NOT NULL, CreatedAtUtc TEXT NOT NULL, NextAttemptAtUtc TEXT NULL, PermanentFailure INTEGER NOT NULL DEFAULT 0 CHECK(PermanentFailure IN (0,1)), UNIQUE(DeviceId, Sequence));
CREATE INDEX IF NOT EXISTS IX_SyncOutbox_StateAttempt ON SyncOutbox(State, NextAttemptAtUtc, Sequence);
CREATE TABLE IF NOT EXISTS DeviceCursors (RemoteDeviceId TEXT PRIMARY KEY, LastAppliedSequence INTEGER NOT NULL CHECK(LastAppliedSequence >= 0));
CREATE TABLE IF NOT EXISTS SyncConflicts (ConflictId TEXT PRIMARY KEY, EntityType TEXT NOT NULL, EntityId TEXT NOT NULL, LocalJson TEXT NOT NULL, RemoteJson TEXT NOT NULL, State TEXT NOT NULL CHECK(State IN ('Pending','Resolved')), ResolutionJson TEXT NOT NULL, CreatedAtUtc TEXT NOT NULL, ResolvedAtUtc TEXT NULL);
CREATE INDEX IF NOT EXISTS IX_SyncConflicts_State ON SyncConflicts(State, CreatedAtUtc);
CREATE TABLE IF NOT EXISTS KnownDevices (DeviceId TEXT PRIMARY KEY, EndpointVersion INTEGER NOT NULL CHECK(EndpointVersion >= 0), EndpointJson TEXT NOT NULL, LastResult TEXT NOT NULL, UpdatedAtUtc TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS SyncUsage (Period TEXT PRIMARY KEY, UploadedBytes INTEGER NOT NULL CHECK(UploadedBytes >= 0), DownloadedBytes INTEGER NOT NULL CHECK(DownloadedBytes >= 0), RequestCount INTEGER NOT NULL CHECK(RequestCount >= 0));
CREATE TABLE IF NOT EXISTS SyncActivities (ActivityId INTEGER PRIMARY KEY AUTOINCREMENT, Description TEXT NOT NULL, OccurredAtUtc TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS AppliedEvents (EventId TEXT PRIMARY KEY, AppliedAtUtc TEXT NOT NULL, EncryptedBytes INTEGER NOT NULL DEFAULT 0 CHECK(EncryptedBytes >= 0));
CREATE TABLE IF NOT EXISTS EntityVersions (EntityType TEXT NOT NULL, EntityId TEXT NOT NULL, Version INTEGER NOT NULL CHECK(Version >= 0), ContentHash TEXT NOT NULL, UpdatedAtUtc TEXT NOT NULL, PRIMARY KEY(EntityType, EntityId));
CREATE TABLE IF NOT EXISTS QuarantinedObjects (ObjectPath TEXT NOT NULL, SafeErrorCode TEXT NOT NULL, FirstSeenAtUtc TEXT NOT NULL, LastSeenAtUtc TEXT NOT NULL, OccurrenceCount INTEGER NOT NULL CHECK(OccurrenceCount > 0), PRIMARY KEY(ObjectPath, SafeErrorCode));
CREATE TABLE IF NOT EXISTS SnapshotState (SingletonId INTEGER PRIMARY KEY CHECK(SingletonId = 1), LastEventCount INTEGER NOT NULL, LastEncryptedBytes INTEGER NOT NULL, LastSnapshotId TEXT NOT NULL);
INSERT OR IGNORE INTO SnapshotState (SingletonId, LastEventCount, LastEncryptedBytes, LastSnapshotId) VALUES (1, 0, 0, '');";
            command.ExecuteNonQuery();
            EnsureColumn(connection, "SyncOutbox", "PermanentFailure", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(connection, "AppliedEvents", "EncryptedBytes", "INTEGER NOT NULL DEFAULT 0");
        }

        private SqliteConnection OpenConnection()
        {
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _databasePath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON";
            command.ExecuteNonQuery();
            return connection;
        }

        private static SyncOutboxItem ReadOutbox(SqliteDataReader reader)
        {
            return new SyncOutboxItem
            {
                EventId = reader.GetString(0), DeviceId = reader.GetString(1), Sequence = reader.GetInt64(2), ObjectPath = reader.GetString(3),
                EncryptedBlob = (byte[])reader[4], State = Enum.Parse<SyncOutboxState>(reader.GetString(5)), RetryCount = reader.GetInt32(6),
                LastErrorCode = reader.GetString(7), CreatedAtUtc = Parse(reader.GetString(8)), NextAttemptAtUtc = reader.IsDBNull(9) ? null : Parse(reader.GetString(9)),
                PermanentFailure = reader.GetInt32(10) != 0
            };
        }

        private static void EnsureColumn(SqliteConnection connection, string table, string column, string definition)
        {
            using var inspect = connection.CreateCommand();
            inspect.CommandText = $"PRAGMA table_info({table})";
            using var reader = inspect.ExecuteReader();
            while (reader.Read()) if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return;
            reader.Close();
            using var alter = connection.CreateCommand();
            alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
            alter.ExecuteNonQuery();
        }

        private static void ValidateOutbox(SyncOutboxItem item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            RequireId(item.EventId, nameof(item.EventId));
            RequireId(item.DeviceId, nameof(item.DeviceId));
            if (item.Sequence < 1) throw new ArgumentOutOfRangeException(nameof(item.Sequence));
            if (string.IsNullOrWhiteSpace(item.ObjectPath)) throw new ArgumentException("同步对象路径不能为空。", nameof(item.ObjectPath));
            if (item.EncryptedBlob == null || item.EncryptedBlob.Length == 0) throw new ArgumentException("同步密文不能为空。", nameof(item.EncryptedBlob));
            if (item.RetryCount < 0) throw new ArgumentOutOfRangeException(nameof(item.RetryCount));
        }

        private static void RequireId(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 512) throw new ArgumentException("标识不能为空且长度不能超过 512。", parameterName);
        }

        private static void Add(SqliteCommand command, string name, object value) { command.Parameters.AddWithValue(name, value ?? DBNull.Value); }
        private static string Format(DateTimeOffset value) { return value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture); }
        private static DateTimeOffset Parse(string value) { return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind); }
    }
}
