using BarTenderPrinter.Domain.Common;
using BarTenderPrinter.Domain.Packaging;
using Xunit;

namespace BarTenderPrinter.Domain.Tests;

public sealed class PackagingTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Bind_BuildsFourLevelPackagingTreeAndCreatesPrintIntents()
    {
        var orderId = EntityId.New();
        var service = new PackagingService();
        var body = Unit(orderId, PackagingUnitType.Body, "BODY-1", 0);
        var box = Unit(orderId, PackagingUnitType.ColorBox, "BOX-1", 1);
        var carton = Unit(orderId, PackagingUnitType.Carton, "CARTON-1", 1);
        var pallet = Unit(orderId, PackagingUnitType.Pallet, "PALLET-1", 1);
        Register(service, body, box, carton, pallet);

        var boxResult = Bind(service, box, body);
        var cartonResult = Bind(service, carton, box);
        var palletResult = Bind(service, pallet, carton);

        Assert.True(boxResult.IsSuccess);
        Assert.Null(boxResult.PrintIntent);
        Assert.Equal(PackagingUnitStatus.Closed, box.Status);
        Assert.Equal(LabelType.Carton, cartonResult.PrintIntent?.LabelType);
        Assert.Equal("CARTON-1", cartonResult.PrintIntent?.Fields["PACKAGE_CODE"]);
        Assert.Equal("BOX-1", cartonResult.PrintIntent?.Fields["CHILD_CODES"]);
        Assert.Equal(LabelType.Pallet, palletResult.PrintIntent?.LabelType);
        Assert.Equal(PackagingUnitStatus.Closed, pallet.Status);
    }

    [Fact]
    public void Bind_RejectsSecondActiveParent()
    {
        var orderId = EntityId.New();
        var service = new PackagingService();
        var body = Unit(orderId, PackagingUnitType.Body, "BODY", 0);
        var first = Unit(orderId, PackagingUnitType.ColorBox, "BOX-1", 2);
        var second = Unit(orderId, PackagingUnitType.ColorBox, "BOX-2", 2);
        Register(service, body, first, second);
        Assert.True(Bind(service, first, body).IsSuccess);

        var duplicate = Bind(service, second, body);

        Assert.Equal(PackagingErrorCodes.BindingConflict, duplicate.Error?.Code);
        Assert.Equal(first.Id, service.GetActiveBinding(body.Id)?.ParentId);
    }

    [Fact]
    public void Bind_RejectsProductMismatchAndInvalidHierarchy()
    {
        var orderId = EntityId.New();
        var service = new PackagingService();
        var body = Unit(orderId, PackagingUnitType.Body, "BODY", 0, color: "Black");
        var wrongColorBox = Unit(orderId, PackagingUnitType.ColorBox, "BOX", 1, color: "Blue");
        var pallet = Unit(orderId, PackagingUnitType.Pallet, "PALLET", 1);
        Register(service, body, wrongColorBox, pallet);

        Assert.Equal(PackagingErrorCodes.ProductMismatch, Bind(service, wrongColorBox, body).Error?.Code);
        Assert.Equal(PackagingErrorCodes.TypeMismatch, Bind(service, pallet, body).Error?.Code);
    }

    [Fact]
    public void Unbind_RemovesOpenParentBinding()
    {
        var orderId = EntityId.New();
        var service = new PackagingService();
        var body = Unit(orderId, PackagingUnitType.Body, "BODY", 0);
        var box = Unit(orderId, PackagingUnitType.ColorBox, "BOX", 2);
        Register(service, body, box);
        Bind(service, box, body);

        var result = service.Unbind(box.Id, body.Id, box.Version, "operator", Now);

        Assert.True(result.IsSuccess);
        Assert.Empty(box.ChildIds);
        Assert.Null(service.GetActiveBinding(body.Id));
    }

    [Fact]
    public void Bind_UsesOptimisticVersionCheck()
    {
        var orderId = EntityId.New();
        var service = new PackagingService();
        var body = Unit(orderId, PackagingUnitType.Body, "BODY", 0);
        var box = Unit(orderId, PackagingUnitType.ColorBox, "BOX", 1);
        Register(service, body, box);

        var result = service.Bind(box.Id, body.Id, 99, body.Version, "operator", Now);

        Assert.Equal(PackagingErrorCodes.ConcurrencyConflict, result.Error?.Code);
    }

    [Fact]
    public void PackagingProperty_AnySuccessfulBindingSequenceRespectsCapacityAndSingleParent()
    {
        for (var capacity = 1; capacity <= 20; capacity++)
        {
            var orderId = EntityId.New();
            var service = new PackagingService();
            var carton = Unit(orderId, PackagingUnitType.Carton, $"CARTON-{capacity}", capacity);
            service.Register(carton);
            var boxes = Enumerable.Range(1, capacity)
                .Select(index => ClosedColorBox(service, orderId, $"BOX-{capacity}-{index}"))
                .ToArray();

            foreach (var box in boxes)
            {
                var result = Bind(service, carton, box);
                Assert.True(result.IsSuccess);
                Assert.True(carton.ChildIds.Count <= carton.Capacity);
                Assert.Equal(carton.Id, service.GetActiveBinding(box.Id)?.ParentId);
            }

            Assert.Equal(capacity, carton.ChildIds.Distinct().Count());
            Assert.Equal(PackagingUnitStatus.Closed, carton.Status);
        }
    }

    private static PackagingUnit ClosedColorBox(PackagingService service, EntityId orderId, string code)
    {
        var body = Unit(orderId, PackagingUnitType.Body, $"{code}-BODY", 0);
        var box = Unit(orderId, PackagingUnitType.ColorBox, code, 1);
        Register(service, body, box);
        Assert.True(Bind(service, box, body).IsSuccess);
        return box;
    }

    private static PackagingOperationResult Bind(PackagingService service, PackagingUnit parent, PackagingUnit child) =>
        service.Bind(parent.Id, child.Id, parent.Version, child.Version, "operator", Now);

    private static PackagingUnit Unit(
        EntityId orderId,
        PackagingUnitType type,
        string code,
        int capacity,
        string model = "Model",
        string color = "Black") =>
        new(EntityId.New(), orderId, type, code, model, color, capacity);

    private static void Register(PackagingService service, params PackagingUnit[] units)
    {
        foreach (var unit in units) service.Register(unit);
    }
}
