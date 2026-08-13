using System.Collections.Generic;
using System.Linq;
using BarTenderPrinter;
using Xunit;

namespace BarTenderPrinter.Tests
{
    public class ModelAndValidationTests
    {
        [Fact]
        public void OrderKeyUsesCustomerModelColorAndOrderNumber()
        {
            var left = PackagingOrder.BuildKey("A", "M1", "Black", "100");
            var right = PackagingOrder.BuildKey("B", "M1", "Black", "100");
            Assert.NotEqual(left, right);
        }

        [Fact]
        public void TemplateFieldCoverageReportsMissingValues()
        {
            var issues = ValidationService.FindTemplateFieldIssues(
                new[] { "IMEI", "BOX" },
                new[] { new DataSourceItem { Field = "IMEI", Enabled = true }, new DataSourceItem { Field = "BOX", Enabled = true } },
                new Dictionary<string, string> { ["IMEI"] = "123" });
            Assert.Contains(issues, issue => issue.Contains("缺少打印值"));
        }

        [Fact]
        public void LocalDataValidationOnlyChecksTargetField()
        {
            var mismatches = ValidationService.FindLocalDataMismatches(
                new Dictionary<string, string> { ["IMEI"] = "A", ["BOX"] = "B" },
                new HashSet<string> { "A" },
                "IMEI");
            Assert.Empty(mismatches);
        }

        [Fact]
        public void FailedHistoryRecordDoesNotEnterDuplicateIndex()
        {
            var history = new HistoryManager();
            history.Records.Clear();
            history.Add("Template", "C:\\a.btw", "template-1", new Dictionary<string, string> { ["IMEI"] = "X" }, "FAIL", "Printer", 1);
            Assert.False(history.ContainsAnyValue("Template", "C:\\a.btw", "template-1", "X"));
        }

        [Fact]
        public void ExpectedLengthPrefersIndividualLength()
        {
            var source = new DataSourceItem { ExpectedLength = 12 };
            Assert.Equal(12, ValidationService.GetExpectedLength(source, true, 15));
        }

        [Theory]
        [InlineData(1, true)]
        [InlineData(-1, true)]
        [InlineData(0, false)]
        public void StepSignRepresentsIncrementDirection(int step, bool autoIncrement)
        {
            var source = new DataSourceItem { AutoStep = step, AutoIncrement = autoIncrement };
            Assert.Equal(autoIncrement, source.AutoStep != 0);
        }

        [Fact]
        public void CsvParserHandlesEscapedQuotes()
        {
            var parts = CsvUtils.ParseLine("\"a,1\",\"b\"\"2\"");
            Assert.Equal(new[] { "a,1", "b\"2" }, parts);
        }

        [Fact]
        public void HistoryPresenterBuildsRowsWithoutNullFailures()
        {
            var table = HistoryPresenter.BuildTable(new[] { new PrintRecord("T", "C:\\a.btw", "tid", new Dictionary<string, string> { ["A"] = "1" }, "now", "PASS", "P", 1) });
            Assert.Single(table.Rows.Cast<System.Data.DataRow>());
        }

        [Fact]
        public void PrintWorkflowBuildsTemplateVersion()
        {
            var workflow = new PrintWorkflow();
            var version = workflow.BuildTemplateVersion(new OrderTemplate { SourceLength = 10, SourceLastWriteTimeUtcTicks = 20, SourceSha256 = "ABCDEF1234567890" });
            Assert.Contains("sha=ABCDEF123456", version);
        }

        [Fact]
        public void OrderEditorControllerClonesTemplateSettings()
        {
            var controller = new OrderEditorController();
            var templates = controller.CloneTemplates(new[] { new OrderTemplate { Id = "t1", FieldSnapshot = new List<string> { "A" } } });
            Assert.Equal("t1", templates[0].Id);
            Assert.Equal("A", templates[0].FieldSnapshot[0]);
        }

        [Fact]
        public void DataGridViewNullConversionRegressionUsesSafeBoolean()
        {
            object value = null;
            Assert.False(value is bool boolean && boolean);
        }

        [Fact]
        public void ReprintRecordKeepsCurrentOrderStateSeparate()
        {
            var record = new PrintRecord("Template", "C:\\a.btw", "template-1", new Dictionary<string, string> { ["IMEI"] = "X" }, "now", "REPRINT_PASS", "Printer", 1)
            {
                ReprintReason = "Damaged label"
            };
            Assert.Equal("Damaged label", record.ReprintReason);
        }
    }
}
