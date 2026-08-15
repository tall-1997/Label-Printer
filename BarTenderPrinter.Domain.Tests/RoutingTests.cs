using BarTenderPrinter.Domain.Common;
using BarTenderPrinter.Domain.Orders;
using BarTenderPrinter.Domain.Production;
using BarTenderPrinter.Domain.Rework;
using BarTenderPrinter.Domain.Routing;
using Xunit;

namespace BarTenderPrinter.Domain.Tests;

public sealed class RoutingTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Route_SortsOperationsAndResolvesPreviousOperation()
    {
        var route = CreateRoute(RouteType.Standard);

        Assert.Equal(new[] { "OP-1", "OP-2", "OP-3" }, route.Operations.Select(operation => operation.Id));
        Assert.Equal("OP-1", route.GetPrevious("OP-2")?.Id);
        Assert.Equal("OP-3", route.GetNext("OP-2")?.Id);
    }

    [Fact]
    public void StationPass_RequiresPreviousOperation()
    {
        var fixture = CreateFixture();

        var result = fixture.Service.Pass(fixture.Command("OP-2", "second"));

        Assert.False(result.IsSuccess);
        Assert.Equal(StationPassErrorCodes.PreviousOperationIncomplete, result.Error?.Code);
        Assert.Equal("OP-1", result.MissingOperationId);
    }

    [Fact]
    public void StationPass_RecordsSequentialRouteAndReturnsIdempotentResult()
    {
        var fixture = CreateFixture();
        var firstCommand = fixture.Command("OP-1", "first");

        var first = fixture.Service.Pass(firstCommand);
        var repeated = fixture.Service.Pass(firstCommand);
        var second = fixture.Service.Pass(fixture.Command("OP-2", "second"));

        Assert.True(first.IsSuccess);
        Assert.Same(first, repeated);
        Assert.True(second.IsSuccess);
        Assert.Equal(2, fixture.Service.GetRouteHistory(fixture.Unit.Id).Count);
    }

    [Fact]
    public void StationPass_RejectsSameIdempotencyKeyWithDifferentRequest()
    {
        var fixture = CreateFixture();
        fixture.Service.Pass(fixture.Command("OP-1", "same"));

        var conflict = fixture.Service.Pass(fixture.Command("OP-2", "same"));

        Assert.Equal(StationPassErrorCodes.IdempotencyConflict, conflict.Error?.Code);
    }

    [Fact]
    public void StationPass_RejectsUnqualifiedStation()
    {
        var fixture = CreateFixture(new[] { "OP-2" });

        var result = fixture.Service.Pass(fixture.Command("OP-1", "first"));

        Assert.Equal(StationPassErrorCodes.StationNotQualified, result.Error?.Code);
    }

    [Fact]
    public void ReworkRoute_RequiresMatchingActiveReworkOrder()
    {
        var fixture = CreateFixture(routeType: RouteType.Rework);
        var missing = fixture.Service.Pass(fixture.Command("OP-1", "missing"));
        var rework = new ReworkOrder(EntityId.New(), fixture.Unit.Id, fixture.Route.Id, "QA_FAIL", "OP-1", 1);
        rework.Approve("supervisor");
        rework.Activate();

        var success = fixture.Service.Pass(fixture.Command("OP-1", "rework", rework));

        Assert.Equal(StationPassErrorCodes.ReworkContextRequired, missing.Error?.Code);
        Assert.True(success.IsSuccess);
        Assert.Equal(rework.Id.Value, success.Record?.ReworkOrderId);
        Assert.Equal(1, success.Record?.ReworkSequence);
    }

    [Fact]
    public void RouteProperty_SuccessfulPassesAlwaysHaveCompletedPredecessors()
    {
        for (var length = 1; length <= 20; length++)
        {
            var fixture = CreateFixture(operationCount: length);
            for (var sequence = 1; sequence <= length; sequence++)
            {
                var result = fixture.Service.Pass(fixture.Command($"OP-{sequence}", $"key-{sequence}"));
                Assert.True(result.IsSuccess);
                var history = fixture.Service.GetRouteHistory(fixture.Unit.Id);
                Assert.Equal(sequence, history.Count);
                for (var predecessor = 1; predecessor < sequence; predecessor++)
                    Assert.Contains(history, record => record.OperationId == $"OP-{predecessor}");
            }
        }
    }

    private static Fixture CreateFixture(
        IEnumerable<string>? qualifications = null,
        RouteType routeType = RouteType.Standard,
        int operationCount = 3)
    {
        var order = new ProductionOrder(EntityId.New(), "PO-1", "Customer", "Model", "Black", 100);
        order.Publish();
        order.StartProduction(Now);
        var unit = new ProductionUnit(EntityId.New(), order.Id);
        unit.Activate();
        var route = CreateRoute(routeType, order.Id, operationCount);
        var station = new Station(EntityId.New(), "Assembly", qualifications ?? route.Operations.Select(operation => operation.Id));
        return new Fixture(order, unit, route, station, new StationPassService());
    }

    private static ManufacturingRoute CreateRoute(RouteType type, EntityId? orderId = null, int operationCount = 3) =>
        new(EntityId.New(), orderId ?? EntityId.New(), "Assembly", type,
            Enumerable.Range(1, operationCount)
                .Reverse()
                .Select(sequence => new ManufacturingOperation
                {
                    Id = $"OP-{sequence}",
                    Name = $"Operation {sequence}",
                    Sequence = sequence
                }));

    private sealed record Fixture(
        ProductionOrder Order,
        ProductionUnit Unit,
        ManufacturingRoute Route,
        Station Station,
        StationPassService Service)
    {
        public StationPassCommand Command(string operationId, string key, ReworkOrder? reworkOrder = null) => new()
        {
            Order = Order,
            Unit = Unit,
            Route = Route,
            Station = Station,
            OperationId = operationId,
            OperatorId = "operator",
            IdempotencyKey = new IdempotencyKey(key),
            OccurredAtUtc = Now,
            ReworkOrder = reworkOrder
        };
    }
}
