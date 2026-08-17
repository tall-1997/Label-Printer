using Xunit;

namespace BarTenderPrinter.Tests
{
    public sealed class SyncLayoutTests
    {
        [Theory]
        [InlineData(919, 96, 2)]
        [InlineData(920, 96, 4)]
        [InlineData(619, 96, 1)]
        [InlineData(620, 96, 2)]
        [InlineData(1149, 120, 2)]
        [InlineData(1150, 120, 4)]
        [InlineData(1839, 192, 2)]
        [InlineData(1840, 192, 4)]
        public void MetricColumnsFollowScaledBreakpoints(int width, int dpi, int expected)
        {
            Assert.Equal(expected, SyncLayoutPolicy.GetMetricColumnCount(width, dpi));
        }

        [Theory]
        [InlineData(919, 96, false)]
        [InlineData(920, 96, true)]
        [InlineData(1379, 144, false)]
        [InlineData(1380, 144, true)]
        [InlineData(1839, 192, false)]
        [InlineData(1840, 192, true)]
        public void ContentColumnsFollowScaledWideBreakpoint(int width, int dpi, bool expected)
        {
            Assert.Equal(expected, SyncLayoutPolicy.UseTwoColumnContent(width, dpi));
        }
    }
}
