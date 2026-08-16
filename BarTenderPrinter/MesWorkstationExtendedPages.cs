using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BarTenderPrinter
{
    internal sealed partial class MesWorkstationPanel
    {
        private TabPage BuildQualityReworkGroup() => CreateGroup("质量返工",
            BuildInspectionLotPage(), BuildInspectionResultPage(), BuildReworkPage());

        private TabPage BuildShippingArchiveGroup() => CreateGroup("出库归档",
            BuildShipmentPage(), BuildArchivePage());

        private TabPage BuildDataExchangeGroup() => CreateGroup("导入导出",
            BuildImportPage(), BuildExportPage());

        private TabPage BuildInspectionLotPage()
        {
            var page = CreatePage("检验批");
            var orderId = AddTextRow(page, "订单 ID");
            var type = AddComboRow(page, "检验类型", "QA", "OQC", "Patrol", "Special");
            var rule = AddTextRow(page, "抽样规则", "全检");
            var samples = AddTextRow(page, "样本单元 ID", "unit-1,unit-2");
            var lotId = AddTextRow(page, "检验批 ID");
            var version = AddTextRow(page, "预期版本", "0");
            var key = AddIdempotencyRow(page);
            var output = AddOutput(page);
            AddAction(page, "创建检验批", async token =>
            {
                var result = await _service.CreateInspectionLotAsync(new MesInspectionLotRequest
                {
                    OrderId = orderId.Text, InspectionType = type.Text,
                    SampleRule = rule.Text, SampleUnitIds = ParseList(samples.Text), IdempotencyKey = key.Text
                }, token);
                ShowResult(output, result, Pretty);
                RenewKeyOnSuccess(result, key);
            });
            AddSecondaryAction(page, "完成检验批", async token =>
            {
                var result = await _service.CompleteInspectionLotAsync(lotId.Text, new MesInspectionCompleteRequest
                {
                    ExpectedVersion = ParseLong(version.Text), IdempotencyKey = key.Text
                }, token);
                ShowResult(output, result, Pretty);
                RenewKeyOnSuccess(result, key);
            });
            return page;
        }

        private TabPage BuildInspectionResultPage()
        {
            var page = CreatePage("检验与处置");
            var lotId = AddTextRow(page, "检验批 ID");
            var unitId = AddTextRow(page, "生产单元 ID");
            var itemCode = AddTextRow(page, "检验项编码");
            var outcome = AddComboRow(page, "检验结果", "Passed", "Failed");
            var defect = AddTextRow(page, "缺陷码");
            var responsible = AddTextRow(page, "责任工序 ID");
            var remarks = AddTextRow(page, "备注");
            var decision = AddComboRow(page, "处置决定", "Release", "Rework", "Scrap");
            var reason = AddTextRow(page, "处置原因码");
            var key = AddIdempotencyRow(page);
            var output = AddOutput(page);
            AddAction(page, "提交检验结果", async token =>
            {
                var result = await _service.AddInspectionResultAsync(lotId.Text, new MesInspectionResultRequest
                {
                    UnitId = unitId.Text, ItemCode = itemCode.Text, Outcome = outcome.Text,
                    DefectCode = defect.Text, ResponsibleOperationId = responsible.Text,
                    Remarks = remarks.Text, IdempotencyKey = key.Text
                }, token);
                ShowResult(output, result, Pretty);
                RenewKeyOnSuccess(result, key);
            });
            AddSecondaryAction(page, "应用处置", async token =>
            {
                var result = await _service.ApplyInspectionDispositionAsync(lotId.Text,
                    new MesInspectionDispositionRequest
                    {
                        Decision = decision.Text, ReasonCode = reason.Text, IdempotencyKey = key.Text
                    }, token);
                ShowResult(output, result, Pretty);
                RenewKeyOnSuccess(result, key);
            });
            AddSecondaryAction(page, "查询处置任务", async token =>
                ShowResult(output, await _service.GetQualityDispositionTasksAsync("Open", token), Pretty));
            return page;
        }

        private TabPage BuildReworkPage()
        {
            var page = CreatePage("返工");
            var unitId = AddTextRow(page, "生产单元 ID");
            var routeId = AddTextRow(page, "返工路线 ID");
            var reason = AddTextRow(page, "原因码");
            var operation = AddTextRow(page, "起始工序 ID");
            var sequence = AddTextRow(page, "返工序号", "1");
            var reworkId = AddTextRow(page, "返工单 ID");
            var command = AddComboRow(page, "状态命令", "approve", "activate", "complete");
            var key = AddIdempotencyRow(page);
            var output = AddOutput(page);
            AddAction(page, "创建返工单", async token =>
            {
                var result = await _service.CreateReworkOrderAsync(new MesReworkOrderRequest
                {
                    ProductionUnitId = unitId.Text, RouteId = routeId.Text, ReasonCode = reason.Text,
                    StartOperationId = operation.Text, Sequence = ParseInt(sequence.Text), IdempotencyKey = key.Text
                }, token);
                ShowResult(output, result, Pretty);
                RenewKeyOnSuccess(result, key);
            });
            AddSecondaryAction(page, "执行状态命令", async token =>
            {
                var result = await _service.ChangeReworkStateAsync(reworkId.Text, command.Text, key.Text, token);
                ShowResult(output, result, Pretty);
                RenewKeyOnSuccess(result, key);
            });
            return page;
        }

        private TabPage BuildShipmentPage()
        {
            var page = CreatePage("出库");
            var orderId = AddTextRow(page, "订单 ID");
            var customer = AddTextRow(page, "客户");
            var quantity = AddTextRow(page, "计划数量", "1");
            var reference = AddTextRow(page, "交付参考");
            var shipmentId = AddTextRow(page, "出库单 ID");
            var cartonId = AddTextRow(page, "卡通箱 ID");
            var key = AddIdempotencyRow(page);
            var output = AddOutput(page);
            AddAction(page, "创建出库单", async token =>
            {
                var result = await _service.CreateShipmentAsync(new MesShipmentRequest
                {
                    OrderId = orderId.Text, Customer = customer.Text,
                    PlannedQuantity = ParseInt(quantity.Text), DeliveryReference = reference.Text,
                    IdempotencyKey = key.Text
                }, token);
                ShowResult(output, result, Pretty);
                RenewKeyOnSuccess(result, key);
            });
            AddSecondaryAction(page, "扫描装箱", async token =>
            {
                var result = await _service.AddShipmentCartonAsync(shipmentId.Text,
                    new MesShipmentCartonRequest { CartonId = cartonId.Text, IdempotencyKey = key.Text }, token);
                ShowResult(output, result, Pretty);
                RenewKeyOnSuccess(result, key);
            });
            AddSecondaryAction(page, "确认出库", async token =>
            {
                var result = await _service.ConfirmShipmentAsync(shipmentId.Text, key.Text, token);
                ShowResult(output, result, Pretty);
                RenewKeyOnSuccess(result, key);
            });
            return page;
        }

        private TabPage BuildArchivePage()
        {
            var page = CreatePage("订单归档");
            var orderId = AddTextRow(page, "订单 ID");
            var repairTaskId = AddTextRow(page, "修复任务 ID");
            var key = AddIdempotencyRow(page);
            var output = AddOutput(page, 260);
            AddAction(page, "创建归档", async token =>
            {
                var result = await _service.ArchiveOrderAsync(orderId.Text, key.Text, token);
                ShowResult(output, result, Pretty);
                RenewKeyOnSuccess(result, key);
            });
            AddSecondaryAction(page, "查询并校验归档", async token =>
                ShowResult(output, await _service.GetOrderArchiveAsync(orderId.Text, token), Pretty));
            AddSecondaryAction(page, "查询修复任务", async token =>
                ShowResult(output, await _service.GetArchiveRepairTasksAsync("Open", token), Pretty));
            AddSecondaryAction(page, "修复并重建归档", async token =>
            {
                var result = await _service.RepairArchiveAsync(repairTaskId.Text, key.Text, token);
                ShowResult(output, result, Pretty);
                RenewKeyOnSuccess(result, key);
            });
            return page;
        }

        private TabPage BuildImportPage()
        {
            var page = CreatePage("导入批次");
            var objectType = AddComboRow(page, "对象类型", "orders", "number-ranges");
            var filePath = AddTextRow(page, "CSV 文件");
            var batchId = AddTextRow(page, "批次 ID");
            var key = AddIdempotencyRow(page);
            var output = AddOutput(page, 240);
            AddAction(page, "选择并验证文件", async token =>
            {
                using var dialog = new OpenFileDialog { Filter = "CSV 数据 (*.csv)|*.csv", CheckFileExists = true };
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                filePath.Text = dialog.FileName;
                if (new FileInfo(dialog.FileName).Length > 10 * 1024 * 1024)
                    throw new InvalidOperationException("CSV 文件不能超过 10 MB。");
                var content = await File.ReadAllBytesAsync(dialog.FileName, token);
                var result = await _service.StageCsvImportAsync(objectType.Text, content, key.Text, token);
                ShowResult(output, result, Pretty);
                RenewKeyOnSuccess(result, key);
            });
            AddSecondaryAction(page, "刷新批次", async token =>
                ShowResult(output, await _service.GetCsvImportAsync(batchId.Text, token), Pretty));
            AddSecondaryAction(page, "确认原子导入", async token =>
            {
                var result = await _service.ConfirmCsvImportAsync(batchId.Text, key.Text, token);
                ShowResult(output, result, Pretty);
                RenewKeyOnSuccess(result, key);
            });
            return page;
        }

        private TabPage BuildExportPage()
        {
            var page = CreatePage("导出作业");
            var objectType = AddComboRow(page, "对象类型", "orders", "number-ranges", "traceability");
            var savePath = AddTextRow(page, "保存路径");
            var output = AddOutput(page, 260);
            AddAction(page, "导出权限脱敏 CSV", async token =>
            {
                var result = await _service.ExportCsvAsync(objectType.Text, token);
                if (result.IsSuccess)
                {
                    using var dialog = new SaveFileDialog { Filter = "CSV 数据 (*.csv)|*.csv", FileName = objectType.Text + ".csv" };
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        await File.WriteAllTextAsync(dialog.FileName, result.Value, token);
                        savePath.Text = dialog.FileName;
                    }
                }
                ShowResult(output, result, value => value);
            });
            return page;
        }

        private TabPage BuildRecoveryPage()
        {
            var page = CreatePage("操作恢复");
            var note = AddTextRow(page, "人工处理说明", "已核对现场实物与中心状态");
            var refresh = new Button { Text = "刷新待处理", AutoSize = true, MinimumSize = new System.Drawing.Size(120, 32) };
            var resubmit = new Button { Text = "重新提交", AutoSize = true, MinimumSize = new System.Drawing.Size(120, 32) };
            var manual = new Button { Text = "转人工处理", AutoSize = true, MinimumSize = new System.Drawing.Size(120, 32) };
            var commands = new FlowLayoutPanel
            {
                Left = 18, Top = NextRow(page), Width = 520, Height = 42,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Tag = "row"
            };
            commands.Controls.AddRange(new Control[] { refresh, resubmit, manual });
            page.Controls.Add(commands);
            _reviewGrid.Left = 18;
            _reviewGrid.Top = commands.Bottom + 8;
            _reviewGrid.Width = Math.Max(320, page.ClientSize.Width - 36);
            _reviewGrid.Height = 280;
            _reviewGrid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            page.Controls.Add(_reviewGrid);
            refresh.Click += async (_, _) => await RunUiActionAsync(refresh, token =>
            {
                RefreshReviewGrid();
                return Task.CompletedTask;
            });
            resubmit.Click += async (_, _) => await RunUiActionAsync(resubmit, async token =>
            {
                var id = SelectedPendingOperationId();
                if (id.Length == 0) return;
                var result = await _service.ResubmitPendingOperationAsync(id, token);
                if (!result.IsSuccess) MessageBox.Show(this, FormatError(result.Error), "恢复失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                RefreshReviewGrid();
            });
            manual.Click += async (_, _) => await RunUiActionAsync(manual, token =>
            {
                var id = SelectedPendingOperationId();
                if (id.Length > 0) _service.MarkPendingOperationForManualReview(id, note.Text);
                RefreshReviewGrid();
                return Task.CompletedTask;
            });
            MiuiTheme.StyleButton(refresh);
            MiuiTheme.StyleButton(resubmit, true);
            MiuiTheme.StyleButton(manual);
            MiuiTheme.StyleDataGridView(_reviewGrid);
            return page;
        }

        private string SelectedPendingOperationId()
        {
            if (_reviewGrid.CurrentRow?.DataBoundItem == null)
            {
                MessageBox.Show(this, "请先选择一条待处理记录。", "操作恢复",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return "";
            }
            var property = _reviewGrid.CurrentRow.DataBoundItem.GetType().GetProperty("记录ID");
            return property?.GetValue(_reviewGrid.CurrentRow.DataBoundItem)?.ToString() ?? "";
        }
    }
}
