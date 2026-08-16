using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;

namespace BarTenderPrinter
{
    internal sealed partial class MesWorkstationPanel
    {
        private TabPage BuildOrderGroup() => CreateGroup("订单", BuildOrderPage(), BuildOrderTransitionPage());

        private TabPage BuildMasterDataGroup() => CreateGroup("主数据",
            BuildProductionUnitPage(), BuildRoutePage(), BuildStationMasterPage(), BuildPackagingMasterPage());

        private TabPage BuildWorkstationGroup() => CreateGroup("生产工位",
            BuildStationPage(), BuildPackagingPage(), BuildNumberPage(), BuildWeightRulePage(), BuildWeightPage(), BuildIdentifierWritePage());

        private TabPage BuildPrintTraceRecoveryGroup() => CreateGroup("作业与恢复",
            BuildPrintPage(), BuildTraceabilityPage(), BuildRecoveryPage());

        private TabPage BuildOrderTransitionPage()
        {
            var page = CreatePage("状态转换");
            var orderId = AddTextRow(page, "订单 ID");
            var target = AddComboRow(page, "目标状态", "Published", "InProduction", "Paused", "Closed");
            var version = AddTextRow(page, "预期版本", "0");
            var key = AddIdempotencyRow(page);
            var output = AddOutput(page);
            AddAction(page, "转换订单", async token =>
            {
                var result = await _service.TransitionOrderAsync(orderId.Text, new MesOrderTransitionRequest
                {
                    TargetStatus = target.Text,
                    ExpectedVersion = ParseLong(version.Text),
                    IdempotencyKey = key.Text
                }, token);
                ShowResult(output, result, Pretty);
                RenewKeyOnSuccess(result, key);
            });
            return page;
        }

        private TabPage BuildProductionUnitPage()
        {
            var page = CreatePage("生产单元");
            var orderId = AddTextRow(page, "订单 ID");
            var allocations = AddTextRow(page, "号码分配", "SerialNumber=allocation-id");
            var key = AddIdempotencyRow(page);
            var output = AddOutput(page);
            AddAction(page, "创建生产单元", async token =>
            {
                var result = await _service.CreateProductionUnitAsync(new MesProductionUnitRequest
                {
                    OrderId = orderId.Text,
                    AllocationIds = ParseMap(allocations.Text),
                    IdempotencyKey = key.Text
                }, token);
                ShowResult(output, result, Pretty);
                RenewKeyOnSuccess(result, key);
            });
            return page;
        }

        private TabPage BuildRoutePage()
        {
            var page = CreatePage("工艺路线");
            var orderId = AddTextRow(page, "订单 ID");
            var name = AddTextRow(page, "路线名称");
            var type = AddComboRow(page, "路线类型", "Standard", "Rework");
            var operations = AddTextRow(page, "工序", "OP10|组装|10,OP20|包装|20");
            var key = AddIdempotencyRow(page);
            var output = AddOutput(page);
            AddAction(page, "创建路线", async token =>
            {
                var result = await _service.CreateRouteAsync(new MesRouteRequest
                {
                    OrderId = orderId.Text,
                    Name = name.Text,
                    RouteType = type.Text,
                    Operations = ParseOperations(operations.Text),
                    IdempotencyKey = key.Text
                }, token);
                ShowResult(output, result, Pretty);
                RenewKeyOnSuccess(result, key);
            });
            return page;
        }

        private TabPage BuildStationMasterPage()
        {
            var page = CreatePage("工位定义");
            var name = AddTextRow(page, "工位名称");
            var operations = AddTextRow(page, "合格工序 ID", "OP10,OP20");
            var key = AddIdempotencyRow(page);
            var output = AddOutput(page);
            AddAction(page, "创建工位", async token =>
            {
                var result = await _service.CreateStationAsync(new MesStationRequest
                {
                    Name = name.Text,
                    QualifiedOperationIds = ParseList(operations.Text),
                    IdempotencyKey = key.Text
                }, token);
                ShowResult(output, result, Pretty);
                RenewKeyOnSuccess(result, key);
            });
            return page;
        }

        private TabPage BuildPackagingMasterPage()
        {
            var page = CreatePage("包装单元");
            var orderId = AddTextRow(page, "订单 ID");
            var unitType = AddComboRow(page, "包装类型", "Body", "ColorBox", "Carton", "Pallet");
            var code = AddTextRow(page, "包装编码");
            var model = AddTextRow(page, "产品型号");
            var color = AddTextRow(page, "颜色");
            var capacity = AddTextRow(page, "容量", "1");
            var productionUnit = AddTextRow(page, "生产单元 ID（可空）");
            var key = AddIdempotencyRow(page);
            var output = AddOutput(page);
            AddAction(page, "创建包装单元", async token =>
            {
                var result = await _service.CreatePackagingUnitAsync(new MesPackagingUnitRequest
                {
                    OrderId = orderId.Text, UnitType = unitType.Text, Code = code.Text,
                    ProductModel = model.Text, Color = color.Text, Capacity = ParseInt(capacity.Text),
                    ProductionUnitId = EmptyToNull(productionUnit.Text), IdempotencyKey = key.Text
                }, token);
                ShowResult(output, result, Pretty);
                RenewKeyOnSuccess(result, key);
            });
            return page;
        }

        private TabPage BuildNumberPage()
        {
            var page = CreatePage("号码状态");
            var allocationId = AddTextRow(page, "号码分配 ID");
            var target = AddComboRow(page, "目标状态", "Frozen", "Released", "Scrapped");
            var reason = AddTextRow(page, "原因码");
            var key = AddIdempotencyRow(page);
            var output = AddOutput(page);
            AddAction(page, "更新状态", async token =>
            {
                var result = await _service.ChangeNumberStatusAsync(allocationId.Text, new MesNumberStatusRequest
                {
                    TargetStatus = target.Text, ReasonCode = reason.Text, IdempotencyKey = key.Text
                }, token);
                ShowResult(output, result, Pretty);
                RenewKeyOnSuccess(result, key);
            });
            AddSecondaryAction(page, "查询历史", async token =>
                ShowResult(output, await _service.GetNumberHistoryAsync(allocationId.Text, token), Pretty));
            return page;
        }

        private TabPage BuildWeightPage()
        {
            var page = CreatePage("称重");
            AddSimulationBanner(page, "模拟电子称：当前客户端手工录入模拟读数并显式上报 IsSimulated=true。 ");
            var packageId = AddTextRow(page, "包装单元 ID");
            var weight = AddTextRow(page, "重量", "12.500");
            var unit = AddTextRow(page, "单位", "kg");
            var device = AddTextRow(page, "设备 ID", "simulated-scale");
            var key = AddIdempotencyRow(page);
            var output = AddOutput(page);
            AddAction(page, "提交模拟称重", async token =>
            {
                var result = await _service.RecordWeightAsync(packageId.Text, new MesWeightRequest
                {
                    Weight = ParseDecimal(weight.Text), Unit = unit.Text, DeviceId = device.Text,
                    IsSimulated = true, IdempotencyKey = key.Text
                }, token);
                ShowResult(output, result, Pretty);
                RenewKeyOnSuccess(result, key);
            });
            return page;
        }

        private TabPage BuildWeightRulePage()
        {
            var page = CreatePage("称重规则");
            var orderId = AddTextRow(page, "订单 ID");
            var packageType = AddComboRow(page, "包装类型", "ColorBox", "Carton", "Pallet");
            var minimum = AddTextRow(page, "最小重量", "10.000");
            var maximum = AddTextRow(page, "最大重量", "20.000");
            var unit = AddTextRow(page, "单位", "kg");
            var key = AddIdempotencyRow(page);
            var output = AddOutput(page);
            AddAction(page, "创建称重规则", async token =>
            {
                var result = await _service.CreateWeightRuleAsync(new MesWeightRuleRequest
                {
                    OrderId = orderId.Text, PackagingUnitType = packageType.Text,
                    MinimumWeight = ParseDecimal(minimum.Text), MaximumWeight = ParseDecimal(maximum.Text),
                    Unit = unit.Text, IdempotencyKey = key.Text
                }, token);
                ShowResult(output, result, Pretty);
                RenewKeyOnSuccess(result, key);
            });
            return page;
        }

        private TabPage BuildIdentifierWritePage()
        {
            var page = CreatePage("写号");
            AddSimulationBanner(page, "模拟写号工具：仅提交模拟任务和结果，未接入加密授权或真实设备工具。 ");
            var unitId = AddTextRow(page, "生产单元 ID");
            var allocations = AddTextRow(page, "号码分配 ID", "allocation-1,allocation-2");
            var targetStation = AddTextRow(page, "目标工位 ID");
            var platform = AddTextRow(page, "平台", "android-simulator");
            var taskId = AddTextRow(page, "任务 ID");
            var state = AddComboRow(page, "结果状态", "Succeeded", "Failed", "Uncertain");
            var diagnostic = AddTextRow(page, "诊断码", "SIMULATED");
            var resultJson = AddTextRow(page, "回读结果 JSON", "{}");
            var key = AddIdempotencyRow(page);
            var output = AddOutput(page);
            AddAction(page, "创建任务", async token =>
            {
                var result = await _service.CreateIdentifierWriteTaskAsync(new MesIdentifierWriteTaskRequest
                {
                    UnitId = unitId.Text, AllocationIds = ParseList(allocations.Text), Platform = platform.Text,
                    TargetStationId = targetStation.Text, IdempotencyKey = key.Text
                }, token);
                ShowResult(output, result, Pretty);
                RenewKeyOnSuccess(result, key);
            });
            AddSecondaryAction(page, "领取任务", async token =>
            {
                var result = await _service.ClaimIdentifierWriteTaskAsync(platform.Text, key.Text, token);
                ShowResult(output, result, Pretty);
                RenewKeyOnSuccess(result, key);
            });
            AddSecondaryAction(page, "提交模拟结果", async token =>
            {
                var result = await _service.RecordIdentifierWriteResultAsync(taskId.Text,
                    new MesIdentifierWriteResultRequest
                    {
                        State = state.Text, DiagnosticCode = diagnostic.Text,
                        Result = ParseJson(resultJson.Text), IdempotencyKey = key.Text
                    }, token);
                ShowResult(output, result, Pretty);
                RenewKeyOnSuccess(result, key);
            });
            return page;
        }

        private static TabPage CreateGroup(string title, params TabPage[] pages)
        {
            var group = new TabPage(title) { BackColor = MiuiTheme.CardBackground };
            var tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.AddRange(pages);
            group.Controls.Add(tabs);
            MiuiTheme.StyleTabControl(tabs, tabs.DeviceDpi);
            return group;
        }

        private static ComboBox AddComboRow(TabPage page, string caption, params string[] values)
        {
            var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            combo.Items.AddRange(values.Cast<object>().ToArray());
            if (combo.Items.Count > 0) combo.SelectedIndex = 0;
            AddControlRow(page, caption, combo);
            MiuiTheme.StyleComboBox(combo);
            return combo;
        }

        private static TextBox AddIdempotencyRow(TabPage page) =>
            AddTextRow(page, "幂等键", Guid.NewGuid().ToString("N"));

        private void AddSecondaryAction(TabPage page, string caption, Func<CancellationToken, System.Threading.Tasks.Task> action)
        {
            var button = new Button
            {
                Text = caption, AutoSize = true, MinimumSize = new System.Drawing.Size(120, 32)
            };
            button.Click += async (_, _) => await RunUiActionAsync(button, action);
            GetActionPanel(page).Controls.Add(button);
            MiuiTheme.StyleButton(button);
        }

        private static void AddSimulationBanner(TabPage page, string text)
        {
            var label = new Label
            {
                Text = text, AutoSize = true, MaximumSize = new System.Drawing.Size(760, 0),
                Left = 18, Top = NextRow(page), Padding = new Padding(10, 7, 10, 7),
                BackColor = MiuiTheme.WarningLight, ForeColor = MiuiTheme.WarningText, Tag = "row"
            };
            page.Controls.Add(label);
        }

        private static string Pretty(JsonElement value) => JsonSerializer.Serialize(value, PrettyJson);
        private static IReadOnlyList<string> ParseList(string value) => (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim()).Where(item => item.Length > 0).ToArray();
        private static IReadOnlyDictionary<string, string> ParseMap(string value) => ParseList(value)
            .Select(item => item.Split('=', 2)).Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);
        private static IReadOnlyList<MesRouteOperationRequest> ParseOperations(string value) => ParseList(value)
            .Select(item => item.Split('|')).Where(parts => parts.Length == 3)
            .Select(parts => new MesRouteOperationRequest { Id = parts[0].Trim(), Name = parts[1].Trim(), Sequence = ParseInt(parts[2]) })
            .ToArray();
        private static int ParseInt(string value) => int.TryParse(value, out var parsed) ? parsed : 0;
        private static long ParseLong(string value) => long.TryParse(value, out var parsed) ? parsed : 0;
        private static decimal ParseDecimal(string value) => decimal.TryParse(value, NumberStyles.Number,
            CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
        private static string EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        private static JsonElement ParseJson(string value)
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(value) ? "{}" : value);
            return document.RootElement.Clone();
        }
        private static void RenewKeyOnSuccess<T>(MesResult<T> result, TextBox key)
        {
            if (result.IsSuccess) key.Text = Guid.NewGuid().ToString("N");
        }
    }
}
