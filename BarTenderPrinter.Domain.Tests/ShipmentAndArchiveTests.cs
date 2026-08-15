using BarTenderPrinter.Domain.Archiving;
using BarTenderPrinter.Domain.Common;
using BarTenderPrinter.Domain.Shipping;
using Xunit;

namespace BarTenderPrinter.Domain.Tests;

public sealed class ShipmentAndArchiveTests
{
    [Fact]
    public void ShipmentConfirmsOnlyAtPlannedQuantity()
    {
        var shipment = new Shipment(EntityId.New(), EntityId.New(), "Customer", 2, "DELIVERY-1");
        shipment.AddItem(new ShipmentItem(EntityId.New(), 2, DateTimeOffset.UtcNow, "warehouse"));

        shipment.Confirm();

        Assert.Equal(ShipmentStatus.Confirmed, shipment.Status);
        Assert.Equal(2, shipment.ActualQuantity);
    }

    [Fact]
    public void ArchiveSnapshotHashIsStableAndPayloadIsImmutable()
    {
        const string payload = "{\"orderId\":\"order-1\"}";
        var first = new OrderArchiveSnapshot(EntityId.New(), new EntityId("order-1"), payload,
            DateTimeOffset.UtcNow, "archiver");
        var second = new OrderArchiveSnapshot(EntityId.New(), new EntityId("order-1"), payload,
            DateTimeOffset.UtcNow, "archiver");

        Assert.Equal(first.PayloadHash, second.PayloadHash);
        Assert.Equal(payload, first.PayloadJson);
    }
}
