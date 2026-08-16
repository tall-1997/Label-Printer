using BarTenderPrinter.Application.Identity;
using Xunit;

namespace BarTenderPrinter.Application.Tests;

public sealed class PlatformCapabilitiesTests
{
    [Fact]
    public void ResolveCombinesCapabilitiesWithoutDuplicates()
    {
        var capabilities = PlatformCapabilities.Resolve(["ProductionOperator", "PackagingOperator"]);

        Assert.Contains("workspace.use", capabilities);
        Assert.Contains("production.view", capabilities);
        Assert.Equal(capabilities.Count, capabilities.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(capabilities.Order(StringComparer.Ordinal), capabilities);
    }

    [Fact]
    public void UnknownRoleHasNoCapabilities()
    {
        Assert.Empty(PlatformCapabilities.Resolve(["UnknownRole"]));
    }
}
