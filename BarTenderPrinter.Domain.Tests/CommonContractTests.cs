using System.Text.Json;
using BarTenderPrinter.Domain.Common;
using Xunit;

namespace BarTenderPrinter.Domain.Tests;

public sealed class CommonContractTests
{
    [Fact]
    public void OperationResult_RoundTrips_WithStablePropertyNames()
    {
        var original = OperationResult<string>.Failure(ErrorCodes.Conflict, "请求冲突", true);

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<OperationResult<string>>(json);

        Assert.NotNull(restored);
        Assert.False(restored.IsSuccess);
        Assert.Equal(ErrorCodes.Conflict, restored.Error?.Code);
        Assert.True(restored.Error?.Retryable);
        Assert.Contains("\"IsSuccess\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Error\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void AuditContext_RoundTrips_WithoutChangingUtcIndependentFields()
    {
        var original = new AuditContext
        {
            ActorId = "operator-1",
            StationId = "station-1",
            ShiftId = "shift-a",
            CorrelationId = "correlation-1"
        };

        var restored = JsonSerializer.Deserialize<AuditContext>(JsonSerializer.Serialize(original));

        Assert.Equal(original, restored);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void IdempotencyKey_RejectsEmptyValues(string value)
    {
        Assert.Throws<ArgumentException>(() => new IdempotencyKey(value));
    }

    [Fact]
    public void SystemClock_ReturnsUtcTime()
    {
        Assert.Equal(TimeSpan.Zero, SystemUtcClock.Instance.UtcNow.Offset);
    }
}
