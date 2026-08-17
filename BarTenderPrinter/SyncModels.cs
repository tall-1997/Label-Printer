using System;
using System.Collections.Generic;
using System.Net;

namespace BarTenderPrinter
{
    public static class SyncErrorCodes
    {
        public const string NetworkUnavailable = "SYNC_NETWORK_UNAVAILABLE";
        public const string WebDavAuthenticationFailed = "WEBDAV_AUTH_FAILED";
        public const string WebDavRateLimited = "WEBDAV_RATE_LIMITED";
        public const string WebDavPreconditionFailed = "WEBDAV_PRECONDITION_FAILED";
        public const string ObjectCorrupted = "SYNC_OBJECT_CORRUPTED";
        public const string SchemaTooNew = "SYNC_SCHEMA_TOO_NEW";
        public const string Conflict = "SYNC_CONFLICT";
        public const string StorageQuota = "SYNC_STORAGE_QUOTA";
        public const string InvalidProfile = "SYNC_PROFILE_INVALID";
        public const string ObjectNotFound = "SYNC_OBJECT_NOT_FOUND";
    }

    public class SyncException : Exception
    {
        public SyncException(string errorCode, string message, Exception innerException = null)
            : base(message, innerException)
        {
            ErrorCode = errorCode ?? "SYNC_ERROR";
        }

        public string ErrorCode { get; }
    }

    public sealed class SyncSecurityException : SyncException
    {
        public SyncSecurityException(string errorCode, string message, Exception innerException = null)
            : base(errorCode, message, innerException) { }
    }

    public class WebDavException : SyncException
    {
        public WebDavException(string errorCode, string message, HttpStatusCode? statusCode = null, TimeSpan? retryAfter = null, Exception innerException = null)
            : base(errorCode, message, innerException)
        {
            StatusCode = statusCode;
            RetryAfter = retryAfter;
        }

        public HttpStatusCode? StatusCode { get; }
        public TimeSpan? RetryAfter { get; }
    }

    public sealed class WebDavPreconditionFailedException : WebDavException
    {
        public WebDavPreconditionFailedException(HttpStatusCode statusCode)
            : base(SyncErrorCodes.WebDavPreconditionFailed, "远端对象已被其他设备更新，请重新同步后重试。", statusCode) { }
    }

    public sealed class WebDavNotFoundException : WebDavException
    {
        public WebDavNotFoundException(HttpStatusCode statusCode)
            : base(SyncErrorCodes.ObjectNotFound, "远端同步对象不存在。", statusCode) { }
    }

    public sealed class CloudObjectMetadata
    {
        public string Path { get; set; } = "";
        public string ETag { get; set; } = "";
        public long ContentLength { get; set; }
        public bool IsCollection { get; set; }
        public DateTimeOffset? LastModifiedUtc { get; set; }
    }

    public sealed class CloudObject
    {
        public byte[] Content { get; set; } = Array.Empty<byte>();
        public CloudObjectMetadata Metadata { get; set; } = new CloudObjectMetadata();
    }

    public interface ICloudObjectStore : IDisposable
    {
        System.Threading.Tasks.Task EnsureCollectionAsync(string path, System.Threading.CancellationToken cancellationToken = default);
        System.Threading.Tasks.Task<IReadOnlyList<CloudObjectMetadata>> ListAsync(string path, System.Threading.CancellationToken cancellationToken = default);
        System.Threading.Tasks.Task<CloudObject> GetAsync(string path, System.Threading.CancellationToken cancellationToken = default);
        System.Threading.Tasks.Task<CloudObjectMetadata> HeadAsync(string path, System.Threading.CancellationToken cancellationToken = default);
        System.Threading.Tasks.Task<CloudObjectMetadata> PutAsync(string path, byte[] content, string ifMatch = null, bool createOnly = false, System.Threading.CancellationToken cancellationToken = default);
    }

    public enum SyncOutboxState { Pending, Uploaded, Failed }
    public enum SyncConflictState { Pending, Resolved }

    public sealed class SyncOutboxItem
    {
        public string EventId { get; set; } = "";
        public string DeviceId { get; set; } = "";
        public long Sequence { get; set; }
        public string ObjectPath { get; set; } = "";
        public byte[] EncryptedBlob { get; set; } = Array.Empty<byte>();
        public SyncOutboxState State { get; set; } = SyncOutboxState.Pending;
        public int RetryCount { get; set; }
        public string LastErrorCode { get; set; } = "";
        public DateTimeOffset CreatedAtUtc { get; set; }
        public DateTimeOffset? NextAttemptAtUtc { get; set; }
        public bool PermanentFailure { get; set; }
    }

    public sealed class QuarantinedSyncObject
    {
        public string ObjectPath { get; set; } = "";
        public string SafeErrorCode { get; set; } = "";
        public DateTimeOffset FirstSeenAtUtc { get; set; }
        public DateTimeOffset LastSeenAtUtc { get; set; }
        public int OccurrenceCount { get; set; }
    }

    public sealed class SyncSnapshotProgress
    {
        public long EventCount { get; set; }
        public long EncryptedBytes { get; set; }
        public long LastSnapshotEventCount { get; set; }
        public long LastSnapshotEncryptedBytes { get; set; }
        public string LastSnapshotId { get; set; } = "";
    }

    public sealed class SyncConflict
    {
        public string ConflictId { get; set; } = "";
        public string EntityType { get; set; } = "";
        public string EntityId { get; set; } = "";
        public string LocalJson { get; set; } = "";
        public string RemoteJson { get; set; } = "";
        public SyncConflictState State { get; set; } = SyncConflictState.Pending;
        public string ResolutionJson { get; set; } = "";
        public DateTimeOffset CreatedAtUtc { get; set; }
        public DateTimeOffset? ResolvedAtUtc { get; set; }
    }

    public sealed class KnownSyncDevice
    {
        public string DeviceId { get; set; } = "";
        public long EndpointVersion { get; set; }
        public string EndpointJson { get; set; } = "";
        public string LastResult { get; set; } = "";
        public DateTimeOffset UpdatedAtUtc { get; set; }
    }

    public sealed class SyncUsage
    {
        public string Period { get; set; } = "";
        public long UploadedBytes { get; set; }
        public long DownloadedBytes { get; set; }
        public long RequestCount { get; set; }
    }

    public sealed class SyncActivity
    {
        public long ActivityId { get; set; }
        public string Description { get; set; } = "";
        public DateTimeOffset OccurredAtUtc { get; set; }
    }
}
