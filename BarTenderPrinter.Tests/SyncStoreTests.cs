using System;
using System.IO;
using System.Linq;
using BarTenderPrinter;
using Xunit;

namespace BarTenderPrinter.Tests
{
    public sealed class SyncStoreTests
    {
        [Fact]
        public void OutboxIsIdempotentAndPreservesEncryptedBytes()
        {
            var store = CreateStore();
            var item = NewOutboxItem();

            Assert.True(store.Enqueue(item));
            Assert.False(store.Enqueue(item));
            var pending = Assert.Single(store.GetPendingOutbox(10));

            Assert.Equal(item.EncryptedBlob, pending.EncryptedBlob);
            store.MarkOutboxFailed(item.EventId, SyncErrorCodes.NetworkUnavailable, DateTimeOffset.UtcNow.AddMinutes(-1));
            pending = Assert.Single(store.GetPendingOutbox(10));
            Assert.Equal(1, pending.RetryCount);
            Assert.Equal(SyncErrorCodes.NetworkUnavailable, pending.LastErrorCode);
            store.MarkOutboxUploaded(item.EventId);
            Assert.Empty(store.GetPendingOutbox(10));
        }

        [Fact]
        public void SequencesIncreaseAndCursorNeverMovesBackward()
        {
            var store = CreateStore();

            Assert.Equal(1, store.ReserveNextSequence("device-a"));
            Assert.Equal(2, store.ReserveNextSequence("device-a"));
            Assert.Equal(1, store.ReserveNextSequence("device-b"));
            store.AdvanceCursor("remote", 8);
            store.AdvanceCursor("remote", 3);

            Assert.Equal(8, store.GetCursor("remote"));
        }

        [Fact]
        public void AppliedEventAndCursorAreCommittedIdempotently()
        {
            var store = CreateStore();

            Assert.True(store.RecordAppliedEvent("remote:4", "remote", 4));
            Assert.False(store.RecordAppliedEvent("remote:4", "remote", 4));

            Assert.True(store.IsEventApplied("remote:4"));
            Assert.Equal(4, store.GetCursor("remote"));
        }

        [Fact]
        public void ConflictsDevicesAndUsageArePersisted()
        {
            var store = CreateStore();
            Assert.True(store.AddConflict(new SyncConflict
            {
                ConflictId = "conflict-1", EntityType = "Order", EntityId = "order-1",
                LocalJson = "{\"version\":1}", RemoteJson = "{\"version\":2}"
            }));
            store.UpsertKnownDevice(new KnownSyncDevice { DeviceId = "device-1", EndpointVersion = 2, EndpointJson = "{}", LastResult = "ok" });
            store.AddUsage("2026-08", 10, 20, 1);
            store.AddUsage("2026-08", 5, 7, 2);

            Assert.Single(store.GetPendingConflicts());
            Assert.True(store.ResolveConflict("conflict-1", "{\"choice\":\"remote\"}"));
            Assert.Empty(store.GetPendingConflicts());
            Assert.Equal(2, store.GetKnownDevice("device-1").EndpointVersion);
            var usage = store.GetUsage("2026-08");
            Assert.Equal(15, usage.UploadedBytes);
            Assert.Equal(27, usage.DownloadedBytes);
            Assert.Equal(3, usage.RequestCount);
        }

        [Fact]
        public void ValuesThatContainSqlAreStoredAsData()
        {
            var store = CreateStore();
            var dangerous = "device'; DROP TABLE SyncUsage; --";
            store.UpsertKnownDevice(new KnownSyncDevice { DeviceId = dangerous, EndpointVersion = 1, EndpointJson = "{}" });
            store.AddUsage("2026-08", 1, 1, 1);

            Assert.Equal(dangerous, store.GetKnownDevice(dangerous).DeviceId);
            Assert.Equal(1, store.GetUsage("2026-08").RequestCount);
        }

        [Fact]
        public void QuarantineStoresOnlySafeMetadataAndCountsOccurrences()
        {
            var store = CreateStore();
            var first = new DateTimeOffset(2026, 8, 17, 10, 0, 0, TimeSpan.Zero);
            store.RecordQuarantinedObject("events/device-a/1.evt", SyncErrorCodes.ObjectCorrupted, first);
            store.RecordQuarantinedObject("events/device-a/1.evt", SyncErrorCodes.ObjectCorrupted, first.AddMinutes(1));

            var item = Assert.Single(store.GetQuarantinedObjects());
            Assert.Equal("events/device-a/1.evt", item.ObjectPath);
            Assert.Equal(SyncErrorCodes.ObjectCorrupted, item.SafeErrorCode);
            Assert.Equal(first, item.FirstSeenAtUtc);
            Assert.Equal(first.AddMinutes(1), item.LastSeenAtUtc);
            Assert.Equal(2, item.OccurrenceCount);
        }

        [Fact]
        public void PendingOutboxHonorsNextAttemptAndPermanentBlock()
        {
            var store = CreateStore();
            var now = new DateTimeOffset(2026, 8, 17, 10, 0, 0, TimeSpan.Zero);
            var delayed = NewOutboxItem();
            store.Enqueue(delayed);
            store.MarkOutboxFailed(delayed.EventId, SyncErrorCodes.NetworkUnavailable, now.AddMinutes(1));
            Assert.Empty(store.GetPendingOutbox(10, now));
            Assert.Single(store.GetPendingOutbox(10, now.AddMinutes(1)));
            store.MarkOutboxBlocked(delayed.EventId, SyncErrorCodes.WebDavAuthenticationFailed);
            Assert.Empty(store.GetPendingOutbox(10, now.AddDays(1)));
            Assert.Equal(1, store.GetPermanentOutboxFailureCount());
        }

        private static SyncStore CreateStore()
        {
            var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            return new SyncStore(Path.Combine(directory, "sync.db"));
        }

        private static SyncOutboxItem NewOutboxItem()
        {
            return new SyncOutboxItem
            {
                EventId = "device-1:1", DeviceId = "device-1", Sequence = 1,
                ObjectPath = "events/device-1/1.evt", EncryptedBlob = new byte[] { 4, 5, 6 }, CreatedAtUtc = DateTimeOffset.UtcNow
            };
        }
    }
}
