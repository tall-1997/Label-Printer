using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BarTenderPrinter
{
    internal sealed class MesLoggerAdapter : IMesClientLog
    {
        public void Info(string message) => LoggerService.Info(message);
        public void Warn(string message) => LoggerService.Warn(message);
    }

    internal sealed class MesWorkstationPanel : Panel
    {
        private readonly MesWorkstationService _service;
        private readonly MesConnectionOptionsStore _optionsStore;
        private readonly Func<MesPrintJob, Task<PrintJobCompletion>> _printExecutor;
        private readonly Action<string, string> _log;
        private readonly Action<int> _operationCountChanged;
        private readonly CancellationTokenSource _lifetimeCancellation = new CancellationTokenSource();
        private readonly TextBox _baseUrl = new TextBox();
        private readonly TextBox _token = new TextBox { UseSystemPasswordChar = true };
        private readonly NumericUpDown _timeout = new NumericUpDown { Minimum = 1, Maximum = 120 };
        private readonly NumericUpDown _retries = new NumericUpDown { Minimum = 0, Maximum = 3 };
        private readonly Label _connectionStatus = new Label { AutoSize = true };
        private readonly TabControl _tabs = new TabControl { Dock = DockStyle.Fill };
        private readonly DataGridView _reviewGrid = CreateGrid();
        private MesPrintJob _claimedJob;
        private readonly Label _printJobSummary = new Label { Dock = DockStyle.Top, Height = 62, AutoEllipsis = true };

        public MesWorkstationPanel(MesWorkstationService service, MesConnectionOptionsStore optionsStore,
            Func<MesPrintJob, Task<PrintJobCompletion>> printExecutor, Action<string, string> log,
            Action<int> operationCountChanged)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _optionsStore = optionsStore ?? throw new ArgumentNullException(nameof(optionsStore));
            _printExecutor = printExecutor ?? throw new ArgumentNullException(nameof(printExecutor));
            _log = log ?? ((_, _) => { });
            _operationCountChanged = operationCountChanged ?? (_ => { });
            Dock = DockStyle.Fill;
            BackColor = MiuiTheme.Background;
            Padding = new Padding(12);
            BuildConnectionCard();
            BuildTabs();
            MiuiTheme.StyleTabControl(_tabs);
            RefreshReviewGrid();
        }

        private void BuildConnectionCard()
        {
            var options = _optionsStore.Load();
            _baseUrl.Text = options.BaseUrl;
            _timeout.Value = options.TimeoutSeconds;
            _retries.Value = options.MaxRetries;
            var card = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 76,
                Padding = new Padding(10),
                WrapContents = true,
                BackColor = MiuiTheme.CardBackground
            };
            var connect = new Button { Text = "保存并连接", AutoSize = true };
            connect.Click += async (_, _) => await RunUiActionAsync(connect, SaveAndConnectAsync);
            AddField(card, "MES 地址", _baseUrl, 260);
            AddField(card, "访问令牌", _token, 180);
            AddField(card, "超时(秒)", _timeout, 70);
            AddField(card, "重试", _retries, 55);
            card.Controls.Add(connect);
            card.Controls.Add(_connectionStatus);
            MiuiTheme.StyleButton(connect, true);
            MiuiTheme.StyleTextBox(_baseUrl);
            MiuiTheme.StyleTextBox(_token);
            MiuiTheme.StyleNumericUpDown(_timeout);
            MiuiTheme.StyleNumericUpDown(_retries);
            MiuiTheme.StyleLabel(_connectionStatus, true);
            Controls.Add(_tabs);
            Controls.Add(card);
        }

        private void BuildTabs()
        {
            _tabs.TabPages.Add(BuildOrderPage());
            _tabs.TabPages.Add(BuildStationPage());
            _tabs.TabPages.Add(BuildPackagingPage());
            _tabs.TabPages.Add(BuildPrintPage());
            _tabs.TabPages.Add(BuildTraceabilityPage());
        }

        private TabPage BuildOrderPage()
        {
            var page = CreatePage("订单工位");
            var orderId = AddTextRow(page, "订单 ID");
            var output = AddOutput(page);
            AddAction(page, "查询订单", async cancellationToken =>
            {
                var result = await _service.GetOrderAsync(orderId.Text.Trim(), cancellationToken);
                ShowResult(output, result, value => JsonSerializer.Serialize(value, PrettyJson));
            });
            return page;
        }

        private TabPage BuildStationPage()
        {
            var page = CreatePage("组装过站");
            var unitId = AddTextRow(page, "生产单元 ID / SN");
            var orderId = AddTextRow(page, "订单 ID");
            var routeId = AddTextRow(page, "路线 ID");
            var operationId = AddTextRow(page, "工序 ID");
            var key = AddTextRow(page, "幂等键", Guid.NewGuid().ToString("N"));
            var output = AddOutput(page);
            AddAction(page, "提交过站", async cancellationToken =>
            {
                var result = await _service.PassStationAsync(new MesStationPassRequest
                {
                    UnitId = unitId.Text.Trim(), OrderId = orderId.Text.Trim(), RouteId = routeId.Text.Trim(),
                    OperationId = operationId.Text.Trim(), IdempotencyKey = key.Text.Trim()
                }, cancellationToken);
                ShowResult(output, result, value => value.GetRawText());
                if (result.IsSuccess) key.Text = Guid.NewGuid().ToString("N");
                RefreshReviewGrid();
            });
            return page;
        }

        private TabPage BuildPackagingPage()
        {
            var page = CreatePage("包装过站");
            var parent = AddTextRow(page, "父包装 ID");
            var child = AddTextRow(page, "子包装 ID");
            var version = AddTextRow(page, "父包装版本", "0");
            var key = AddTextRow(page, "幂等键", Guid.NewGuid().ToString("N"));
            var output = AddOutput(page);
            AddAction(page, "提交绑定", async cancellationToken =>
            {
                long.TryParse(version.Text, out var expectedVersion);
                var result = await _service.BindPackagingAsync(new MesPackagingBindRequest
                {
                    ParentId = parent.Text.Trim(), ChildId = child.Text.Trim(), ExpectedParentVersion = expectedVersion,
                    IdempotencyKey = key.Text.Trim()
                }, cancellationToken);
                ShowResult(output, result, value => value.GetRawText());
                if (result.IsSuccess) key.Text = Guid.NewGuid().ToString("N");
                RefreshReviewGrid();
            });
            return page;
        }

        private TabPage BuildPrintPage()
        {
            var page = CreatePage("MES 打印");
            var claim = new Button { Text = "领取打印作业", Width = 130, Height = 32, Left = 18, Top = 18 };
            var execute = new Button { Text = "使用现有打印流程执行", Width = 180, Height = 32, Left = 160, Top = 18, Enabled = false };
            var recover = new Button { Text = "恢复同步", Width = 110, Height = 32, Left = 352, Top = 18 };
            _printJobSummary.Top = 62;
            _printJobSummary.Text = "尚未领取作业";
            claim.Click += async (_, _) => await RunUiActionAsync(claim, async cancellationToken =>
            {
                var result = await _service.ClaimPrintJobAsync("claim-" + Guid.NewGuid().ToString("N"), cancellationToken);
                if (result.IsSuccess && result.Value?.Job != null)
                {
                    _claimedJob = result.Value.Job;
                    _printJobSummary.Text = $"标签: {_claimedJob.LabelType}   MES 作业: {_claimedJob.JobId}\r\n批次: {_claimedJob.ReadRequestString("batchId")}   同步状态: {_claimedJob.State}";
                    execute.Enabled = true;
                }
                else if (result.IsSuccess)
                    _printJobSummary.Text = "MES 当前没有待领取打印作业。";
                else ShowError(_printJobSummary, result.Error);
                RefreshReviewGrid();
            });
            execute.Click += async (_, _) => await RunUiActionAsync(execute, async cancellationToken =>
            {
                if (_claimedJob == null) return;
                execute.Enabled = false;
                var completion = await _printExecutor(_claimedJob);
                var state = completion.PrintResult.State.ToString();
                var receipt = await _service.SubmitPrintReceiptAsync(_claimedJob, state, new
                {
                    state,
                    completion.HistorySaved,
                    completion.HistoryStatus,
                    diagnostic = "<redacted>"
                }, cancellationToken);
                _printJobSummary.Text = receipt.IsSuccess
                    ? $"标签: {_claimedJob.LabelType}   MES 作业: {_claimedJob.JobId}\r\n同步状态: {receipt.Value.State}"
                    : $"本地结果已保留，等待恢复同步。{FormatError(receipt.Error)}";
                _claimedJob = null;
                RefreshReviewGrid();
            });
            recover.Click += async (_, _) => await RunUiActionAsync(recover, async cancellationToken =>
            {
                var recovery = await _service.RecoverPrintJobsAsync(cancellationToken);
                _claimedJob = recovery.PrintableJobs.FirstOrDefault();
                execute.Enabled = _claimedJob != null;
                _printJobSummary.Text = _claimedJob == null
                    ? $"恢复查询完成，已核对 {recovery.RecoveredCount} 个 MES 打印作业。"
                    : $"已恢复可打印作业: {_claimedJob.JobId}，标签: {_claimedJob.LabelType}";
                RefreshReviewGrid();
            });
            MiuiTheme.StyleButton(claim, true);
            MiuiTheme.StyleButton(execute);
            MiuiTheme.StyleButton(recover);
            MiuiTheme.StyleLabel(_printJobSummary);
            page.Controls.AddRange(new Control[] { claim, execute, recover, _printJobSummary });
            return page;
        }

        private TabPage BuildTraceabilityPage()
        {
            var page = CreatePage("追溯与核查");
            var type = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            type.Items.AddRange(new object[] { "Order", "Imei", "SerialNumber", "Carton", "Pallet" });
            type.SelectedIndex = 0;
            AddControlRow(page, "查询类型", type);
            var value = AddTextRow(page, "查询值");
            var output = AddOutput(page, 250);
            AddAction(page, "追溯查询", async cancellationToken =>
            {
                var result = await _service.QueryTraceabilityAsync(type.Text, value.Text.Trim(), cancellationToken);
                ShowResult(output, result, data => JsonSerializer.Serialize(data, PrettyJson));
            });
            _reviewGrid.Top = output.Bottom + 12;
            _reviewGrid.Left = 18;
            _reviewGrid.Width = Math.Max(320, page.ClientSize.Width - 36);
            _reviewGrid.Height = 180;
            _reviewGrid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            page.Controls.Add(_reviewGrid);
            MiuiTheme.StyleComboBox(type);
            MiuiTheme.StyleDataGridView(_reviewGrid);
            return page;
        }

        private async Task SaveAndConnectAsync(CancellationToken cancellationToken)
        {
            try
            {
                var options = new MesConnectionOptions
                {
                    BaseUrl = _baseUrl.Text,
                    TimeoutSeconds = (int)_timeout.Value,
                    MaxRetries = (int)_retries.Value
                }.Normalize();
                _optionsStore.Save(options);
                _service.Configure(options, _token.Text);
                var result = await _service.CheckHealthAsync(cancellationToken);
                _connectionStatus.Text = result.IsSuccess ? "MES 已连接" : FormatError(result.Error);
                _connectionStatus.ForeColor = result.IsSuccess ? MiuiTheme.Success : MiuiTheme.Error;
                _log(result.IsSuccess ? "MES 连接成功" : "MES 连接失败: " + FormatError(result.Error), result.IsSuccess ? "SUCCESS" : "ERROR");
                if (result.IsSuccess) await _service.RecoverPrintJobsAsync(cancellationToken);
                RefreshReviewGrid();
            }
            catch (ArgumentException ex)
            {
                _connectionStatus.Text = ex.Message;
                _connectionStatus.ForeColor = MiuiTheme.Error;
            }
        }

        private void RefreshReviewGrid()
        {
            var operations = _service.PendingOperations;
            if (!string.IsNullOrWhiteSpace(_service.PendingOperationsError))
            {
                _connectionStatus.Text = _service.PendingOperationsError;
                _connectionStatus.ForeColor = MiuiTheme.Error;
                _log(_service.PendingOperationsError, "ERROR");
            }
            _reviewGrid.DataSource = operations
                .Where(item => item.State != MesPendingState.Synced)
                .Select(item => new
                {
                    类型 = item.Kind,
                    业务标识 = item.BusinessId,
                    幂等键 = item.IdempotencyKey,
                    状态 = item.State.ToString(),
                    错误码 = item.ErrorCode,
                    更新时间 = item.UpdatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
                }).ToList();
        }

        private static readonly JsonSerializerOptions PrettyJson = new JsonSerializerOptions { WriteIndented = true };
        private static TabPage CreatePage(string text) => new TabPage(text) { BackColor = MiuiTheme.CardBackground, AutoScroll = true };
        private static DataGridView CreateGrid() => new DataGridView
        {
            ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.FullRowSelect
        };

        private static void AddField(FlowLayoutPanel panel, string caption, Control control, int width)
        {
            var field = new FlowLayoutPanel { Width = width, Height = 48, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            field.Controls.Add(new Label { Text = caption, AutoSize = true });
            control.Width = width - 8;
            field.Controls.Add(control);
            panel.Controls.Add(field);
        }

        private static int NextRow(TabPage page) => page.Controls.Cast<Control>().Where(control => control.Tag as string == "row")
            .Select(control => control.Bottom).DefaultIfEmpty(12).Max() + 8;

        private static TextBox AddTextRow(TabPage page, string caption, string value = "")
        {
            var text = new TextBox { Text = value };
            AddControlRow(page, caption, text);
            MiuiTheme.StyleTextBox(text);
            return text;
        }

        private static void AddControlRow(TabPage page, string caption, Control control)
        {
            var top = NextRow(page);
            var label = new Label { Text = caption + "：", Left = 18, Top = top + 4, Width = 130, Tag = "row" };
            control.Left = 150;
            control.Top = top;
            control.Width = 360;
            control.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            control.Tag = "row";
            page.Controls.Add(label);
            page.Controls.Add(control);
            MiuiTheme.StyleLabel(label);
        }

        private static TextBox AddOutput(TabPage page, int height = 150)
        {
            var output = new TextBox
            {
                Left = 18, Top = NextRow(page), Width = Math.Max(320, page.ClientSize.Width - 36), Height = height,
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Tag = "row"
            };
            page.Controls.Add(output);
            MiuiTheme.StyleTextBox(output);
            return output;
        }

        private void AddAction(TabPage page, string caption, Func<CancellationToken, Task> action)
        {
            var button = new Button { Text = caption, Left = 526, Top = NextRow(page) - 36, Width = 120, Height = 30 };
            button.Click += async (_, _) =>
            {
                await RunUiActionAsync(button, action);
            };
            page.Controls.Add(button);
            MiuiTheme.StyleButton(button, true);
        }

        private async Task RunUiActionAsync(Control control, Func<CancellationToken, Task> action)
        {
            control.Enabled = false;
            _operationCountChanged(1);
            try
            {
                await action(_lifetimeCancellation.Token);
            }
            catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested) { }
            catch (Exception ex)
            {
                var message = ex.GetBaseException().Message;
                _log("MES 操作失败: " + message, "ERROR");
                MessageBox.Show(this, message, "MES 操作失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _operationCountChanged(-1);
                if (!control.IsDisposed) control.Enabled = true;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _lifetimeCancellation.Cancel();
                _lifetimeCancellation.Dispose();
            }
            base.Dispose(disposing);
        }

        private static void ShowResult<T>(TextBox output, MesResult<T> result, Func<T, string> format)
        {
            output.Text = result.IsSuccess ? format(result.Value) : FormatError(result.Error);
        }

        private static void ShowError(Label label, MesApiError error) => label.Text = FormatError(error);
        private static string FormatError(MesApiError error) => error == null ? "MES 请求失败。" :
            $"{error.Code}: {error.Message} 关联 ID: {error.CorrelationId}";
    }
}
