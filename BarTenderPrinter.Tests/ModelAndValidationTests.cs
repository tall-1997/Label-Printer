using System;
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
        public void OrderKeyCannotCollideWhenValuesContainSeparators()
        {
            var left = PackagingOrder.BuildKey("A|B", "C", "Black", "100");
            var right = PackagingOrder.BuildKey("A", "B|C", "Black", "100");

            Assert.NotEqual(left, right);
        }

        [Fact]
        public void OrderCascadeFiltersEachLevelByItsPredecessors()
        {
            var orders = new[]
            {
                new PackagingOrder { Customer = "A", ProductModel = "M1", Color = "Black", OrderNumber = "10" },
                new PackagingOrder { Customer = "A", ProductModel = "M2", Color = "White", OrderNumber = "20" },
                new PackagingOrder { Customer = "B", ProductModel = "M1", Color = "Blue", OrderNumber = "30" }
            };

            Assert.Equal(new[] { "M1", "M2" }, OrderCascadeService.GetModels(orders, "A"));
            Assert.Equal(new[] { "Black" }, OrderCascadeService.GetColors(orders, "A", "M1"));
            Assert.Equal(new[] { "10" }, OrderCascadeService.GetOrderNumbers(orders, "A", "M1", "Black"));
        }

        [Fact]
        public void OrderCascadeReturnsNoDownstreamCandidatesForNewValues()
        {
            var orders = new[] { new PackagingOrder { Customer = "A", ProductModel = "M1", Color = "Black", OrderNumber = "10" } };

            Assert.Empty(OrderCascadeService.GetModels(orders, "New customer"));
            Assert.Empty(OrderCascadeService.GetColors(orders, "A", "New model"));
            Assert.Empty(OrderCascadeService.GetOrderNumbers(orders, "A", "M1", "New color"));
            Assert.True(OrderCascadeService.Contains(new[] { "M1" }, "m1"));
        }

        [Fact]
        public void UpdatingOrderKeepsStableIdentity()
        {
            var directory = CreateTempDirectory();
            var manager = new OrderManager(Path.Combine(directory, "orders.json"));
            var original = new PackagingOrder { Id = "stable-order", Customer = "A", ProductModel = "M1", Color = "Black", OrderNumber = "10" };
            manager.Add(original);
            var updated = new PackagingOrder { Id = original.Id, Customer = "A", ProductModel = "M2", Color = "White", OrderNumber = "20" };

            manager.Add(updated, original.Key);

            Assert.Single(manager.Orders);
            Assert.Equal("stable-order", manager.Orders[0].OrderId);
            Assert.Equal(updated.Key, manager.Orders[0].Key);
        }

        [Fact]
        public void CorruptAccountFileIsPreserved()
        {
            var directory = CreateTempDirectory();
            var path = Path.Combine(directory, "accounts.json");
            const string corruptContent = "{invalid json";
            File.WriteAllText(path, corruptContent);

            var manager = new AccountManager(path);

            Assert.NotNull(manager.LoadError);
            Assert.Null(manager.DefaultAccount);
            Assert.Equal(corruptContent, File.ReadAllText(path));
        }

        [Fact]
        public void MissingAccountFileCreatesFixedSuperAdministrator()
        {
            var directory = CreateTempDirectory();
            var path = Path.Combine(directory, "accounts.json");

            var manager = new AccountManager(path);

            Assert.Null(manager.LoadError);
            Assert.NotNull(manager.DefaultAccount);
            Assert.Equal("admin123", manager.BootstrapPassword);
            Assert.True(manager.TryLogin("superadmin", "admin123", out var account));
            Assert.Equal("Admin", account.Role);
            Assert.StartsWith("PBKDF2-SHA256$", account.PasswordHash);
            Assert.True(File.Exists(path));
        }

        [Fact]
        public void ExistingSuperAdministratorPasswordIsResetToFixedPassword()
        {
            var directory = CreateTempDirectory();
            var path = Path.Combine(directory, "accounts.json");
            var oldPasswordHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes("old-password")));
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new[]
            {
                new UserAccount { UserName = "SuperAdmin", PasswordHash = oldPasswordHash, Role = "Operator" }
            }));

            var manager = new AccountManager(path);

            Assert.True(manager.TryLogin("superadmin", "admin123", out var account));
            Assert.False(manager.TryLogin("superadmin", "old-password", out _));
            Assert.Equal("Admin", account.Role);
            Assert.StartsWith("PBKDF2-SHA256$", account.PasswordHash);
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
            history.Add("Template", "C:\\a.btw", "template-1", new Dictionary<string, string> { ["IMEI"] = "X" }, "FAIL", "Printer", 1);
            Assert.False(history.ContainsAnyValue("Template", "C:\\a.btw", "template-1", "X"));
        }

        [Fact]
        public void UncertainHistoryRecordReservesValueUntilReviewed()
        {
            var dir = CreateTempDirectory();
            var history = new HistoryManager(Path.Combine(dir, "records.csv"), Path.Combine(dir, "records.jsonl"), Path.Combine(dir, "records.db"));

            Assert.True(history.Add("Template", "C:\\a.btw", "template-1", new Dictionary<string, string> { ["IMEI"] = "X" }, "UNCERTAIN", "Printer", 1));

            Assert.True(history.ContainsAnyValue("Template", "C:\\a.btw", "template-1", "X"));
            Assert.Null(history.GetLatestSuccessful("Template", "C:\\a.btw", "template-1"));

            Assert.True(history.Delete(history.Records[0].RecordId, "admin", "reviewed"));
            Assert.False(history.ContainsAnyValue("Template", "C:\\a.btw", "template-1", "X"));
        }

        [Fact]
        public void LatestSuccessfulPreviewRecordSkipsFailures()
        {
            var dir = CreateTempDirectory();
            var history = new HistoryManager(Path.Combine(dir, "records.csv"), Path.Combine(dir, "records.jsonl"), Path.Combine(dir, "records.db"));
            history.Add("Template", "C:\\a.btw", "template-1", new Dictionary<string, string> { ["IMEI"] = "PASS-1" }, "PASS", "P", 1);
            history.Add("Template", "C:\\a.btw", "template-1", new Dictionary<string, string> { ["IMEI"] = "FAIL-2" }, "FAIL", "P", 1);
            history.Add("Template", "C:\\a.btw", "template-1", new Dictionary<string, string> { ["IMEI"] = "PASS-3" }, "REPRINT_PASS", "P", 1);

            var record = history.GetLatestSuccessful("Template", "C:\\a.btw", "template-1");

            Assert.Equal("PASS-3", record.FieldValues["IMEI"]);
        }

        [Fact]
        public void LatestSuccessfulPreviewRecordStaysWithinTemplate()
        {
            var dir = CreateTempDirectory();
            var history = new HistoryManager(Path.Combine(dir, "records.csv"), Path.Combine(dir, "records.jsonl"), Path.Combine(dir, "records.db"));
            history.Add("Template A", "C:\\a.btw", "template-a", new Dictionary<string, string> { ["IMEI"] = "A" }, "PASS", "P", 1);
            history.Add("Template B", "C:\\b.btw", "template-b", new Dictionary<string, string> { ["IMEI"] = "B" }, "PASS", "P", 1);

            var record = history.GetLatestSuccessful("Template A", "C:\\a.btw", "template-a");

            Assert.Equal("A", record.FieldValues["IMEI"]);
        }

        [Fact]
        public void LatestSuccessfulPreviewRecordReturnsNullWithoutSuccess()
        {
            var dir = CreateTempDirectory();
            var history = new HistoryManager(Path.Combine(dir, "records.csv"), Path.Combine(dir, "records.jsonl"), Path.Combine(dir, "records.db"));
            history.Add("Template", "C:\\a.btw", "template-1", new Dictionary<string, string> { ["IMEI"] = "FAIL" }, "FAIL", "P", 1);

            Assert.Null(history.GetLatestSuccessful("Template", "C:\\a.btw", "template-1"));
        }

        [Fact]
        public void HistoryManagerSkipsBadJsonlRows()
        {
            var dir = CreateTempDirectory();
            var csv = Path.Combine(dir, "records.csv");
            var jsonl = Path.Combine(dir, "records.jsonl");
            var db = Path.Combine(dir, "records.db");
            var legacyRecord = new PrintRecord("Template", "C:\\a.btw", "tid", new Dictionary<string, string> { ["A"] = "1" }, "now", "PASS", "P", 1)
            {
                SchemaVersion = 2
            };
            File.WriteAllText(jsonl, "{bad json}\n" + System.Text.Json.JsonSerializer.Serialize(legacyRecord));
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
        public void HistoryManagerRecoversCorruptSqliteRowFromJsonlMirror()
        {
            var dir = CreateTempDirectory();
            var db = Path.Combine(dir, "records.db");
            var history = new HistoryManager(Path.Combine(dir, "records.csv"), Path.Combine(dir, "records.jsonl"), db);
            Assert.True(history.Add("Template", "C:\\a.btw", "template-1", new Dictionary<string, string> { ["IMEI"] = "A" }, "PASS", "Printer", 1));
            Assert.True(history.Add("Template", "C:\\a.btw", "template-1", new Dictionary<string, string> { ["IMEI"] = "B" }, "PASS", "Printer", 1));
            var corruptId = history.Records[0].RecordId;
            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE PrintRecords SET Json = '{' WHERE RecordId = $RecordId";
                command.Parameters.AddWithValue("$RecordId", corruptId);
                command.ExecuteNonQuery();
            }

            var reloaded = new HistoryManager(Path.Combine(dir, "records.csv"), Path.Combine(dir, "records.jsonl"), db);
            reloaded.Load();

            Assert.Equal(2, reloaded.Records.Count);
            Assert.Equal(new[] { "A", "B" }, reloaded.Records.Select(record => record.FieldValues["IMEI"]).OrderBy(value => value));
            var loadedLines = File.ReadAllLines(Path.Combine(dir, "records.jsonl"));
            Assert.Equal(2, loadedLines.Length);

            var rebuilt = new HistoryManager(Path.Combine(dir, "records.csv"), Path.Combine(dir, "records.jsonl"), db);
            rebuilt.Load();
            Assert.Equal(2, rebuilt.Records.Count);
        }

        [Fact]
        public void HistoryManagerRecoversFullyCorruptSqliteFromJsonlMirror()
        {
            var dir = CreateTempDirectory();
            var db = Path.Combine(dir, "records.db");
            var jsonl = Path.Combine(dir, "records.jsonl");
            var history = new HistoryManager(Path.Combine(dir, "records.csv"), jsonl, db);
            Assert.True(history.Add("Template", "C:\\a.btw", "template-1", new Dictionary<string, string> { ["IMEI"] = "A" }, "PASS", "Printer", 1));
            Assert.True(history.Add("Template", "C:\\a.btw", "template-1", new Dictionary<string, string> { ["IMEI"] = "B" }, "PASS", "Printer", 1));
            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE PrintRecords SET Json = '{'";
                command.ExecuteNonQuery();
            }

            var reloaded = new HistoryManager(Path.Combine(dir, "records.csv"), jsonl, db);
            reloaded.Load();

            Assert.Equal(2, reloaded.Records.Count);
            Assert.Equal(new[] { "A", "B" }, reloaded.Records.Select(record => record.FieldValues["IMEI"]).OrderBy(value => value));
        }

        [Fact]
        public void HistoryManagerPreservesJsonlWhenCorruptSqliteRecordCannotBeRecovered()
        {
            var dir = CreateTempDirectory();
            var db = Path.Combine(dir, "records.db");
            var jsonl = Path.Combine(dir, "records.jsonl");
            var history = new HistoryManager(Path.Combine(dir, "records.csv"), jsonl, db);
            Assert.True(history.Add("Template", "C:\\a.btw", "template-1", new Dictionary<string, string> { ["IMEI"] = "A" }, "PASS", "Printer", 1));
            Assert.True(history.Add("Template", "C:\\a.btw", "template-1", new Dictionary<string, string> { ["IMEI"] = "B" }, "PASS", "Printer", 1));
            var corruptId = history.Records[0].RecordId;
            var healthyMirrorLine = File.ReadAllLines(jsonl).Single(line => !line.Contains(corruptId, StringComparison.Ordinal));
            File.WriteAllText(jsonl, healthyMirrorLine + Environment.NewLine);
            var mirrorBeforeLoad = File.ReadAllText(jsonl);
            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE PrintRecords SET Json = '{' WHERE RecordId = $RecordId";
                command.Parameters.AddWithValue("$RecordId", corruptId);
                command.ExecuteNonQuery();
            }

            var reloaded = new HistoryManager(Path.Combine(dir, "records.csv"), jsonl, db);
            reloaded.Load();

            Assert.Single(reloaded.Records);
            Assert.Equal("B", reloaded.Records[0].FieldValues["IMEI"]);
            Assert.Equal(mirrorBeforeLoad, File.ReadAllText(jsonl));
        }

        [Fact]
        public void HistoryManagerKeepsSuccessfulPrimaryWriteWhenArchiveFails()
        {
            var dir = CreateTempDirectory();
            var archivePath = Path.Combine(dir, "archive-file");
            File.WriteAllText(archivePath, "blocks archive directory creation");
            var db = Path.Combine(dir, "records.db");
            var history = new HistoryManager(Path.Combine(dir, "records.csv"), Path.Combine(dir, "records.jsonl"), db, archivePath);

            var added = history.Add("Template", "C:\\a.btw", "template-1", new Dictionary<string, string> { ["IMEI"] = "X" }, "PASS", "Printer", 1);

            Assert.True(added);
            Assert.Single(history.Records);
            var reloaded = new HistoryManager(Path.Combine(dir, "records.csv"), Path.Combine(dir, "records.jsonl"), db, archivePath);
            reloaded.Load();
            Assert.Single(reloaded.Records);
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
        public void BusinessHistoryCsvMapsOrderFieldsAndTemplateSources()
        {
            var order = new PackagingOrder { Id = "order-1", Customer = "客户甲", ProductModel = "M1", Color = "蓝色", OrderNumber = "SO001" };
            var record = new PrintRecord("T", "C:\\a.btw", "template-1", new Dictionary<string, string>
            {
                ["IMEI"] = "123,456",
                ["BOX"] = "A\"B"
            }, "2026-08-14 09:08:07", "PASS", "P", 1)
            {
                OrderId = order.OrderId,
                OperatorName = "operator1"
            };

            var csv = BusinessHistoryCsvExporter.BuildCsv(new[] { record }, order, new[] { "IMEI", "BOX", "MISSING" });
            var rows = csv.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            Assert.Equal(new[] { "日期", "客户", "颜色", "机型", "订单号", "IMEI", "BOX", "MISSING", "操作人", "打印时间", "打印状态" }, CsvUtils.ParseLine(rows[0]));
            Assert.Equal(new[] { "2026-08-14", "客户甲", "蓝色", "M1", "SO001", "123,456", "A\"B", "", "operator1", "2026/08/14 09:08:07", "PASS" }, CsvUtils.ParseLine(rows[1]));
            Assert.Contains("\"123,456\"", rows[1]);
            Assert.Contains("\"A\"\"B\"", rows[1]);
        }

        [Fact]
        public void BusinessHistoryCsvExportSplitsOrdersAndWritesUtf8Bom()
        {
            var directory = Path.Combine(Path.GetTempPath(), "BarTenderPrinterTests", Guid.NewGuid().ToString("N"));
            var orders = new[]
            {
                new PackagingOrder { Id = "order-1", Customer = "客户甲", ProductModel = "M1", Color = "蓝色", OrderNumber = "SO001" },
                new PackagingOrder { Id = "order-2", Customer = "客户乙", ProductModel = "M2", Color = "红色", OrderNumber = "SO002" }
            };
            var records = orders.Select(order => new PrintRecord("T", "C:\\a.btw", "template-1", new Dictionary<string, string> { ["SN"] = order.OrderNumber }, "2026-08-14 10:00:00", "PASS", "P", 1)
            {
                OrderId = order.OrderId
            }).ToList();

            var paths = BusinessHistoryCsvExporter.Export(directory, records, orders, new[] { "SN", "sn" }, new DateTime(2026, 8, 14));

            Assert.Equal(2, paths.Count);
            Assert.Contains(paths, path => Path.GetFileName(path) == "客户甲_M1_蓝色_SO001_20260814.csv");
            Assert.Contains(paths, path => Path.GetFileName(path) == "客户乙_M2_红色_SO002_20260814.csv");
            Assert.All(paths, path => Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, File.ReadAllBytes(path).Take(3).ToArray()));
        }

        [Fact]
        public void BusinessHistoryCsvExportRequiresExplicitOverwrite()
        {
            var directory = Path.Combine(Path.GetTempPath(), "BarTenderPrinterTests", Guid.NewGuid().ToString("N"));
            var order = new PackagingOrder { Id = "order-1", Customer = "客户甲", ProductModel = "M1", Color = "蓝色", OrderNumber = "SO001" };
            var record = new PrintRecord { OrderId = order.OrderId, PrintTime = "2026-08-14 10:00:00" };

            BusinessHistoryCsvExporter.Export(directory, new[] { record }, new[] { order }, Array.Empty<string>(), new DateTime(2026, 8, 14));
            var error = Assert.Throws<IOException>(() => BusinessHistoryCsvExporter.Export(directory, new[] { record }, new[] { order }, Array.Empty<string>(), new DateTime(2026, 8, 14)));
            Assert.StartsWith("导出文件已存在：", error.Message);
            Assert.Single(BusinessHistoryCsvExporter.Export(directory, new[] { record }, new[] { order }, Array.Empty<string>(), new DateTime(2026, 8, 14), true));
        }

        [Fact]
        public void BusinessHistoryCsvExportMergesRecordsWithoutOrderSnapshot()
        {
            var directory = Path.Combine(Path.GetTempPath(), "BarTenderPrinterTests", Guid.NewGuid().ToString("N"));
            var records = new[]
            {
                new PrintRecord { OrderId = "missing-1", PrintTime = "2026-08-14 10:00:00" },
                new PrintRecord { OrderId = "missing-2", PrintTime = "2026-08-14 11:00:00" }
            };

            var paths = BusinessHistoryCsvExporter.Export(directory, records, Array.Empty<PackagingOrder>(), Array.Empty<string>(), new DateTime(2026, 8, 14));
            var rows = File.ReadAllLines(Assert.Single(paths), Encoding.UTF8);

            Assert.Equal(3, rows.Length);
            Assert.Equal("____20260814.csv", Path.GetFileName(paths[0]));
        }

        [Fact]
        public void HistoryPresenterBuildsRowsWithoutNullFailures()
        {
            var table = HistoryPresenter.BuildTable(new[] { new PrintRecord("T", "C:\\a.btw", "tid", new Dictionary<string, string> { ["A"] = "1" }, "now", "PASS", "P", 1) });
            Assert.Single(table.Rows.Cast<System.Data.DataRow>());
        }

        [Theory]
        [InlineData(true, "", PrintJobKind.Print, "PASS")]
        [InlineData(false, "submission=uncertain", PrintJobKind.Print, "UNCERTAIN")]
        [InlineData(false, "error", PrintJobKind.Print, "FAIL")]
        [InlineData(true, "", PrintJobKind.Reprint, "REPRINT_PASS")]
        [InlineData(false, "submission=uncertain", PrintJobKind.Reprint, "REPRINT_UNCERTAIN")]
        [InlineData(false, "error", PrintJobKind.Reprint, "REPRINT_FAIL")]
        public void PrintWorkflowMapsHistoryStatus(bool success, string diagnostics, PrintJobKind kind, string expected)
        {
            var result = new PrintResult(success, "message", diagnostics);
            Assert.Equal(expected, new PrintWorkflow().GetHistoryStatus(result, kind));
        }

        [Theory]
        [InlineData(false, true, "submission=uncertain", PrintJobKind.Print, "打印结果待核查")]
        [InlineData(false, false, "submission=uncertain", PrintJobKind.Reprint, "补打印结果待核查，历史保存失败")]
        [InlineData(true, false, "", PrintJobKind.Print, "打印作业已提交，历史保存失败")]
        public void PrintWorkflowBuildsCompletionStatus(bool success, bool historySaved, string diagnostics, PrintJobKind kind, string expected)
        {
            var result = new PrintResult(success, "message", diagnostics);
            Assert.Equal(expected, new PrintWorkflow().GetCompletionStatus(result, historySaved, kind));
        }

        [Theory]
        [InlineData(true, PrintSubmissionState.Submitted)]
        [InlineData(false, PrintSubmissionState.Failed)]
        [InlineData(0, PrintSubmissionState.Submitted)]
        [InlineData(1, PrintSubmissionState.Uncertain)]
        [InlineData("unknown", PrintSubmissionState.Uncertain)]
        public void PrintOutResultUsesSuccessWhitelist(object result, PrintSubmissionState expected)
        {
            Assert.Equal(expected, BarTenderService.ClassifyPrintOutResult(result));
        }

        [Fact]
        public void NullPrintOutResultIsUncertain()
        {
            Assert.Equal(PrintSubmissionState.Uncertain, BarTenderService.ClassifyPrintOutResult(null));
        }

        [Fact]
        public void ExplicitPrintStateDoesNotDependOnDiagnosticsText()
        {
            var result = new PrintResult(PrintSubmissionState.Uncertain, "unknown", "diagnostics changed");

            Assert.False(result.Success);
            Assert.Equal(PrintSubmissionState.Uncertain, new PrintWorkflow().Classify(result));
        }

        [Fact]
        public async System.Threading.Tasks.Task PrintCoordinatorPersistsMappedReprintResult()
        {
            var history = new FakeHistoryRepository();
            var coordinator = new PrintJobCoordinator(
                new FakeBarTenderService(new PrintResult(PrintSubmissionState.Uncertain, "unknown")),
                history, new PrintWorkflow());

            var completion = await coordinator.ExecuteAsync(CreatePrintJobRequest(PrintJobKind.Reprint));

            Assert.Equal(PrintSubmissionState.Uncertain, completion.PrintResult.State);
            Assert.Equal("REPRINT_UNCERTAIN", completion.HistoryStatus);
            Assert.True(completion.HistorySaved);
            Assert.Equal("REPRINT_UNCERTAIN", history.LastStatus);
            Assert.Equal("补打印结果待核查", completion.CompletionStatus);
        }

        [Fact]
        public async System.Threading.Tasks.Task PrintCoordinatorConvertsPrintExceptionAndStillRecordsHistory()
        {
            var history = new FakeHistoryRepository();
            var coordinator = new PrintJobCoordinator(new FakeBarTenderService(new InvalidOperationException("offline")), history, new PrintWorkflow());

            var completion = await coordinator.ExecuteAsync(CreatePrintJobRequest(PrintJobKind.Print));

            Assert.Equal(PrintSubmissionState.Failed, completion.PrintResult.State);
            Assert.Equal("FAIL", completion.HistoryStatus);
            Assert.True(completion.HistorySaved);
            Assert.Equal("FAIL", history.LastStatus);
        }

        [Fact]
        public async System.Threading.Tasks.Task PrintCoordinatorReportsHistoryExceptionWithoutLosingPrintResult()
        {
            var coordinator = new PrintJobCoordinator(
                new FakeBarTenderService(new PrintResult(PrintSubmissionState.Submitted, "")),
                new FakeHistoryRepository { AddException = new IOException("disk full") },
                new PrintWorkflow());

            var completion = await coordinator.ExecuteAsync(CreatePrintJobRequest(PrintJobKind.Print));

            Assert.True(completion.PrintResult.Success);
            Assert.False(completion.HistorySaved);
            Assert.Equal("disk full", completion.HistoryError);
            Assert.Equal("打印作业已提交，历史保存失败", completion.CompletionStatus);
        }

        [Fact]
        public async System.Threading.Tasks.Task PrintCoordinatorUsesRequestSnapshotAcrossAsyncBoundary()
        {
            var service = new PausingBarTenderService();
            var history = new FakeHistoryRepository();
            var coordinator = new PrintJobCoordinator(service, history, new PrintWorkflow());
            var request = CreatePrintJobRequest(PrintJobKind.Print);
            request.TemplateFields.Add("IMEI");

            var execution = coordinator.ExecuteAsync(request);
            await service.PrintStarted;
            request.FieldValues["IMEI"] = "changed";
            request.TemplateFields[0] = "changed";
            service.CompletePrint();
            var completion = await execution;

            Assert.True(completion.HistorySaved);
            Assert.Equal("123", service.CapturedFieldValues["IMEI"]);
            Assert.Equal("123", history.LastEntry.FieldValues["IMEI"]);
            Assert.Equal("IMEI", history.LastEntry.TemplateFields[0]);
        }

        [Fact]
        public async System.Threading.Tasks.Task HistoryManagerSerializesConcurrentAdds()
        {
            var dir = CreateTempDirectory();
            var db = Path.Combine(dir, "records.db");
            var history = new HistoryManager(Path.Combine(dir, "records.csv"), Path.Combine(dir, "records.jsonl"), db);
            var additions = Enumerable.Range(0, 12).Select(index => System.Threading.Tasks.Task.Run(() =>
                history.Add("Template", "C:\\a.btw", "template-1", new Dictionary<string, string> { ["IMEI"] = $"VALUE-{index}" }, "PASS", "Printer", 1)));

            var results = await System.Threading.Tasks.Task.WhenAll(additions);

            Assert.All(results, Assert.True);
            Assert.Equal(12, history.Records.Count);
            var reloaded = new HistoryManager(Path.Combine(dir, "records.csv"), Path.Combine(dir, "records.jsonl"), db);
            reloaded.Load();
            Assert.Equal(12, reloaded.Records.Count);
            Assert.True(reloaded.ContainsAnyValue("Template", "C:\\a.btw", "template-1", "VALUE-11"));
        }

        [Fact]
        public void TemplateSessionRoundTripPreservesIdentityAndClonesSources()
        {
            var settings = new TemplateSettings
            {
                Scope = "OrderTemplate",
                OrderId = "order-1",
                TemplateId = "template-1",
                TemplateName = "label.btw",
                TemplatePath = "C:\\label.btw",
                TemplateFields = new List<string> { "IMEI" },
                DataSources = new List<DataSourceItem> { new DataSourceItem { Field = "IMEI", LockedValue = "123" } }
            };

            var clone = TemplateSessionState.FromSettings(settings).ToSettings();
            clone.DataSources[0].LockedValue = "456";

            Assert.Equal("OrderTemplate", clone.Scope);
            Assert.Equal("order-1", clone.OrderId);
            Assert.Equal("template-1", clone.TemplateId);
            Assert.Equal("123", settings.DataSources[0].LockedValue);
        }

        [Fact]
        public void DataSourceCloneCopiesValuesWithoutSharingInstance()
        {
            var source = new DataSourceItem
            {
                Name = "IMEI",
                Field = "IMEI1",
                Enabled = true,
                AutoIncrement = true,
                AutoStep = -1,
                IsLocked = true,
                LockAfterInput = true,
                LockedValue = "123",
                AutoIncrementLocked = true,
                ExpectedLength = 15,
                LengthRevision = 4,
                LengthEdited = true,
                UseLocalDataValidation = true
            };

            var clone = source.Clone();
            clone.LockedValue = "456";

            Assert.NotSame(source, clone);
            Assert.Equal("IMEI1", clone.Field);
            Assert.Equal(-1, clone.AutoStep);
            Assert.Equal(15, clone.ExpectedLength);
            Assert.True(clone.UseLocalDataValidation);
            Assert.Equal("123", source.LockedValue);
        }

        [Fact]
        public void NaturalStringComparerOrdersNumericSegmentsByValue()
        {
            var values = new List<string> { "Label10", "Label2", "Label1" };

            values.Sort(NaturalStringComparer.Instance);

            Assert.Equal(new[] { "Label1", "Label2", "Label10" }, values);
        }

        [Fact]
        public void ReprintUncertainHistoryReservesValueUntilReviewed()
        {
            var dir = CreateTempDirectory();
            var history = new HistoryManager(Path.Combine(dir, "records.csv"), Path.Combine(dir, "records.jsonl"), Path.Combine(dir, "records.db"));
            Assert.True(history.Add("Template", "C:\\a.btw", "template-1", new Dictionary<string, string> { ["IMEI"] = "X" }, "REPRINT_UNCERTAIN", "Printer", 1));

            Assert.True(history.ContainsAnyValue("Template", "C:\\a.btw", "template-1", "X"));
        }

        [Fact]
        public void HistoryStatisticsIgnoreRecordsWithoutPrintTime()
        {
            var dir = CreateTempDirectory();
            var jsonl = Path.Combine(dir, "records.jsonl");
            File.WriteAllText(jsonl, System.Text.Json.JsonSerializer.Serialize(new PrintRecord
            {
                SchemaVersion = 2,
                PrintTime = null,
                Status = "PASS",
                RecordChecksum = ""
            }) + Environment.NewLine);
            var history = new HistoryManager(Path.Combine(dir, "records.csv"), jsonl, Path.Combine(dir, "records.db"));
            history.Load();

            Assert.Equal(0, history.TodayCount());
            Assert.Equal(0, history.TodayCount("", "", ""));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void MissingHistoryStatusIsUncertain(string status)
        {
            var record = new PrintRecord("Template", "C:\\a.btw", new Dictionary<string, string>(), "now", status, "Printer", 1);

            Assert.Equal("UNCERTAIN", record.Status);
        }

        [Fact]
        public void TemplateSettingsManagerSkipsNullEntriesAndKeepsValidSettings()
        {
            var path = Path.Combine(CreateTempDirectory(), "template-settings.json");
            File.WriteAllText(path, "[null,{\"TemplateName\":\"label.btw\",\"TemplatePath\":\"C:\\\\label.btw\"}]");

            var manager = new TemplateSettingsManager(path);

            Assert.True(manager.TryGet("label.btw", "C:\\label.btw", out var settings));
            Assert.Equal("label.btw", settings.TemplateName);
        }

        [Fact]
        public void LayoutPolicyKeepsRestoredWindowVisible()
        {
            var constrained = UiLayoutPolicy.ConstrainToWorkingArea(
                new System.Drawing.Rectangle(4000, -200, 1200, 900),
                new System.Drawing.Rectangle(0, 0, 1024, 768));

            Assert.Equal(new System.Drawing.Rectangle(0, 0, 1024, 768), constrained);
        }

        [Fact]
        public void LayoutPolicyPrioritizesMainWindowWhenTilingPreview()
        {
            var widths = UiLayoutPolicy.CalculateTileWidths(1000, 500, 680, 220);

            Assert.Equal(680, widths.MainWidth);
            Assert.Equal(320, widths.PreviewWidth);
        }

        [Theory]
        [InlineData(300, 100, 40, 220, 100)]
        [InlineData(300, 500, 40, 220, 220)]
        [InlineData(20, 20, 40, 220, 20)]
        public void InputPanelHeightStaysWithinAvailableSpace(int required, int available, int minimum, int maximum, int expected)
        {
            Assert.Equal(expected, UiLayoutPolicy.CalculateInputPanelHeight(required, available, minimum, maximum));
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
        public void PreviewCacheKeyChangesWithTemplateContentOrFields()
        {
            var template = CreateTemplateFile();
            var original = BarTenderService.BuildPreviewCacheKey(template, new Dictionary<string, string> { ["IMEI"] = "123" });
            var timestamp = File.GetLastWriteTimeUtc(template);
            File.WriteAllText(template, "changed template content");
            File.SetLastWriteTimeUtc(template, timestamp);
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

        private static PrintJobRequest CreatePrintJobRequest(PrintJobKind kind)
        {
            return new PrintJobRequest
            {
                Kind = kind,
                TemplateName = "label.btw",
                TemplatePath = "C:\\label.btw",
                TemplateId = "template-1",
                FieldValues = new Dictionary<string, string> { ["IMEI"] = "123" },
                Printer = "Printer",
                Copies = 1
            };
        }

        private sealed class FakeBarTenderService : IBarTenderService
        {
            private readonly PrintResult _result;
            private readonly Exception _exception;

            public FakeBarTenderService(PrintResult result) => _result = result;
            public FakeBarTenderService(Exception exception) => _exception = exception;

            public bool IsConnected => true;
            public bool IsOfflineMode => false;
            public bool IsPreviewAvailable => false;
            public string PreviewUnavailableReason => "";
            public bool Connect() => true;
            public List<string> GetTemplateDataSources(string templatePath) => new List<string>();
            public void RunDiagnostics(string templatePath) { }
            public PrintResult Print(string templatePath, Dictionary<string, string> fieldValues, string printer, int copies) => _result;
            public System.Threading.Tasks.Task<PrintResult> PrintAsync(string templatePath, Dictionary<string, string> fieldValues, string printer, int copies)
            {
                return _exception == null
                    ? System.Threading.Tasks.Task.FromResult(_result)
                    : System.Threading.Tasks.Task.FromException<PrintResult>(_exception);
            }
            public System.Threading.Tasks.Task<string> ExportPreviewAsync(string templatePath, Dictionary<string, string> fieldValues) => System.Threading.Tasks.Task.FromResult("");
            public string[] GetAvailableTemplates(string directory) => Array.Empty<string>();
            public string[] GetPrinters() => Array.Empty<string>();
            public void Disconnect() { }
            public void Dispose() { }
        }

        private sealed class PausingBarTenderService : IBarTenderService
        {
            private readonly System.Threading.Tasks.TaskCompletionSource<bool> _printStarted = new System.Threading.Tasks.TaskCompletionSource<bool>(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly System.Threading.Tasks.TaskCompletionSource<PrintResult> _printCompletion = new System.Threading.Tasks.TaskCompletionSource<PrintResult>(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);

            public System.Threading.Tasks.Task PrintStarted => _printStarted.Task;
            public Dictionary<string, string> CapturedFieldValues { get; private set; }
            public bool IsConnected => true;
            public bool IsOfflineMode => false;
            public bool IsPreviewAvailable => false;
            public string PreviewUnavailableReason => "";
            public bool Connect() => true;
            public List<string> GetTemplateDataSources(string templatePath) => new List<string>();
            public void RunDiagnostics(string templatePath) { }
            public PrintResult Print(string templatePath, Dictionary<string, string> fieldValues, string printer, int copies) => throw new NotSupportedException();
            public System.Threading.Tasks.Task<PrintResult> PrintAsync(string templatePath, Dictionary<string, string> fieldValues, string printer, int copies)
            {
                CapturedFieldValues = fieldValues;
                _printStarted.SetResult(true);
                return _printCompletion.Task;
            }
            public void CompletePrint() => _printCompletion.SetResult(new PrintResult(PrintSubmissionState.Submitted, ""));
            public System.Threading.Tasks.Task<string> ExportPreviewAsync(string templatePath, Dictionary<string, string> fieldValues) => System.Threading.Tasks.Task.FromResult("");
            public string[] GetAvailableTemplates(string directory) => Array.Empty<string>();
            public string[] GetPrinters() => Array.Empty<string>();
            public void Disconnect() { }
            public void Dispose() { }
        }

        private sealed class FakeHistoryRepository : IHistoryRepository
        {
            public IReadOnlyList<PrintRecord> Records { get; } = new List<PrintRecord>();
            public Exception AddException { get; set; }
            public string LastStatus { get; private set; } = "";
            public PrintHistoryEntry LastEntry { get; private set; }

            public void Load() { }
            public bool Add(PrintHistoryEntry entry)
            {
                if (AddException != null) throw AddException;
                LastEntry = entry;
                LastStatus = entry.Status;
                return true;
            }
            public bool Clear(string templateName, string templatePath, string templateId, string operatorName = "", string reason = "") => true;
            public bool Delete(string recordId, string operatorName = "", string reason = "") => true;
            public PrintRecord GetById(string recordId) => null;
            public PrintRecord GetLatestSuccessful(string templateName, string templatePath, string templateId) => null;
            public List<PrintRecord> Search(string templateName, string templatePath, string templateId, string keyword, bool exact, int limit = 0, bool newestFirst = false, int offset = 0) => new List<PrintRecord>();
            public List<PrintRecord> Search(string templateName, string templatePath, string templateId, string keyword, bool exact, int limit, bool newestFirst, int offset, string status, string datePrefix, string printer, string orderQuery) => new List<PrintRecord>();
            public int Count(string templateName, string templatePath, string templateId) => 0;
            public int TodayCount(string templateName, string templatePath, string templateId) => 0;
            public bool ContainsAnyValue(string templateName, string templatePath, string templateId, string value) => false;
            public void Export(string path, IEnumerable<PrintRecord> records) { }
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
