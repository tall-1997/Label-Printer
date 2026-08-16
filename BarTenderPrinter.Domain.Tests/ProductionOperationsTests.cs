using BarTenderPrinter.Domain.Common;
using BarTenderPrinter.Domain.Production;
using Xunit;

namespace BarTenderPrinter.Domain.Tests;

public sealed class ProductionOperationsTests
{
    [Theory]
    [InlineData(99.999, WeightMeasurementResult.Failed)]
    [InlineData(100, WeightMeasurementResult.Passed)]
    [InlineData(150, WeightMeasurementResult.Passed)]
    [InlineData(150.001, WeightMeasurementResult.Failed)]
    public void WeightRuleEvaluatesInclusiveRange(decimal weight, WeightMeasurementResult expected)
    {
        var rule = new WeightRule(EntityId.New(), EntityId.New(), "Carton", 100, 150, "kg");

        Assert.Equal(expected, rule.Evaluate(weight, "KG"));
    }

    [Fact]
    public void WeightRuleRejectsMismatchedUnit()
    {
        var rule = new WeightRule(EntityId.New(), EntityId.New(), "Carton", 100, 150, "kg");

        var exception = Assert.Throws<InvalidOperationException>(() => rule.Evaluate(120, "g"));
        Assert.Equal("WEIGHT_UNIT_MISMATCH", exception.Message);
    }
}
