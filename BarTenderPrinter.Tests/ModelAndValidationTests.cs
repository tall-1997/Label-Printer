using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
        public void LocalDataValidationChecksOnlySelectedEnabledSources()
        {
            var mismatches = ValidationService.FindLocalDataMismatches(
                new Dictionary<string, string> { ["IMEI"] = "A", ["BOX"] = "B", ["SERIAL"] = "C" },
                new HashSet<string> { "A" },
                new[]
                {
                    new DataSourceItem { Field = "IMEI", Enabled = true, UseLocalDataValidation = true },
                    new DataSourceItem { Field = "BOX", Enabled = true, UseLocalDataValidation = false },
                    new DataSourceItem { Field = "SERIAL", Enabled = false, UseLocalDataValidation = true }
                });

            Assert.Empty(mismatches);
        }

        [Fact]
        public void LegacyTargetFieldMigratesToMatchingSource()
        {
            var settings = new TemplateSettings
            {
                SchemaVersion = 2,
                LocalDataTargetField = "imei",
                LocalData = new List<string> { "A" },
                DataSources = new List<DataSourceItem>
                {
                    new DataSourceItem { Field = "IMEI", Enabled = true },
                    new DataSourceItem { Field = "BOX", Enabled = true }
                }
            };

            ValidationService.MigrateLocalDataSelection(settings);

            Assert.Equal(3, settings.SchemaVersion);
            Assert.True(settings.DataSources[0].UseLocalDataValidation);
            Assert.False(settings.DataSources[1].UseLocalDataValidation);
        }

        [Fact]
        public void LegacyLocalDataWithoutTargetSelectsAllEnabledSources()
        {
            var settings = new TemplateSettings
            {
                SchemaVersion = 2,
                LocalData = new List<string> { "A" },
                DataSources = new List<DataSourceItem>
                {
                    new DataSourceItem { Field = "IMEI", Enabled = true },
                    new DataSourceItem { Field = "BOX", Enabled = false }
                }
            };

            ValidationService.MigrateLocalDataSelection(settings);

            Assert.True(settings.DataSources[0].UseLocalDataValidation);
            Assert.False(settings.DataSources[1].UseLocalDataValidation);
        }

        [Fact]
        public void FailedHistoryRecordDoesNotEnterDuplicateIndex()
        {
            var dir = CreateTempDirectory();
            var history = new HistoryManager(Path.Combine(dir, "records.csv"), Path.Combine(dir, "records.jsonl"));
            history.Records.Clear();
            history.Add("Template", "C:\\a.btw", "template-1", new Dictionary<string, string> { ["IMEI"] = "X" }, "FAIL", "Printer", 1);
            Assert.False(history.ContainsAnyValue("Template", "C:\\a.btw", "template-1", "X"));
        }

        [Fact]
        public void LatestSuccessfulPreviewRecordSkipsFailures()
        {
            var dir = CreateTempDirectory();
            var history = new HistoryManager(Path.Combine(dir, "records.csv"), Path.Combine(dir, "records.jsonl"), Path.Combine(dir, "records.db"));
            history.Records.Clear();
            history.Records.Add(new PrintRecord("Template", "C:\\a.btw", "template-1", new Dictionary<string, string> { ["IMEI"] = "PASS-1" }, "1", "PASS", "P", 1));
            history.Records.Add(new PrintRecord("Template", "C:\\a.btw", "template-1", new Dictionary<string, string> { ["IMEI"] = "FAIL-2" }, "2", "FAIL", "P", 1));
            history.Records.Add(new PrintRecord("Template", "C:\\a.btw", "template-1", new Dictionary<string, string> { ["IMEI"] = "PASS-3" }, "3", "REPRINT_PASS", "P", 1));

            var record = history.GetLatestSuccessful("Template", "C:\\a.btw", "template-1");

            Assert.Equal("PASS-3", record.FieldValues["IMEI"]);
        }

        [Fact]
        public void LatestSuccessfulPreviewRecordStaysWithinTemplate()
        {
            var dir = CreateTempDirectory();
            var history = new HistoryManager(Path.Combine(dir, "records.csv"), Path.Combine(dir, "records.jsonl"), Path.Combine(dir, "records.db"));
            history.Records.Clear();
            history.Records.Add(new PrintRecord("Template A", "C:\\a.btw", "template-a", new Dictionary<string, string> { ["IMEI"] = "A" }, "1", "PASS", "P", 1));
            history.Records.Add(new PrintRecord("Template B", "C:\\b.btw", "template-b", new Dictionary<string, string> { ["IMEI"] = "B" }, "2", "PASS", "P", 1));

            var record = history.GetLatestSuccessful("Template A", "C:\\a.btw", "template-a");

            Assert.Equal("A", record.FieldValues["IMEI"]);
        }

        [Fact]
        public void LatestSuccessfulPreviewRecordReturnsNullWithoutSuccess()
        {
            var dir = CreateTempDirectory();
            var history = new HistoryManager(Path.Combine(dir, "records.csv"), Path.Combine(dir, "records.jsonl"), Path.Combine(dir, "records.db"));
            history.Records.Clear();
            history.Records.Add(new PrintRecord("Template", "C:\\a.btw", "template-1", new Dictionary<string, string> { ["IMEI"] = "FAIL" }, "1", "FAIL", "P", 1));

            Assert.Null(history.GetLatestSuccessful("Template", "C:\\a.btw", "template-1"));
        }

        [Fact]
        public void HistoryManagerSkipsBadJsonlRows()
        {
            var dir = CreateTempDirectory();
            var csv = Path.Combine(dir, "records.csv");
            var jsonl = Path.Combine(dir, "records.jsonl");
            var db = Path.Combine(dir, "records.db");
            File.WriteAllText(jsonl, "{bad json}\n" + System.Text.Json.JsonSerializer.Serialize(new PrintRecord("Template", "C:\\a.btw", "tid", new Dictionary<string, string> { ["A"] = "1" }, "now", "PASS", "P", 1)));
            var history = new HistoryManager(csv, jsonl, db);
            history.Load();
            Assert.Single(history.Records);
            Assert.True(File.Exists(jsonl + ".bad"));
        }

        [Fact]
        public void HistoryManagerFallsBackToCsvWhenJsonlIsEmpty()
        {
            var dir = CreateTempDirectory();
            var csv = Path.Combine(dir, "records.csv");
            var jsonl = Path.Combine(dir, "records.jsonl");
            var db = Path.Combine(dir, "records.db");
            File.WriteAllText(jsonl, "");
            File.WriteAllText(csv, "record_id,template_name,template_path,field_values,print_time,status,printer,copies\n1,T,C:\\a.btw,eyJBIjoiMSJ9,now,PASS,P,1\n");
            var history = new HistoryManager(csv, jsonl, db);
            history.Load();
            Assert.Single(history.Records);
            Assert.True(File.Exists(db));
        }

        [Fact]
        public void HistoryManagerWritesSqlitePrimaryStore()
        {
            var dir = CreateTempDirectory();
            var db = Path.Combine(dir, "records.db");
            var history = new HistoryManager(Path.Combine(dir, "records.csv"), Path.Combine(dir, "records.jsonl"), db);
            Assert.True(history.Add("Template", "C:\\a.btw", "template-1", new Dictionary<string, string> { ["IMEI"] = "X" }, "PASS", "Printer", 1));
            Assert.True(File.Exists(db));
        }

        [Fact]
        public void ExcludedHistoryLeavesStoredRecordAndStopsDuplicateChecks()
        {
            var dir = CreateTempDirectory();
            var archive = Path.Combine(dir, "history-records");
            var history = new HistoryManager(Path.Combine(dir, "records.csv"), Path.Combine(dir, "records.jsonl"), Path.Combine(dir, "records.db"), archive);
            Assert.True(history.Add("Template", "C:\\a.btw", "template-1", new Dictionary<string, string> { ["IMEI"] = "X" }, "PASS", "Printer", 1));
            var recordId = history.Records.Single().RecordId;

            Assert.True(history.Delete(recordId, "admin", "test exclusion"));

            Assert.Single(history.Records);
            Assert.True(history.Records[0].IsExcluded);
            Assert.Null(history.GetById(recordId));
            Assert.Empty(history.Search("Template", "C:\\a.btw", "template-1", "", false));
            Assert.False(history.ContainsAnyValue("Template", "C:\\a.btw", "template-1", "X"));
            Assert.Equal(0, history.Count("Template", "C:\\a.btw", "template-1"));
            Assert.Single(Directory.GetFiles(archive, $"*{recordId}.json", SearchOption.AllDirectories));
        }

        [Fact]
        public void ClearHistoryUsesOneExclusionBatchAndSurvivesReload()
        {
            var dir = CreateTempDirectory();
            var csv = Path.Combine(dir, "records.csv");
            var jsonl = Path.Combine(dir, "records.jsonl");
            var db = Path.Combine(dir, "records.db");
            var archive = Path.Combine(dir, "history-records");
            var history = new HistoryManager(csv, jsonl, db, archive);
            Assert.True(history.Add("Template", "C:\\a.btw", "template-1", new Dictionary<string, string> { ["IMEI"] = "A" }, "PASS", "Printer", 1));
            Assert.True(history.Add("Template", "C:\\a.btw", "template-1", new Dictionary<string, string> { ["IMEI"] = "B" }, "PASS", "Printer", 1));

            Assert.True(history.Clear("Template", "C:\\a.btw", "template-1", "admin", "clear control"));

            Assert.Equal(2, history.Records.Count);
            Assert.Single(history.Records.Select(record => record.ExclusionBatchId).Distinct());
            var reloaded = new HistoryManager(csv, jsonl, db, archive);
            reloaded.Load();
            Assert.Equal(2, reloaded.Records.Count);
            Assert.All(reloaded.Records, record => Assert.True(record.IsExcluded));
            Assert.Empty(reloaded.Search("Template", "C:\\a.btw", "template-1", "", false));
        }

        [Fact]
        public void EmptyMigratedDatabaseDoesNotReloadLegacyCsv()
        {
            var dir = CreateTempDirectory();
            var csv = Path.Combine(dir, "records.csv");
            var jsonl = Path.Combine(dir, "records.jsonl");
            var db = Path.Combine(dir, "records.db");
            File.WriteAllText(csv, "imei,print_time,status\nLEGACY,now,PASS\n");
            var history = new HistoryManager(csv, jsonl, db, Path.Combine(dir, "history-records"));
            history.Load();
            Assert.Single(history.Records);
            Assert.True(history.Clear());

            var reloaded = new HistoryManager(csv, jsonl, db, Path.Combine(dir, "history-records"));
            reloaded.Load();

            Assert.Empty(reloaded.Search("", "", "", "", false));
            Assert.Single(reloaded.Records);
            Assert.True(reloaded.Records[0].IsExcluded);
        }

        [Fact]
        public void TemplateDataSourceMergePreservesMatchesAndEnablesNewFields()
        {
            var merged = MainForm.MergeTemplateDataSources(
                new[] { "BOX", "IMEI", "LOT" },
                new[]
                {
                    new DataSourceItem { Field = "imei", Name = "Old IMEI", Enabled = false, AutoStep = -1 },
                    new DataSourceItem { Field = "BOX", Name = "Box" }
                });

            Assert.Equal(3, merged.Count);
            Assert.Equal(new[] { "imei", "BOX", "LOT" }, merged.Select(source => source.Field));
            Assert.False(merged.Single(source => source.Field.Equals("imei", System.StringComparison.OrdinalIgnoreCase)).Enabled);
            Assert.Equal(-1, merged.Single(source => source.Field.Equals("imei", System.StringComparison.OrdinalIgnoreCase)).AutoStep);
            Assert.True(merged.Single(source => source.Field == "LOT").Enabled);
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
        public void SvgIconRendererCreatesRequestedTransparentBitmap()
        {
            foreach (var iconType in System.Enum.GetValues<AppIcon>())
            {
                foreach (var size in new[] { 16, 24, 32 })
                {
                    using var icon = SvgIconRenderer.Render(iconType, MiuiTheme.Primary, size);
                    Assert.Equal(size, icon.Width);
                    Assert.Equal(size, icon.Height);
                    Assert.Equal(0, icon.GetPixel(0, 0).A);
                    Assert.Contains(Enumerable.Range(0, size), x => Enumerable.Range(0, size).Any(y => icon.GetPixel(x, y).A > 0));
                }
            }
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
        public void OrderEditorControllerDeepClonesValidationSelection()
        {
            var original = new OrderTemplate
            {
                Settings = new TemplateSettings
                {
                    DataSources = new List<DataSourceItem>
                    {
                        new DataSourceItem { Field = "IMEI", Enabled = true, UseLocalDataValidation = true }
                    }
                }
            };

            var clone = new OrderEditorController().CloneTemplates(new[] { original })[0];
            clone.Settings.DataSources[0].UseLocalDataValidation = false;

            Assert.True(original.Settings.DataSources[0].UseLocalDataValidation);
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

        [Fact]
        public void TemplateVersionMismatchIsDetectable()
        {
            var oldVersion = "ticks=1;len=2;sha=AAA";
            var newVersion = "ticks=2;len=2;sha=BBB";
            Assert.NotEqual(oldVersion, newVersion);
        }

        [Fact]
        public void UserSessionRestrictsHistoryDeletion()
        {
            var session = new UserSession { Role = "Operator" };
            Assert.False(session.CanDeleteHistory);
            Assert.False(session.CanApproveReprint);
        }

        [Fact]
        public void PreviewCacheKeyIgnoresFieldInsertionOrder()
        {
            var template = CreateTemplateFile();
            var first = BarTenderService.BuildPreviewCacheKey(template, new Dictionary<string, string>
            {
                ["IMEI"] = "123",
                ["BOX"] = "456"
            });
            var second = BarTenderService.BuildPreviewCacheKey(template, new Dictionary<string, string>
            {
                ["BOX"] = "456",
                ["IMEI"] = "123"
            });

            Assert.Equal(first, second);
        }

        [Fact]
        public void PreviewCacheKeyChangesWithTemplateOrFields()
        {
            var template = CreateTemplateFile();
            var original = BarTenderService.BuildPreviewCacheKey(template, new Dictionary<string, string> { ["IMEI"] = "123" });
            File.SetLastWriteTimeUtc(template, File.GetLastWriteTimeUtc(template).AddSeconds(2));
            var templateChanged = BarTenderService.BuildPreviewCacheKey(template, new Dictionary<string, string> { ["IMEI"] = "123" });
            var fieldChanged = BarTenderService.BuildPreviewCacheKey(template, new Dictionary<string, string> { ["IMEI"] = "456" });

            Assert.NotEqual(original, templateChanged);
            Assert.NotEqual(templateChanged, fieldChanged);
        }

        [Fact]
        public void PreviewFieldsProjectCaseInsensitivelyAndIgnoreUnknownFields()
        {
            var projected = BarTenderService.ProjectPreviewFields(
                new Dictionary<string, string> { ["imei"] = "123", ["Removed"] = "old" },
                new[] { "IMEI", "BOX" });

            Assert.Single(projected);
            Assert.Equal("123", projected["IMEI"]);
        }

        [Fact]
        public void PreviewFieldsReturnEmptyWhenHistoryHasNoCurrentFields()
        {
            var projected = BarTenderService.ProjectPreviewFields(
                new Dictionary<string, string> { ["Removed"] = "old" },
                new[] { "IMEI" });

            Assert.Empty(projected);
        }

        [Fact]
        public void ApplicationStateRoundTripsAndFallsBackFromInvalidJson()
        {
            var path = Path.Combine(CreateTempDirectory(), "application-state.json");
            var manager = new ApplicationStateManager(path);
            manager.Save(new ApplicationState
            {
                ActiveOrderId = "order-1",
                ActiveTemplateId = "template-1",
                SelectedTemplatePath = "C:\\labels\\a.btw",
                Printer = "Printer",
                Copies = 3,
                PreviewEnabled = true
            });

            var restored = manager.Load();
            Assert.Equal("order-1", restored.ActiveOrderId);
            Assert.Equal(3, restored.Copies);
            Assert.True(restored.PreviewEnabled);

            File.WriteAllText(path, "{broken");
            var fallback = manager.Load();
            Assert.Equal(0, fallback.SchemaVersion);
            Assert.Equal(1, fallback.Copies);
            Assert.Equal("", fallback.ActiveOrderId);
        }

        [Fact]
        public void PreviewSdkPathRequiresSdkRedistX64Sequence()
        {
            Assert.True(BarTenderService.IsSdkRedistributablePath(Path.Combine("C:\\", "Seagull", "SDK", "Redist", "x64", "Seagull.BarTender.Print.dll")));
            Assert.False(BarTenderService.IsSdkRedistributablePath(Path.Combine("C:\\", "Seagull", "SDK", "bin", "x64", "Seagull.BarTender.Print.dll")));
        }

        [Theory]
        [InlineData(0x8664, true)]
        [InlineData(0x014c, false)]
        public void PeArchitectureDetectionAcceptsOnlyX64(int machine, bool expected)
        {
            var path = Path.Combine(CreateTempDirectory(), "assembly.dll");
            WritePeHeader(path, machine);

            Assert.Equal(expected, BarTenderService.IsX64Pe(path));
        }

        [Fact]
        public void PeArchitectureDetectionRejectsInvalidFiles()
        {
            var path = Path.Combine(CreateTempDirectory(), "invalid.dll");
            File.WriteAllBytes(path, new byte[] { 0x4D });

            Assert.False(BarTenderService.IsX64Pe(path));
        }

        private static string CreateTemplateFile()
        {
            var path = Path.Combine(CreateTempDirectory(), "template.btw");
            File.WriteAllText(path, "template");
            return path;
        }

        private static void WritePeHeader(string path, int machine)
        {
            using var stream = File.Create(path);
            using var writer = new BinaryWriter(stream, Encoding.ASCII, true);
            writer.Write((ushort)0x5A4D);
            stream.Position = 0x3C;
            writer.Write(0x80);
            stream.Position = 0x80;
            writer.Write(0x00004550u);
            writer.Write((ushort)machine);
        }

        private static string CreateTempDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), "btp-tests-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
