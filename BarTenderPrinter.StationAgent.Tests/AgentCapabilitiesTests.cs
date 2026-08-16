using BarTenderPrinter.StationAgent;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BarTenderPrinter.StationAgent.Tests;

public sealed class AgentCapabilitiesTests
{
    [Fact]
    public void CreateReturnsLoopbackAgentCapabilitySnapshot()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["StationAgent:StationId"] = "station-07" }).Build();
        var now = new DateTimeOffset(2026, 8, 16, 2, 0, 0, TimeSpan.Zero);

        var health = AgentCapabilities.Create(configuration, now);

        Assert.Equal("healthy", health.Status);
        Assert.Equal("station-07", health.StationId);
        Assert.Equal(now, health.UtcNow);
        Assert.Contains(health.Capabilities, capability => capability.Id == "bartender");
        Assert.Contains(health.Capabilities, capability => capability.Id == "scale" && capability.Simulated);
        Assert.Contains(health.Capabilities, capability => capability.Id == "offline-outbox" && capability.State == "ready");
    }
}
