using System.Text.Json;
using BarTenderPrinter.Application.Auditing;
using BarTenderPrinter.Application.Idempotency;
using Xunit;

namespace BarTenderPrinter.Application.Tests;

public sealed class CanonicalRequestDigestTests
{
    [Fact]
    public void EquivalentJsonProducesSameDigest()
    {
        var digest = new CanonicalRequestDigest();
        using var first = JsonDocument.Parse("{\"b\":2,\"a\":{\"d\":4,\"c\":3}}");
        using var second = JsonDocument.Parse("{\"a\":{\"c\":3,\"d\":4},\"b\":2}");

        Assert.Equal(digest.Compute(first.RootElement), digest.Compute(second.RootElement));
    }

    [Fact]
    public void ArrayOrderChangesDigest()
    {
        var digest = new CanonicalRequestDigest();
        Assert.NotEqual(digest.Compute(new[] { 1, 2 }), digest.Compute(new[] { 2, 1 }));
    }

    [Fact]
    public void AuditPayloadMasksIdentifiersAndHashesDiagnostics()
    {
        var json = AuditSanitizer.Serialize(new { Imei = "861234567890123", Result = new { token = "secret" } });

        Assert.DoesNotContain("861234567890123", json);
        Assert.DoesNotContain("secret", json);
        Assert.Contains("sha256", json);
    }
}
