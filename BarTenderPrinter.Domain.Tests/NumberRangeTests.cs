using System.Collections.Concurrent;
using BarTenderPrinter.Domain.Common;
using BarTenderPrinter.Domain.Numbering;
using Xunit;

namespace BarTenderPrinter.Domain.Tests;

public sealed class NumberRangeTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Allocate_FormatsPrefixDateAndFixedWidthNumber()
    {
        var range = CreateRange(2, 9, 1, 3, NumberDatePattern.YyyyMm);

        var result = range.Allocate(new IdempotencyKey("request-1"), "station", "operator", Now);

        Assert.True(result.IsSuccess);
        Assert.Equal("SN202608002", result.Value?.Value);
    }

    [Fact]
    public void Allocate_ReturnsOriginalAllocationForSameIdempotencyKey()
    {
        var range = CreateRange(1, 10);
        var key = new IdempotencyKey("same-request");

        var first = range.Allocate(key, "station", "operator", Now);
        var second = range.Allocate(key, "station", "operator", Now.AddMinutes(1));

        Assert.Same(first.Value, second.Value);
        Assert.Equal(2, range.NextValue);
        Assert.Equal(1, range.Version);
    }

    [Fact]
    public void Allocate_StopsAtRangeBoundary()
    {
        var range = CreateRange(8, 10, 2);

        Assert.True(range.Allocate(new IdempotencyKey("1"), "s", "o", Now).IsSuccess);
        Assert.True(range.Allocate(new IdempotencyKey("2"), "s", "o", Now).IsSuccess);
        var exhausted = range.Allocate(new IdempotencyKey("3"), "s", "o", Now);

        Assert.False(exhausted.IsSuccess);
        Assert.Equal("NUMBER_RANGE_EXHAUSTED", exhausted.Error?.Code);
    }

    [Fact]
    public void Allocate_DoesNotRepeatLongMaxValueBoundary()
    {
        var range = CreateRange(long.MaxValue, long.MaxValue);

        var first = range.Allocate(new IdempotencyKey("max-1"), "s", "o", Now);
        var second = range.Allocate(new IdempotencyKey("max-2"), "s", "o", Now);

        Assert.True(first.IsSuccess);
        Assert.False(second.IsSuccess);
        Assert.Equal("NUMBER_RANGE_EXHAUSTED", second.Error?.Code);
    }

    [Fact]
    public async Task Allocate_IsUniqueUnderConcurrentRequests()
    {
        var range = CreateRange(1, 100);
        var values = new ConcurrentBag<string>();

        await Task.WhenAll(Enumerable.Range(1, 100).Select(async index =>
        {
            await Task.Yield();
            var result = range.Allocate(new IdempotencyKey($"request-{index}"), "s", "o", Now);
            Assert.True(result.IsSuccess);
            values.Add(result.Value!.Value);
        }));

        Assert.Equal(100, values.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void AllocationProperty_AllGeneratedNumbersStayWithinConfiguredRange()
    {
        for (var start = 0; start < 10; start++)
        {
            for (var step = 1; step <= 5; step++)
            {
                var end = start + 25;
                var range = CreateRange(start, end, step);
                var allocated = new HashSet<string>(StringComparer.Ordinal);
                for (var index = 0; index < 30; index++)
                {
                    var result = range.Allocate(new IdempotencyKey($"{start}-{step}-{index}"), "s", "o", Now);
                    if (!result.IsSuccess) break;
                    Assert.True(allocated.Add(result.Value!.Value));
                    var numeric = long.Parse(result.Value.Value[2..]);
                    Assert.InRange(numeric, start, end);
                }
            }
        }
    }

    private static NumberRange CreateRange(
        long start,
        long end,
        long step = 1,
        int width = 0,
        NumberDatePattern datePattern = NumberDatePattern.None) =>
        new(EntityId.New(), EntityId.New(), NumberType.SerialNumber, "SN", datePattern, start, end, step, width);
}
