using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BarTenderPrinter
{
    public partial class MainForm : Form
    {
        private readonly IBarTenderService _btService = new BarTenderService();
        private readonly IHistoryRepository _history = new HistoryManager();
        private readonly TemplateSettingsManager _templateSettings = new TemplateSettingsManager();
        private readonly OrderManager _orders = new OrderManager();
        private readonly PrintWorkflow _printWorkflow = new PrintWorkflow();
        private readonly OrderEditorController _orderEditor = new OrderEditorController();
        private readonly IDialogService _dialogs = new DialogService();
        private readonly ApplicationStateManager _applicationStateManager = new ApplicationStateManager();
        private readonly System.Windows.Forms.Timer _historySearchTimer = new System.Windows.Forms.Timer { Interval = 180 };
        private readonly string _startupTemplatePath;
        private readonly string _configFile;
        private readonly string _version = "v5.7.72";

        private List<DataSourceItem> _dataSources = new List<DataSourceItem>();
        private TextBox[] _inputTextBoxes = new TextBox[0];
        private Panel[] _rowPanels = new Panel[0];
        private Button[] _lockButtons = new Button[0];
        private string _templatesFolder = "";
        private string _selectedTemplatePath = "";
        private HashSet<string> _localData = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string _localDataPath = "";
        private string _localDataStoragePath = "";
        private string _localDataColumnName = "";
        private string _localDataTargetField = "";
        private bool _useLocalDataValidation = false;
        private bool _duplicateValidationEnabled = true;
        private bool _isInitializing = true;
        private bool _isLoadingConfig;
        private bool _hasSavedDataSourceOrder;
        private List<DataSourceItem> _legacyDataSourcesPending = new List<DataSourceItem>();
        private int _dataSourceLoadVersion;
        private bool _lengthValidationEnabled;
        private int _globalExpectedLength;
        private long _globalLengthRevision;
        private long _lengthRevisionCounter;
        private GroupBox _orderPanel;
        private ComboBox _cmbOrderCustomer;
        private ComboBox _cmbOrderModel;
        private ComboBox _cmbOrderColor;
        private ComboBox _cmbOrderNumber;
        private Button _btnAddOrder;
        private Panel _navPanel;
        private Button _btnSidebarToggle;
        private Button _btnPrintPage;
        private Button _btnOrderPage;
        private bool _sidebarExpanded;
        private Panel _printOrderPanel;
        private ComboBox _cmbPrintCustomer;
        private ComboBox _cmbPrintModel;
        private ComboBox _cmbPrintColor;
        private ComboBox _cmbPrintOrderNumber;
        private Panel _orderPagePanel;
        private Panel _orderContentPanel;
        private ComboBox _txtOrderCustomer;
        private ComboBox _txtOrderModel;
        private ComboBox _txtOrderColor;
        private ComboBox _txtOrderNumber;
        private TextBox _txtOrderTemplate;
        private FlowLayoutPanel _orderTemplateCards;
        private ComboBox _cmbOrderPrinter;
        private NumericUpDown _numOrderCopies;
        private CheckBox _chkOrderInputValidation;
        private CheckBox _chkOrderDuplicateValidation;
        private CheckBox _chkOrderLengthValidation;
        private NumericUpDown _numOrderGlobalLength;
        private Label _lblOrderLocalData;
        private DataGridView _orderDataSourcesGrid;
        private readonly List<OrderTemplate> _orderTemplateDrafts = new List<OrderTemplate>();
        private OrderTemplate _selectedOrderTemplateDraft;
        private PackagingOrder _editingOrder;
        private PackagingOrder _activeOrder;
        private OrderTemplate _activeOrderTemplate;
        private bool _loadingOrderTemplate;
        private bool _loadingOrderFilters;
        private bool _loadingPrintOrderFilters;
        private bool _loadingOrderEditor;
        private bool _orderEditorDirty;
        private bool _applyingOrderGlobalLength;
        private bool _updatingOrderToggleAll;
        private CheckBox _chkOrderToggleAllSources;
        private TextBox _txtOperator;
        private ComboBox _cmbRole;
        private Button _btnPrevHistoryPage;
        private Button _btnNextHistoryPage;
        private Button _btnLogin;
        private Label _lblHistoryPage;
        private Label _lblEffectiveSummary;
        private ComboBox _cmbHistoryStatus;
        private TextBox _txtHistoryDate;
        private readonly ToolTip _toolTips = new ToolTip();
        private readonly Dictionary<string, int> _historyColumnWidths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly UserSession _session = new UserSession();
        private readonly AccountManager _accountManager = new AccountManager();
        private readonly Dictionary<string, int> _pendingPrintValues = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private CheckBox _chkPreview;
        private PreviewForm _previewForm;
        private int _previewRequestVersion;
        private bool _closingPreviewForm;
        private Rectangle? _prePreviewBounds;
        private FormWindowState? _prePreviewWindowState;
        private Size? _prePreviewMinimumSize;
        private bool _tilingPreview;
        private int _pendingPrintJobCount;
        private int _historyPageIndex;
        private const int HistoryPageSize = 200;
        private ApplicationState _applicationState = new ApplicationState();
        private readonly HashSet<string> _availablePrinters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private PictureBox _brandIcon;
        private Label _versionBadge;
        private Label _authorLabel;
        private Panel _todayStatsCard;
        private Panel _totalStatsCard;
        private Button _btnToggleLog;
        private Button _btnAbout;
        private int _historyToolbarWidth;

        private int ScaleUi(int value) => (int)Math.Round(value * Math.Max(1F, DeviceDpi / 96F));
        private int SidebarCollapsedWidth => ScaleUi(12);
        private int SidebarExpandedWidth => ScaleUi(168);

        public MainForm(string startupTemplatePath = null)
        {
            _startupTemplatePath = NormalizeStartupTemplatePath(startupTemplatePath);
            InitializeComponent();
            InstallP2Controls();
            InstallPreviewControl();
            SilentLogin();
            InstallOrderSidebar();
            ConfigureModernShell();
            _configFile = AppPaths.ConfigFile;
            _applicationState = _applicationStateManager.Load();
            Text = $"BarTender Printer {_version}";
            MiuiTheme.ApplyTheme(this);
            DpiChanged += (s, e) =>
            {
                if (IsDisposed || Disposing || !IsHandleCreated) return;
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        if (IsDisposed || Disposing) return;
                        ApplyModernIcons(e.DeviceDpiNew);
                        MiuiTheme.StyleTabControl(tabBottom, e.DeviceDpiNew);
                        LayoutStatsCards();
                        LayoutModernShell();
                        RebuildPrintPageLayout();
                        DockPreviewForm();
                    }));
                }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            };
            FormClosed += (s, e) => DisposeModernImages();
            Load += MainForm_Load;
            Shown += MainForm_Shown;
            FormClosing += (s, e) =>
            {
                if (MessageBox.Show(this, "确定退出软件？", "退出确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                { e.Cancel = true; return; }
                if (!ConfirmOrderEditorChanges()) { e.Cancel = true; return; }
                SaveCurrentTemplateSettings();
                SaveApplicationState();
                ClosePreviewForm();
                _historySearchTimer.Dispose();
                _btService.Dispose();
            };
            inputPanel.SizeChanged += InputPanel_SizeChanged;
            inputPanel.Scroll += (s, e) => ClampInputPanelScroll();
            inputPanel.MouseWheel += (s, e) => BeginInvoke((Action)ClampInputPanelScroll);
            SizeChanged += (s, e) => { RebuildPrintPageLayout(); DockPreviewForm(); };
            historyPanel.SizeChanged += (s, e) =>
            {
                if (_historyToolbarWidth != historyPanel.ClientSize.Width) LayoutHistoryToolbar();
            };
            LocationChanged += (s, e) => DockPreviewForm();
            dgvHistory.CellDoubleClick += DgvHistory_CellDoubleClick;
            dgvHistory.CellMouseDown += DgvHistory_CellMouseDown;
            dgvHistory.ColumnWidthChanged += (s, e) => { if (e.Column != null) { _historyColumnWidths[e.Column.Name] = e.Column.Width; SaveConfig(); } };
            var historyMenu = new ContextMenuStrip();
            historyMenu.Items.Add("从历史控件排除此记录", null, DeleteSelectedHistoryRecord_Click);
            historyMenu.Opening += HistoryMenu_Opening;
            dgvHistory.ContextMenuStrip = historyMenu;
            cmbPrinter.SelectedIndexChanged += (s, e) => SaveCurrentConfigurationState();
            _historySearchTimer.Tick += (s, e) => { _historySearchTimer.Stop(); LoadHistory(); };
        }

        private void ConfigureModernShell()
        {
            try
            {
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
                _brandIcon = new PictureBox
                {
                    Location = new Point(16, 10),
                    Size = new Size(38, 38),
                    SizeMode = PictureBoxSizeMode.Zoom,
                    BackColor = Color.Transparent
                };
                titlePanel.Controls.Add(_brandIcon);
                _brandIcon.BringToFront();
            }
            catch (Exception ex) { LoggerService.Warn($"加载应用图标失败: {ex.Message}"); }

            titleLabel.Text = "BarTender Printer";
            _versionBadge = new Label
            {
                Text = _version,
                AutoSize = true,
                Location = new Point(205, 11),
                Padding = new Padding(7, 2, 7, 2),
                BackColor = MiuiTheme.PrimaryLight,
                ForeColor = MiuiTheme.PrimaryDark,
                Font = MiuiTheme.VersionFont
            };
            _authorLabel = new Label
            {
                Text = "By---池鱼",
                AutoSize = true,
                Location = new Point(59, 34),
                ForeColor = MiuiTheme.TextSecondary,
                Font = MiuiTheme.SecondaryFont
            };
            titlePanel.Controls.Add(_versionBadge);
            titlePanel.Controls.Add(_authorLabel);
            _versionBadge.BringToFront();
            _authorLabel.BringToFront();

            _btnAbout = new Button { Text = "关于", Dock = DockStyle.Right, Width = 84 };
            _btnToggleLog = new Button { Text = "收起日志", Dock = DockStyle.Right, Width = 104 };
            _btnAbout.Click += (s, e) => ShowAboutDialog();
            _btnToggleLog.Click += (s, e) => ToggleLogPanel();
            titlePanel.Controls.Add(_btnAbout);
            titlePanel.Controls.Add(_btnToggleLog);
            titlePanel.Controls.SetChildIndex(_btnAbout, 0);
            titlePanel.Controls.SetChildIndex(_btnToggleLog, 0);
            _btnSidebarToggle.Dock = DockStyle.Right;
            titlePanel.Controls.Add(_btnSidebarToggle);
            titlePanel.Controls.SetChildIndex(_btnSidebarToggle, 0);
            MiuiTheme.StyleButton(_btnAbout);
            MiuiTheme.StyleButton(_btnToggleLog);

            ApplyModernIcons();

            titlePanel.Paint += (sender, args) =>
            {
                using (var pen = new Pen(MiuiTheme.Divider))
                    args.Graphics.DrawLine(pen, 0, titlePanel.Height - 1, titlePanel.Width, titlePanel.Height - 1);
            };
            _navPanel.BackColor = MiuiTheme.Sidebar;
            _btnSidebarToggle.BackColor = MiuiTheme.CardBackground;
            _btnSidebarToggle.FlatAppearance.BorderSize = 0;
            _btnSidebarToggle.FlatAppearance.MouseOverBackColor = MiuiTheme.SidebarHover;
            _btnPrintPage.ForeColor = Color.White;
            _btnOrderPage.ForeColor = MiuiTheme.TextPrimary;
            _btnOrderPage.BackColor = MiuiTheme.Sidebar;
            _btnOrderPage.FlatAppearance.BorderSize = 0;
            _btnOrderPage.FlatAppearance.MouseOverBackColor = MiuiTheme.SidebarHover;

            MiuiTheme.StyleComboBox(cmbTemplate);
            MiuiTheme.StyleComboBox(cmbPrinter);
            MiuiTheme.StyleNumericUpDown(numCopies);
            MiuiTheme.StyleCheckBox(_chkPreview);
            MiuiTheme.StyleTabControl(tabBottom, DeviceDpi);
            MiuiTheme.StyleDataGridView(dgvHistory);
            MiuiTheme.StyleStatusStrip(statusStrip);
            ConfigureWorkspaceCards();
            lblConnection.ForeColor = MiuiTheme.Warning;
            lblTodayStatus.ForeColor = MiuiTheme.TextSecondary;
            lblTotalStatus.ForeColor = MiuiTheme.TextSecondary;
            lblVersion.ForeColor = MiuiTheme.Primary;
            LayoutModernShell();
        }

        private void LayoutModernShell()
        {
            var commandHeight = Math.Max(ScaleUi(36), titlePanel.ClientSize.Height - ScaleUi(16));
            var compact = titlePanel.ClientSize.Width < ScaleUi(900);
            titleLabel.Visible = !compact;
            if (_brandIcon != null) _brandIcon.Visible = !compact;
            if (_versionBadge != null) _versionBadge.Visible = !compact;
            if (_authorLabel != null) _authorLabel.Visible = !compact;
            btnExportLog.Text = compact ? string.Empty : "导出日志";
            _btnToggleLog.Text = compact ? string.Empty : groupBoxLog.Visible ? "收起日志" : "展开日志";
            _btnAbout.Text = compact ? string.Empty : "关于";
            if (_chkPreview != null) _chkPreview.Text = compact ? "预览" : _btService.IsPreviewAvailable ? "开启预览" : "预览不可用";
            _toolTips.SetToolTip(btnExportLog, "导出日志");
            _toolTips.SetToolTip(_btnToggleLog, groupBoxLog.Visible ? "收起日志" : "展开日志");
            _toolTips.SetToolTip(_btnAbout, "关于");
            foreach (var button in new[] { btnExportLog, _btnToggleLog, _btnAbout, _btnSidebarToggle })
            {
                if (button == null) continue;
                button.Height = commandHeight;
                button.Width = compact || button == _btnSidebarToggle
                    ? ScaleUi(44)
                    : Math.Max(ScaleUi(92), button.GetPreferredSize(new Size(0, commandHeight)).Width + ScaleUi(10));
                button.Margin = new Padding(ScaleUi(4), 0, ScaleUi(4), 0);
            }
            if (_chkPreview != null) _chkPreview.Padding = new Padding(ScaleUi(10), ScaleUi(9), ScaleUi(10), 0);
            LayoutSidebar();
            LayoutHistoryToolbar();
        }

        private void LayoutSidebar()
        {
            if (_navPanel == null) return;
            var sidebarWidth = _sidebarExpanded ? SidebarExpandedWidth : SidebarCollapsedWidth;
            _navPanel.Width = sidebarWidth;
            _navPanel.Height = Math.Max(ScaleUi(120), WorkspaceBottom - titlePanel.Bottom);
            var navWidth = Math.Max(ScaleUi(136), SidebarExpandedWidth - ScaleUi(16));
            _btnPrintPage.Bounds = new Rectangle(ScaleUi(8), ScaleUi(12), navWidth, ScaleUi(44));
            _btnOrderPage.Bounds = new Rectangle(ScaleUi(8), ScaleUi(64), navWidth, ScaleUi(44));
            if (_printOrderPanel != null) _printOrderPanel.Left = sidebarWidth + ScaleUi(12);
            if (_orderPagePanel != null)
            {
                _orderPagePanel.Left = sidebarWidth;
                _orderPagePanel.Width = Math.Max(ScaleUi(320), ClientSize.Width - sidebarWidth);
            }
        }

        private void LayoutHistoryToolbar()
        {
            if (historyPanel?.Controls.OfType<FlowLayoutPanel>().FirstOrDefault() is not FlowLayoutPanel toolbar) return;
            _historyToolbarWidth = historyPanel.ClientSize.Width;
            toolbar.Padding = new Padding(ScaleUi(4));
            foreach (Control control in toolbar.Controls)
            {
                control.Margin = new Padding(ScaleUi(3));
                if (control is Button button)
                {
                    button.AutoSize = true;
                    button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                    button.MinimumSize = new Size(ScaleUi(58), ScaleUi(32));
                }
                else if (control is Label label)
                {
                    label.AutoSize = true;
                    label.MinimumSize = new Size(0, ScaleUi(28));
                    label.TextAlign = ContentAlignment.MiddleLeft;
                }
                else if (control is TextBox or ComboBox)
                {
                    control.Height = Math.Max(control.PreferredSize.Height, ScaleUi(28));
                }
            }
            if (_txtOperator != null) _txtOperator.Width = ScaleUi(96);
            if (_cmbRole != null) _cmbRole.Width = ScaleUi(104);
            if (_cmbHistoryStatus != null) _cmbHistoryStatus.Width = ScaleUi(124);
            if (_txtHistoryDate != null) _txtHistoryDate.Width = ScaleUi(112);
            if (txtSearch != null) txtSearch.Width = ScaleUi(190);
            var preferredHeight = toolbar.GetPreferredSize(new Size(Math.Max(ScaleUi(320), historyPanel.ClientSize.Width), 0)).Height;
            historyPanel.Height = Math.Max(ScaleUi(76), preferredHeight + ScaleUi(4));
        }

        private int WorkspaceBottom => groupBoxLog.Visible ? groupBoxLog.Top : statusStrip.Top;

        private void ToggleLogPanel()
        {
            groupBoxLog.Visible = !groupBoxLog.Visible;
            LayoutModernShell();
            RebuildPrintPageLayout();
            if (_orderPagePanel != null)
            {
                _orderPagePanel.Height = Math.Max(ScaleUi(120), WorkspaceBottom - titlePanel.Bottom);
                _orderContentPanel?.PerformLayout();
            }
        }

        private void ShowAboutDialog()
        {
            using (var form = new Form())
            using (var appImage = Icon?.ToBitmap())
            {
                form.Text = "关于 BarTender Printer";
                form.ClientSize = new Size(440, 330);
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterParent;
                form.MaximizeBox = false;
                form.MinimizeBox = false;
                form.Icon = Icon;

                var iconBox = new PictureBox
                {
                    Location = new Point(28, 26),
                    Size = new Size(72, 72),
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Image = appImage == null ? null : new Bitmap(appImage)
                };
                var product = new Label { Text = "BarTender Printer", Location = new Point(120, 30), AutoSize = true, Font = MiuiTheme.ProductTitleFont, ForeColor = MiuiTheme.TextPrimary };
                var version = new Label { Text = $"版本 {_version}", Location = new Point(122, 66), AutoSize = true, ForeColor = MiuiTheme.Primary };
                var author = new Label { Text = "By---池鱼", Location = new Point(122, 89), AutoSize = true, ForeColor = MiuiTheme.TextSecondary };
                var divider = new Panel { Location = new Point(28, 120), Size = new Size(384, 1), BackColor = MiuiTheme.Divider };
                var description = new Label
                {
                    Text = "面向包装 MES 场景的 BarTender 标签打印与历史追溯工具。\r\n支持订单模板、字段校验、打印预览、补打印和审计记录。",
                    Location = new Point(28, 142),
                    Size = new Size(384, 54),
                    ForeColor = MiuiTheme.TextPrimary
                };
                var runtime = new Label
                {
                    Text = $"运行环境  .NET {Environment.Version}  |  Windows x64\r\n数据目录  {AppPaths.DataDirectory}",
                    Location = new Point(28, 208),
                    Size = new Size(384, 48),
                    ForeColor = MiuiTheme.TextSecondary,
                    AutoEllipsis = true
                };
                var projectLink = new LinkLabel
                {
                    Text = "github.com/tall-1997/Label-Printer",
                    Location = new Point(28, 270),
                    AutoSize = true,
                    LinkColor = MiuiTheme.Primary
                };
                projectLink.LinkClicked += (s, e) =>
                {
                    try { Process.Start(new ProcessStartInfo("https://github.com/tall-1997/Label-Printer") { UseShellExecute = true }); }
                    catch (Exception ex) { LoggerService.Warn($"打开项目地址失败: {ex.Message}"); }
                };
                var close = new Button { Text = "关闭", Location = new Point(332, 266), Size = new Size(80, 32), DialogResult = DialogResult.OK };
                form.Controls.AddRange(new Control[] { iconBox, product, version, author, divider, description, runtime, projectLink, close });
                form.AcceptButton = close;
                StyleDialog(form, close);
                form.ShowDialog(this);
                iconBox.Image?.Dispose();
                iconBox.Image = null;
            }
        }

        private void StyleDialog(Form form, Button primaryButton = null, Button secondaryButton = null)
        {
            form.AutoScaleDimensions = new SizeF(96F, 96F);
            form.AutoScaleMode = AutoScaleMode.Dpi;
            form.Icon = Icon;
            MiuiTheme.ApplyTheme(form);
            foreach (var button in form.Controls.OfType<Button>())
                MiuiTheme.StyleButton(button, button == primaryButton);
            if (secondaryButton != null) MiuiTheme.StyleButton(secondaryButton);
        }

        private void ConfigureWorkspaceCards()
        {
            _printOrderPanel.BackColor = MiuiTheme.CardBackground;
            _printOrderPanel.Padding = new Padding(ScaleUi(12), ScaleUi(8), ScaleUi(12), ScaleUi(8));
            inputPanel.BackColor = MiuiTheme.CardBackground;
            inputPanel.Padding = Padding.Empty;
            inputPanel.BorderStyle = BorderStyle.None;
            historyPanel.BackColor = MiuiTheme.CardBackground;
            tabHistory.BackColor = MiuiTheme.CardBackground;
            tabStats.BackColor = MiuiTheme.CardBackground;
            groupBoxLog.Padding = new Padding(10, 8, 10, 8);
            txtLog.BackColor = MiuiTheme.InputBackground;
            txtLog.BorderStyle = BorderStyle.None;
            ConfigureStatsCards();
            foreach (var panel in new[] { _printOrderPanel, inputPanel, historyPanel })
            {
                panel.Paint += DrawCardBorder;
                panel.Invalidate();
            }
        }

        private void ConfigureStatsCards()
        {
            _todayStatsCard = new Panel { BackColor = MiuiTheme.PrimaryLight };
            _totalStatsCard = new Panel { BackColor = Color.FromArgb(238, 242, 255) };
            lblTodayTitle.Location = new Point(16, 14);
            lblTodayTitle.ForeColor = MiuiTheme.PrimaryDark;
            lblTodayCount.Location = new Point(16, 38);
            lblTodayCount.AutoSize = true;
            lblTodayCount.ForeColor = MiuiTheme.Primary;
            lblTotalTitle.Location = new Point(16, 14);
            lblTotalTitle.ForeColor = MiuiTheme.Accent;
            lblTotalCount.Location = new Point(16, 38);
            lblTotalCount.AutoSize = true;
            lblTotalCount.ForeColor = MiuiTheme.Accent;
            _todayStatsCard.Controls.Add(lblTodayTitle);
            _todayStatsCard.Controls.Add(lblTodayCount);
            _totalStatsCard.Controls.Add(lblTotalTitle);
            _totalStatsCard.Controls.Add(lblTotalCount);
            tabStats.Controls.Add(_todayStatsCard);
            tabStats.Controls.Add(_totalStatsCard);
            MiuiTheme.StyleCard(_todayStatsCard);
            MiuiTheme.StyleCard(_totalStatsCard);
            _todayStatsCard.BackColor = MiuiTheme.PrimaryLight;
            _totalStatsCard.BackColor = Color.FromArgb(238, 242, 255);
            tabStats.SizeChanged += (s, e) => LayoutStatsCards();
            LayoutStatsCards();
        }

        private void LayoutStatsCards()
        {
            if (_todayStatsCard == null || _totalStatsCard == null) return;
            var scale = Math.Max(1F, DeviceDpi / 96F);
            var margin = (int)Math.Round(18 * scale);
            var gap = (int)Math.Round(14 * scale);
            var height = (int)Math.Round(96 * scale);
            var width = Math.Max((int)Math.Round(150 * scale), (tabStats.ClientSize.Width - margin * 2 - gap) / 2);
            _todayStatsCard.Bounds = new Rectangle(margin, margin, width, height);
            _totalStatsCard.Bounds = new Rectangle(_todayStatsCard.Right + gap, margin, width, height);
            lblTodayCount.MaximumSize = new Size(Math.Max(1, width - ScaleUi(32)), 0);
            lblTotalCount.MaximumSize = new Size(Math.Max(1, width - ScaleUi(32)), 0);
        }

        private static void DrawCardBorder(object sender, PaintEventArgs e)
        {
            if (sender is not Control control || control.Width < 2 || control.Height < 2) return;
            using (var pen = new Pen(MiuiTheme.Divider))
                e.Graphics.DrawRectangle(pen, 0, 0, control.Width - 1, control.Height - 1);
        }

        private void ApplyModernIcons(int dpi = 0)
        {
            var scale = Math.Max(1F, (dpi > 0 ? dpi : DeviceDpi) / 96F);
            if (_brandIcon != null && Icon != null)
            {
                using (var source = Icon.ToBitmap())
                {
                    var size = ScaleIcon(38, scale);
                    ReplaceImage(_brandIcon, new Bitmap(source, new Size(size, size)));
                }
            }
            ReplaceImage(btnExportLog, SvgIconRenderer.Render(AppIcon.Export, MiuiTheme.Primary, ScaleIcon(18, scale)));
            ReplaceImage(btnPrint, SvgIconRenderer.Render(AppIcon.Print, Color.White, ScaleIcon(19, scale)));
            btnPrint.TextImageRelation = TextImageRelation.ImageBeforeText;
            btnPrint.ImageAlign = ContentAlignment.MiddleCenter;
            btnPrint.TextAlign = ContentAlignment.MiddleCenter;
            btnPrint.Padding = new Padding(ScaleUi(8), 0, ScaleUi(8), 0);
            ReplaceImage(btnRefreshPrinter, SvgIconRenderer.Render(AppIcon.Refresh, MiuiTheme.TextSecondary, ScaleIcon(16, scale)));
            ReplaceImage(btnClearSearch, SvgIconRenderer.Render(AppIcon.Clear, MiuiTheme.TextSecondary, ScaleIcon(15, scale)));
            ReplaceImage(btnClearHistory, SvgIconRenderer.Render(AppIcon.Clear, MiuiTheme.Error, ScaleIcon(15, scale)));
            ReplaceImage(btnExportHistory, SvgIconRenderer.Render(AppIcon.Export, MiuiTheme.TextSecondary, ScaleIcon(15, scale)));
            ReplaceImage(btnImportHistory, SvgIconRenderer.Render(AppIcon.Import, MiuiTheme.TextSecondary, ScaleIcon(15, scale)));
            ReplaceImage(btnReprintHistory, SvgIconRenderer.Render(AppIcon.Reprint, MiuiTheme.Primary, ScaleIcon(15, scale)));
            ReplaceImage(_btnToggleLog, SvgIconRenderer.Render(AppIcon.Log, MiuiTheme.TextSecondary, ScaleIcon(16, scale)));
            ReplaceImage(_btnAbout, SvgIconRenderer.Render(AppIcon.Info, MiuiTheme.TextSecondary, ScaleIcon(16, scale)));
            ReplaceImage(_btnSidebarToggle, SvgIconRenderer.Render(AppIcon.Menu, MiuiTheme.TextPrimary, ScaleIcon(18, scale)));
            ApplyNavigationIcons(_orderPagePanel?.Visible == true, scale);
        }

        private void ApplyNavigationIcons(bool orderPageActive, float scale = 0F)
        {
            if (scale <= 0F) scale = Math.Max(1F, DeviceDpi / 96F);
            ReplaceImage(_btnPrintPage, SvgIconRenderer.Render(AppIcon.Print, orderPageActive ? MiuiTheme.TextPrimary : Color.White, ScaleIcon(17, scale)));
            ReplaceImage(_btnOrderPage, SvgIconRenderer.Render(AppIcon.Orders, orderPageActive ? Color.White : MiuiTheme.TextPrimary, ScaleIcon(17, scale)));
        }

        private static int ScaleIcon(int logicalSize, float scale)
        {
            return Math.Max(logicalSize, (int)Math.Round(logicalSize * scale));
        }

        private static void ReplaceImage(Button button, Image image)
        {
            if (button == null) { image?.Dispose(); return; }
            var previous = button.Image;
            button.Image = image;
            button.Padding = new Padding(image == null ? ScaleUiStatic(button, 8) : ScaleUiStatic(button, 8), 0, ScaleUiStatic(button, 8), 0);
            previous?.Dispose();
        }

        private static int ScaleUiStatic(Control control, int value) =>
            (int)Math.Round(value * Math.Max(1F, control.DeviceDpi / 96F));

        private static void ReplaceImage(PictureBox picture, Image image)
        {
            var previous = picture.Image;
            picture.Image = image;
            previous?.Dispose();
        }

        private void DisposeModernImages()
        {
            foreach (var button in new[] { btnExportLog, btnPrint, btnRefreshPrinter, btnClearSearch, btnClearHistory, btnExportHistory, btnImportHistory, btnReprintHistory, _btnToggleLog, _btnAbout, _btnSidebarToggle, _btnPrintPage, _btnOrderPage })
            {
                if (button == null) continue;
                var image = button.Image;
                button.Image = null;
                image?.Dispose();
            }
            if (_brandIcon != null)
            {
                var image = _brandIcon.Image;
                _brandIcon.Image = null;
                image?.Dispose();
            }
        }

        private void InstallPreviewControl()
        {
            _chkPreview = new CheckBox
            {
                Text = _btService.IsPreviewAvailable ? "开启预览" : "预览不可用",
                AutoSize = true,
                Dock = DockStyle.Right,
                Padding = new Padding(10, 9, 10, 0),
                ForeColor = _btService.IsPreviewAvailable ? MiuiTheme.TextPrimary : MiuiTheme.TextSecondary,
                Enabled = _btService.IsPreviewAvailable
            };
            if (!_btService.IsPreviewAvailable)
                _toolTips.SetToolTip(_chkPreview, _btService.PreviewUnavailableReason);
            _chkPreview.CheckedChanged += Preview_CheckedChanged;
            titlePanel.Controls.Add(_chkPreview);
            titlePanel.Controls.SetChildIndex(_chkPreview, 0);
        }

        private void Preview_CheckedChanged(object sender, EventArgs e)
        {
            if (_chkPreview.Checked)
            {
                EnsurePreviewForm();
                _ = RefreshPreviewAsync();
            }
            else
            {
                ClosePreviewForm();
            }
        }

        private void EnsurePreviewForm()
        {
            if (_previewForm != null && !_previewForm.IsDisposed) return;
            _previewForm = new PreviewForm { Owner = this };
            _previewForm.ImageAspectRatioChanged += (sender, args) => DockPreviewForm();
            _previewForm.DpiChanged += (sender, args) => PostToUi(DockPreviewForm);
            _previewForm.PreviewClosed += (sender, args) =>
            {
                _previewForm = null;
                RestorePrePreviewBounds();
                if (_closingPreviewForm || _chkPreview == null) return;
                _chkPreview.Checked = false;
            };
            DockPreviewForm();
            _previewForm.Show();
        }

        private void ClosePreviewForm()
        {
            _previewRequestVersion++;
            if (_previewForm == null || _previewForm.IsDisposed) return;
            _closingPreviewForm = true;
            try { _previewForm.Close(); }
            finally
            {
                _previewForm = null;
                _closingPreviewForm = false;
                RestorePrePreviewBounds();
            }
        }

        private void DockPreviewForm()
        {
            if (_previewForm == null || _previewForm.IsDisposed || WindowState == FormWindowState.Minimized || _tilingPreview) return;
            var scale = Math.Max(1F, DeviceDpi / 96F);
            int S(int value) => (int)Math.Round(value * scale);
            var gap = S(8);
            var workingArea = Screen.FromControl(this).WorkingArea;
            var minHeight = Math.Min(workingArea.Height, S(240));
            var top = Math.Max(workingArea.Top, Math.Min(Top, workingArea.Bottom - minHeight));
            var height = Math.Min(workingArea.Height, Math.Max(minHeight, Math.Min(Height, workingArea.Bottom - top)));
            var ratio = Math.Max(0.35F, Math.Min(3F, _previewForm.ImageAspectRatio));
            var desiredWidth = (int)Math.Round(Math.Max(0, height - S(66)) * ratio + S(24));
            var width = Math.Min(workingArea.Width, Math.Max(Math.Min(workingArea.Width, S(280)), Math.Min(S(640), desiredWidth)));
            var rightSpace = workingArea.Right - Right - gap;
            var leftSpace = Left - workingArea.Left - gap;
            int left;
            if (rightSpace >= width) left = Right + gap;
            else if (leftSpace >= width) left = Left - width - gap;
            else
            {
                TileMainAndPreview(workingArea, width, gap);
                return;
            }
            left = Math.Max(workingArea.Left, Math.Min(left, workingArea.Right - width));
            var target = new Rectangle(left, top, width, height);
            if (target.IntersectsWith(Bounds))
            {
                TileMainAndPreview(workingArea, width, gap);
                return;
            }
            if (_previewForm.Bounds != target) _previewForm.Bounds = target;
        }

        private void TileMainAndPreview(Rectangle workingArea, int previewWidth, int gap)
        {
            _tilingPreview = true;
            try
            {
                if (!_prePreviewBounds.HasValue)
                {
                    _prePreviewWindowState = WindowState;
                    _prePreviewBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                    _prePreviewMinimumSize = MinimumSize;
                }
                if (WindowState != FormWindowState.Normal) WindowState = FormWindowState.Normal;
                MinimumSize = Size.Empty;
                var usableWidth = Math.Max(1, workingArea.Width - gap);
                previewWidth = Math.Max(1, Math.Min(previewWidth, usableWidth / 3));
                var mainWidth = Math.Max(1, usableWidth - previewWidth);
                Bounds = new Rectangle(workingArea.Left, workingArea.Top, mainWidth, workingArea.Height);
                var previewBounds = new Rectangle(Bounds.Right + gap, workingArea.Top, previewWidth, workingArea.Height);
                if (_previewForm.Bounds != previewBounds) _previewForm.Bounds = previewBounds;
            }
            finally
            {
                _tilingPreview = false;
            }
        }

        private void RestorePrePreviewBounds()
        {
            if (!_prePreviewBounds.HasValue || IsDisposed || Disposing) return;
            _tilingPreview = true;
            try
            {
                WindowState = FormWindowState.Normal;
                Bounds = _prePreviewBounds.Value;
                if (_prePreviewMinimumSize.HasValue) MinimumSize = _prePreviewMinimumSize.Value;
                if (_prePreviewWindowState == FormWindowState.Maximized) WindowState = FormWindowState.Maximized;
            }
            finally
            {
                _prePreviewBounds = null;
                _prePreviewWindowState = null;
                _prePreviewMinimumSize = null;
                _tilingPreview = false;
            }
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            const int wmDisplayChange = 0x007E;
            if (message.Msg == wmDisplayChange) PostToUi(DockPreviewForm);
        }

        private async Task RefreshPreviewAsync(Dictionary<string, string> successfulValues = null)
        {
            if (_chkPreview?.Checked != true || string.IsNullOrWhiteSpace(_selectedTemplatePath)) return;
            EnsurePreviewForm();
            if (_pendingPrintJobCount > 0)
            {
                _previewForm?.ShowLoading("打印队列处理中，稍后刷新");
                return;
            }
            var requestVersion = ++_previewRequestVersion;
            var templatePath = _selectedTemplatePath;
            var templateName = Path.GetFileName(templatePath);
            var templateId = GetCurrentTemplateId();
            var values = successfulValues;
            var source = "上一次成功打印";
            if (values == null)
            {
                var record = _history.GetLatestSuccessful(templateName, templatePath, templateId);
                values = record == null
                    ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(record.FieldValues ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
                if (record == null) source = "原模板预览";
            }
            _previewForm?.ShowLoading(source);
            string imagePath;
            string previewError = null;
            try
            {
                imagePath = await _btService.ExportPreviewAsync(templatePath, values);
            }
            catch (Exception ex)
            {
                LoggerService.Error("生成预览失败", ex);
                previewError = ex.GetBaseException().Message;
                imagePath = "";
            }
            if (requestVersion != _previewRequestVersion || _chkPreview?.Checked != true ||
                !string.Equals(templatePath, _selectedTemplatePath, StringComparison.OrdinalIgnoreCase))
                return;
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            {
                _previewForm?.ShowError(string.IsNullOrWhiteSpace(previewError)
                    ? "标签预览生成失败"
                    : $"标签预览生成失败：{previewError}");
                return;
            }
            try { _previewForm?.ShowPreview(imagePath, source); }
            catch (Exception ex)
            {
                LoggerService.Error("加载预览图片失败", ex);
                _previewForm?.ShowError("标签预览加载失败");
            }
        }

        private void InstallP2Controls()
        {
            var existingHistoryControls = historyPanel.Controls.Cast<Control>().ToArray();
            historyPanel.Controls.Clear();
            historyPanel.Height = ScaleUi(76);
            var historyToolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, AutoScroll = true, Padding = new Padding(2) };
            historyToolbar.Controls.AddRange(existingHistoryControls);
            historyPanel.Controls.Add(historyToolbar);
            _txtOperator = new TextBox { Location = new Point(695, 2), Size = new Size(80, 25), Text = Environment.UserName ?? "" };
            var lblOperator = new Label { Text = "操作员：", Location = new Point(638, 5), Size = new Size(55, 18) };
            _cmbRole = new ComboBox { Location = new Point(780, 2), Size = new Size(85, 25), DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbRole.Items.AddRange(new object[] { "Admin", "Supervisor", "Operator" });
            _cmbRole.SelectedIndex = 0;
            _cmbRole.SelectedIndexChanged += (s, e) => UpdateSession();
            _txtOperator.TextChanged += (s, e) => UpdateSession();
            _btnLogin = new Button { Text = "登录", Location = new Point(870, 1), Size = new Size(50, 24) };
            _btnLogin.Click += (s, e) => ShowLoginDialog();
            _btnPrevHistoryPage = new Button { Text = "上一页", Location = new Point(925, 1), Size = new Size(60, 24) };
            _btnNextHistoryPage = new Button { Text = "下一页", Location = new Point(990, 1), Size = new Size(60, 24) };
            _lblHistoryPage = new Label { Text = "第 1 页", Location = new Point(1055, 5), Size = new Size(80, 18) };
            _cmbHistoryStatus = new ComboBox { Location = new Point(1140, 2), Size = new Size(90, 25), DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbHistoryStatus.Items.AddRange(new object[] { "全部状态", "PASS", "FAIL", "REPRINT_PASS", "REPRINT_FAIL" });
            _cmbHistoryStatus.SelectedIndex = 0;
            _txtHistoryDate = new TextBox { Location = new Point(1180, 2), Size = new Size(90, 25), PlaceholderText = "yyyy-MM-dd" };
            _btnPrevHistoryPage.Click += (s, e) => { if (_historyPageIndex > 0) { _historyPageIndex--; LoadHistory(); } };
            _btnNextHistoryPage.Click += (s, e) => { _historyPageIndex++; LoadHistory(); };
            _cmbHistoryStatus.SelectedIndexChanged += (s, e) => { _historyPageIndex = 0; LoadHistory(); };
            _txtHistoryDate.TextChanged += (s, e) => { _historyPageIndex = 0; _historySearchTimer.Stop(); _historySearchTimer.Start(); };
            historyToolbar.Controls.AddRange(new Control[] { lblOperator, _txtOperator, _cmbRole, _btnLogin, _btnPrevHistoryPage, _btnNextHistoryPage, _lblHistoryPage, _cmbHistoryStatus, _txtHistoryDate });
            MiuiTheme.StyleLabel(lblOperator);
            MiuiTheme.StyleLabel(_lblHistoryPage, true);
            MiuiTheme.StyleTextBox(_txtOperator);
            MiuiTheme.StyleButton(_btnPrevHistoryPage);
            MiuiTheme.StyleButton(_btnNextHistoryPage);
            MiuiTheme.StyleButton(_btnLogin);
            MiuiTheme.StyleTextBox(_txtHistoryDate);
            UpdateSession();
        }

        private void SilentLogin()
        {
            ApplyAccount(_accountManager.DefaultAccount);
        }

        private void ApplyAccount(UserAccount account)
        {
            if (account == null) return;
            _txtOperator.Text = account.UserName;
            _cmbRole.SelectedItem = account.Role;
            UpdateSession();
            AuditLogger.Append(GetOperatorName(), "Login", $"role={account.Role}");
        }

        private void ShowLoginDialog()
        {
            using (var form = new Form())
            {
                form.Text = "账户登录";
                form.Size = new Size(340, 190);
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterParent;
                form.MaximizeBox = false;
                form.MinimizeBox = false;
                var lblUser = new Label { Text = "账号：", Location = new Point(14, 18), Size = new Size(60, 22) };
                var txtUser = new TextBox { Location = new Point(80, 15), Size = new Size(220, 25), Text = "superadmin" };
                var lblPassword = new Label { Text = "密码：", Location = new Point(14, 55), Size = new Size(60, 22) };
                var txtPassword = new TextBox { Location = new Point(80, 52), Size = new Size(220, 25), UseSystemPasswordChar = true, Text = "admin" };
                var ok = new Button { Text = "登录", Location = new Point(145, 105), Size = new Size(70, 28), DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "取消", Location = new Point(230, 105), Size = new Size(70, 28), DialogResult = DialogResult.Cancel };
                ok.Click += (s, e) =>
                {
                    if (!_accountManager.TryLogin(txtUser.Text.Trim(), txtPassword.Text, out var account))
                    {
                        _dialogs.ShowWarning(form, "账号或密码错误。", "登录失败");
                        form.DialogResult = DialogResult.None;
                        return;
                    }
                    ApplyAccount(account);
                };
                form.Controls.AddRange(new Control[] { lblUser, txtUser, lblPassword, txtPassword, ok, cancel });
                form.AcceptButton = ok;
                form.CancelButton = cancel;
                StyleDialog(form, ok, cancel);
                form.ShowDialog(this);
            }
        }

        private void UpdateSession()
        {
            _session.OperatorName = GetOperatorName();
            _session.Role = _cmbRole?.SelectedItem?.ToString() ?? "Admin";
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            LoadConfig(_configFile);
            RebuildInputFields();
            RefreshOrderFilters();
            SetStatus("正在初始化...");
        }

        private void InstallOrderSidebar()
        {
            var collapsedWidth = SidebarCollapsedWidth;
            var orderSelectorHeight = ScaleUi(64);
            var printControls = Controls.Cast<Control>()
                .Where(control => control != titlePanel && control != groupBoxLog && control != statusStrip)
                .ToDictionary(control => control, control => control.Bounds);
            ClientSize = new Size(ClientSize.Width + collapsedWidth, ClientSize.Height);
            MinimumSize = new Size(MinimumSize.Width + collapsedWidth, MinimumSize.Height);
            foreach (var item in printControls)
            {
                var bounds = item.Value;
                var top = bounds.Top >= 42 ? bounds.Top + orderSelectorHeight : bounds.Top;
                var height = item.Key == tabBottom ? Math.Max(80, bounds.Height - orderSelectorHeight) : bounds.Height;
                item.Key.Bounds = new Rectangle(bounds.Left + collapsedWidth, top, bounds.Width, height);
            }

            _navPanel = new Panel
            {
                Location = new Point(0, titlePanel.Bottom),
                Size = new Size(collapsedWidth, WorkspaceBottom - titlePanel.Bottom),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left,
                BackColor = MiuiTheme.Sidebar
            };
            _btnSidebarToggle = new Button { Text = "" };
            _btnSidebarToggle.Click += (s, e) => SetSidebarExpanded(!_sidebarExpanded);
            _btnSidebarToggle.Paint += SidebarToggle_Paint;
            _btnPrintPage = new Button { Text = "打印页面", Visible = false };
            _btnOrderPage = new Button { Text = "订单管理", Visible = false };
            _btnPrintPage.Click += (s, e) => { ShowPrintPage(); SetSidebarExpanded(false); };
            _btnOrderPage.Click += (s, e) => { ShowOrderManagementPage(); SetSidebarExpanded(false); };
            _navPanel.Controls.AddRange(new Control[] { _btnSidebarToggle, _btnPrintPage, _btnOrderPage });
            Controls.Add(_navPanel);
            _navPanel.BringToFront();
            MiuiTheme.StyleButton(_btnSidebarToggle);
            MiuiTheme.StyleNavigationButton(_btnPrintPage, true);
            MiuiTheme.StyleNavigationButton(_btnOrderPage, false);
            ApplyNavigationIcons(false);
            LayoutSidebar();

            _printOrderPanel = new Panel
            {
                Location = new Point(collapsedWidth + ScaleUi(10), titlePanel.Bottom + ScaleUi(4)),
                Size = new Size(ClientSize.Width - collapsedWidth - ScaleUi(20), ScaleUi(34)),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = BackColor
            };
            _printOrderPanel.Height = ScaleUi(78);
            var printOrderLabel = new Label
            {
                Text = "当前订单：",
                Location = new Point(0, ScaleUi(8)),
                Size = new Size(ScaleUi(72), ScaleUi(20))
            };
            _cmbPrintCustomer = AddPrintOrderCombo("客户", 75, 4, 150);
            _cmbPrintModel = AddPrintOrderCombo("机型", 235, 4, 150);
            _cmbPrintColor = AddPrintOrderCombo("颜色", 395, 4, 150);
            _cmbPrintOrderNumber = AddPrintOrderCombo("订单号", 555, 4, 150);
            _cmbPrintCustomer.SelectedIndexChanged += (s, e) => { if (!_loadingPrintOrderFilters) RefreshPrintOrderSelector(PrintOrderFilterLevel.Customer, false); };
            _cmbPrintModel.SelectedIndexChanged += (s, e) => { if (!_loadingPrintOrderFilters) RefreshPrintOrderSelector(PrintOrderFilterLevel.Model, false); };
            _cmbPrintColor.SelectedIndexChanged += (s, e) => { if (!_loadingPrintOrderFilters) RefreshPrintOrderSelector(PrintOrderFilterLevel.Color, false); };
            _cmbPrintOrderNumber.SelectedIndexChanged += (s, e) => ApplyPrintOrderSelection();
            _printOrderPanel.Controls.Add(printOrderLabel);
            _lblEffectiveSummary = new Label
            {
                Text = "当前生效设置：未选择订单",
                Location = new Point(0, ScaleUi(58)),
                Size = new Size(_printOrderPanel.Width, ScaleUi(18)),
                AutoEllipsis = true
            };
            _printOrderPanel.Controls.Add(_lblEffectiveSummary);
            Controls.Add(_printOrderPanel);
            MiuiTheme.StyleLabel(printOrderLabel);
            MiuiTheme.StyleLabel(_lblEffectiveSummary, true);

            _orderPagePanel = new Panel
            {
                Location = new Point(collapsedWidth, titlePanel.Bottom),
                Size = new Size(ClientSize.Width - collapsedWidth, WorkspaceBottom - titlePanel.Bottom),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Visible = false,
                BackColor = BackColor
            };
            _orderPanel = new GroupBox
            {
                Text = "包装 MES 订单",
                Dock = DockStyle.Left,
                Width = ScaleUi(250),
                Padding = new Padding(ScaleUi(12))
            };
            _orderContentPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(ScaleUi(12)), AutoScroll = true };
            var y = 28;
            _btnAddOrder = new Button
            {
                Text = "添加订单",
                Location = new Point(ScaleUi(12), ScaleUi(y)),
                Size = new Size(ScaleUi(200), ScaleUi(30))
            };
            _btnAddOrder.Click += (s, e) => ShowAddOrderPage();
            y += 46;

            _cmbOrderCustomer = AddOrderCombo("客户", 12, y); y += 58;
            _cmbOrderModel = AddOrderCombo("机型", 12, y); y += 58;
            _cmbOrderColor = AddOrderCombo("颜色", 12, y); y += 58;
            _cmbOrderNumber = AddOrderCombo("订单号", 12, y);
            _cmbOrderCustomer.SelectedIndexChanged += (s, e) => { if (!_loadingOrderFilters) RefreshOrderFilters(OrderFilterLevel.Customer); };
            _cmbOrderModel.SelectedIndexChanged += (s, e) => { if (!_loadingOrderFilters) RefreshOrderFilters(OrderFilterLevel.Model); };
            _cmbOrderColor.SelectedIndexChanged += (s, e) => { if (!_loadingOrderFilters) RefreshOrderFilters(OrderFilterLevel.Color); };
            _cmbOrderNumber.SelectedIndexChanged += (s, e) => { if (!_loadingOrderFilters) ApplySelectedOrder(); };

            _orderPanel.Controls.Add(_btnAddOrder);
            _orderPagePanel.Controls.Add(_orderContentPanel);
            _orderPagePanel.Controls.Add(_orderPanel);
            _orderPanel.Visible = false;
            Controls.Add(_orderPagePanel);
            MiuiTheme.StyleGroupBox(_orderPanel);
            MiuiTheme.StyleButton(_btnAddOrder, true);
            foreach (var control in new Control[]
            {
                btnSaveConfig, btnLoadConfig, btnEditDataSources, btnLoadLocalData, btnDiagnostics,
                chkUseLocalData, chkLengthValidation, chkDuplicateValidation, btnGlobalLength, lblLocalData,
                lblTemplateDir, txtTemplateDir, btnBrowseDir
            })
                control.Visible = false;
            RebuildPrintPageLayout();
            RefreshPrintOrderSelector(PrintOrderFilterLevel.All, false);
        }

        private ComboBox AddPrintOrderCombo(string labelText, int x, int y, int width)
        {
            var label = new Label
            {
                Text = labelText + "：",
                Location = new Point(ScaleUi(x), ScaleUi(y)),
                Size = new Size(ScaleUi(width), ScaleUi(18))
            };
            var combo = new ComboBox
            {
                Location = new Point(ScaleUi(x), ScaleUi(y + 22)),
                Size = new Size(ScaleUi(width), ScaleUi(25)),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _printOrderPanel.Controls.Add(label);
            _printOrderPanel.Controls.Add(combo);
            MiuiTheme.StyleLabel(label);
            return combo;
        }

        private void RebuildPrintPageLayout()
        {
            if (_printOrderPanel == null) return;
            var scale = Math.Max(1F, DeviceDpi / 96F);
            int S(int value) => (int)Math.Round(value * scale);
            var left = _printOrderPanel.Left;
            var width = Math.Max(S(500), ClientSize.Width - left - S(10));
            _printOrderPanel.Width = width;
            var orderCaption = _printOrderPanel.Controls.OfType<Label>().FirstOrDefault(item => item.Text == "当前订单：");
            if (orderCaption != null) orderCaption.Bounds = new Rectangle(S(12), S(8), width - S(24), S(22));
            var comboHeight = Math.Max(_cmbPrintCustomer.PreferredHeight, S(25));
            var comboGap = S(10);
            var combos = new[] { _cmbPrintCustomer, _cmbPrintModel, _cmbPrintColor, _cmbPrintOrderNumber };
            var columns = width >= S(760) ? 4 : 2;
            var comboWidth = Math.Max(S(150), (width - S(24) - comboGap * (columns - 1)) / columns);
            var firstComboTop = S(54);
            for (var index = 0; index < combos.Length; index++)
            {
                var row = index / columns;
                var column = index % columns;
                combos[index].Bounds = new Rectangle(
                    S(12) + column * (comboWidth + comboGap),
                    firstComboTop + row * (comboHeight + S(34)),
                    comboWidth,
                    comboHeight);
            }
            SetPrintOrderLabelBounds("客户：", _cmbPrintCustomer);
            SetPrintOrderLabelBounds("机型：", _cmbPrintModel);
            SetPrintOrderLabelBounds("颜色：", _cmbPrintColor);
            SetPrintOrderLabelBounds("订单号：", _cmbPrintOrderNumber);
            if (_lblEffectiveSummary != null)
            {
                var comboBottom = combos.Max(combo => combo.Bottom);
                _lblEffectiveSummary.Bounds = new Rectangle(S(12), comboBottom + S(10), width - S(24), S(22));
                _printOrderPanel.Height = _lblEffectiveSummary.Bottom + S(10);
            }

            cmbTemplate.Location = new Point(left, _printOrderPanel.Bottom + S(8));
            cmbTemplate.Size = new Size(width, Math.Max(cmbTemplate.PreferredHeight, S(25)));
            lblSelectedTemplate.Location = new Point(left, cmbTemplate.Bottom + S(4));
            lblSelectedTemplate.Size = new Size(Math.Min(S(420), width), S(18));

            lblPrinter.Location = new Point(left, lblSelectedTemplate.Bottom + S(16));
            cmbPrinter.Location = new Point(left + S(58), lblSelectedTemplate.Bottom + S(12));
            btnRefreshPrinter.Size = new Size(
                Math.Max(S(72), btnRefreshPrinter.GetPreferredSize(new Size(0, S(28))).Width + S(4)),
                Math.Max(S(28), btnRefreshPrinter.GetPreferredSize(new Size(0, S(28))).Height));
            btnRefreshPrinter.Location = new Point(left + width - S(95) - btnRefreshPrinter.Width, lblSelectedTemplate.Bottom + S(11));
            lblCopies.Location = new Point(left + width - S(86), lblSelectedTemplate.Bottom + S(15));
            numCopies.Location = new Point(left + width - S(45), lblSelectedTemplate.Bottom + S(12));
            cmbPrinter.Size = new Size(Math.Max(S(180), btnRefreshPrinter.Left - cmbPrinter.Left - S(6)), Math.Max(cmbPrinter.PreferredHeight, S(25)));

            inputPanel.Location = new Point(left, cmbPrinter.Bottom + S(10));
            inputPanel.Width = width;
            btnPrint.Location = new Point(left, inputPanel.Bottom + S(8));
            btnPrint.Width = width;
            tabBottom.Location = new Point(left, btnPrint.Bottom + S(8));
            tabBottom.Size = new Size(width, Math.Max(1, WorkspaceBottom - tabBottom.Top - S(8)));
        }

        private void SetPrintOrderLabelBounds(string text, ComboBox combo)
        {
            var label = _printOrderPanel.Controls.OfType<Label>().FirstOrDefault(item => item.Text == text);
            if (label == null) return;
            label.Left = combo.Left;
            label.Top = combo.Top - ScaleUi(24);
            label.Width = combo.Width;
            label.Height = ScaleUi(20);
        }

        private void SetSidebarExpanded(bool expanded)
        {
            _sidebarExpanded = expanded;
            _btnPrintPage.Visible = expanded;
            _btnOrderPage.Visible = expanded;
            LayoutSidebar();
            RebuildPrintPageLayout();
            _navPanel.BringToFront();
        }

        private void SidebarToggle_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if (_btnSidebarToggle.Image == null)
                ReplaceImage(_btnSidebarToggle, SvgIconRenderer.Render(AppIcon.Menu, MiuiTheme.TextPrimary, ScaleIcon(18, Math.Max(1F, DeviceDpi / 96F))));
        }

        private void ShowPrintPage()
        {
            if (_orderPagePanel.Visible && !ConfirmOrderEditorChanges()) return;
            if (_activeOrderTemplate != null && !ResolveTemplateUpdate(_activeOrder, _activeOrderTemplate)) return;
            if (!SaveSelectedOrderTemplateDraft()) return;
            _orderPagePanel.Visible = false;
            _printOrderPanel.Visible = true;
            _printOrderPanel.BringToFront();
            if (_chkPreview != null) _chkPreview.Visible = true;
            MiuiTheme.StyleNavigationButton(_btnPrintPage, true);
            MiuiTheme.StyleNavigationButton(_btnOrderPage, false);
            ApplyNavigationIcons(false);
        }

        private void ShowOrderManagementPage()
        {
            if (_chkPreview != null)
            {
                _chkPreview.Checked = false;
                _chkPreview.Visible = false;
            }
            _printOrderPanel.Visible = false;
            _orderPagePanel.Visible = true;
            _orderPagePanel.BringToFront();
            MiuiTheme.StyleNavigationButton(_btnOrderPage, true);
            MiuiTheme.StyleNavigationButton(_btnPrintPage, false);
            ApplyNavigationIcons(true);
            if (_activeOrder != null)
            {
                if (_orderDataSourcesGrid == null || (_editingOrder != null &&
                    !string.Equals(_editingOrder.Key, _activeOrder.Key, StringComparison.OrdinalIgnoreCase)))
                    ShowOrderSettingsPage(_activeOrder);
                return;
            }
            if (HasCompleteOrderSelection())
            {
                var order = _orders.Find(_cmbOrderCustomer.SelectedItem?.ToString(), _cmbOrderModel.SelectedItem?.ToString(),
                    _cmbOrderColor.SelectedItem?.ToString(), _cmbOrderNumber.SelectedItem?.ToString());
                if (order != null) ShowOrderSettingsPage(order);
                else BuildOrderEditor(null);
            }
            else BuildOrderEditor(null);
        }

        private enum PrintOrderFilterLevel { All, Customer, Model, Color }

        private void RefreshPrintOrderSelector(PrintOrderFilterLevel level = PrintOrderFilterLevel.All, bool syncActiveOrder = true)
        {
            if (_cmbPrintOrderNumber == null) return;
            _loadingPrintOrderFilters = true;
            try
            {
                var selectedCustomer = _cmbPrintCustomer.SelectedItem?.ToString() ?? "";
                var selectedModel = _cmbPrintModel.SelectedItem?.ToString() ?? "";
                var selectedColor = _cmbPrintColor.SelectedItem?.ToString() ?? "";
                var selectedOrder = _cmbPrintOrderNumber.SelectedItem?.ToString() ?? "";
                if (level == PrintOrderFilterLevel.All)
                    FillCombo(_cmbPrintCustomer, _orders.Orders.Select(order => order.Customer), selectedCustomer);
                if (level <= PrintOrderFilterLevel.Customer)
                    FillCombo(_cmbPrintModel, _orders.Orders.Where(order => IsSelectedOrEmpty(_cmbPrintCustomer, order.Customer)).Select(order => order.ProductModel), selectedModel);
                if (level <= PrintOrderFilterLevel.Model)
                    FillCombo(_cmbPrintColor, _orders.Orders.Where(order => IsSelectedOrEmpty(_cmbPrintCustomer, order.Customer) && IsSelectedOrEmpty(_cmbPrintModel, order.ProductModel)).Select(order => order.Color), selectedColor);
                FillCombo(_cmbPrintOrderNumber, _orders.Orders.Where(order =>
                    IsSelectedOrEmpty(_cmbPrintCustomer, order.Customer) &&
                    IsSelectedOrEmpty(_cmbPrintModel, order.ProductModel) &&
                    IsSelectedOrEmpty(_cmbPrintColor, order.Color)).Select(order => order.OrderNumber), selectedOrder);
                if (syncActiveOrder && level == PrintOrderFilterLevel.All && _activeOrder != null) SelectPrintOrder(_activeOrder);
            }
            finally { _loadingPrintOrderFilters = false; }
            if (!syncActiveOrder && HasCompletePrintOrderSelection()) ApplyPrintOrderSelection();
            else if (!syncActiveOrder)
            {
                _activeOrder = null;
                _activeOrderTemplate = null;
                _selectedTemplatePath = "";
                cmbTemplate.Items.Clear();
                lblSelectedTemplate.Text = "请选择完整订单";
                ResetTemplateState();
                LoadHistory();
                RefreshStats();
            }
        }

        private bool HasCompletePrintOrderSelection()
        {
            return _cmbPrintCustomer?.SelectedItem != null &&
                   _cmbPrintModel?.SelectedItem != null &&
                   _cmbPrintColor?.SelectedItem != null &&
                   _cmbPrintOrderNumber?.SelectedItem != null;
        }

        private void ApplyPrintOrderSelection()
        {
            if (_loadingPrintOrderFilters) return;
            var order = _orders.Find(_cmbPrintCustomer.SelectedItem?.ToString(), _cmbPrintModel.SelectedItem?.ToString(),
                _cmbPrintColor.SelectedItem?.ToString(), _cmbPrintOrderNumber.SelectedItem?.ToString());
            if (order == null) return;
            var previousOrder = _activeOrder;
            SelectOrder(order);
            if (ApplyOrder(order)) return;
            _loadingPrintOrderFilters = true;
            try
            {
                if (previousOrder != null) { SelectOrder(previousOrder); SelectPrintOrder(previousOrder); }
                else ClearOrderSelection();
            }
            finally { _loadingPrintOrderFilters = false; }
        }

        private void SelectPrintOrder(PackagingOrder order)
        {
            if (order == null) return;
            FillCombo(_cmbPrintCustomer, _orders.Orders.Select(item => item.Customer), order.Customer);
            _cmbPrintCustomer.SelectedItem = order.Customer;
            FillCombo(_cmbPrintModel, _orders.Orders.Where(item => string.Equals(item.Customer, order.Customer, StringComparison.OrdinalIgnoreCase)).Select(item => item.ProductModel), order.ProductModel);
            _cmbPrintModel.SelectedItem = order.ProductModel;
            FillCombo(_cmbPrintColor, _orders.Orders.Where(item => string.Equals(item.Customer, order.Customer, StringComparison.OrdinalIgnoreCase) && string.Equals(item.ProductModel, order.ProductModel, StringComparison.OrdinalIgnoreCase)).Select(item => item.Color), order.Color);
            _cmbPrintColor.SelectedItem = order.Color;
            FillCombo(_cmbPrintOrderNumber, _orders.Orders.Where(item => string.Equals(item.Customer, order.Customer, StringComparison.OrdinalIgnoreCase) && string.Equals(item.ProductModel, order.ProductModel, StringComparison.OrdinalIgnoreCase) && string.Equals(item.Color, order.Color, StringComparison.OrdinalIgnoreCase)).Select(item => item.OrderNumber), order.OrderNumber);
            _cmbPrintOrderNumber.SelectedItem = order.OrderNumber;
        }

        private void ClearOrderSelection()
        {
            foreach (var combo in new[] { _cmbOrderCustomer, _cmbOrderModel, _cmbOrderColor, _cmbOrderNumber })
                if (combo != null) combo.SelectedIndex = -1;
            UpdateEffectiveSummary();
        }

        private void UpdateEffectiveSummary()
        {
            if (_lblEffectiveSummary == null) return;
            var enabledCount = _dataSources?.Count(source => source.Enabled) ?? 0;
            var validation = $"本地:{(_useLocalDataValidation ? "开" : "关")} 重复:{(_duplicateValidationEnabled ? "开" : "关")} 长度:{(_lengthValidationEnabled ? "开" : "关")}";
            _lblEffectiveSummary.Text = $"当前生效设置：订单={_activeOrder?.DisplayName ?? "未选择"} | 模板={Path.GetFileName(_selectedTemplatePath)} | 打印机={cmbPrinter.SelectedItem} | 份数={numCopies.Value} | 字段={enabledCount} | {validation}";
        }

        private ComboBox AddOrderCombo(string labelText, int x, int y)
        {
            var label = new Label
            {
                Text = labelText + "：",
                Location = new Point(ScaleUi(x), ScaleUi(y - 2)),
                Size = new Size(ScaleUi(200), ScaleUi(18))
            };
            var combo = new ComboBox
            {
                Location = new Point(ScaleUi(x), ScaleUi(y + 22)),
                Size = new Size(ScaleUi(200), ScaleUi(25)),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _orderPanel.Controls.Add(label);
            _orderPanel.Controls.Add(combo);
            MiuiTheme.StyleLabel(label);
            return combo;
        }

        private enum OrderFilterLevel { All, Customer, Model, Color }

        private void RefreshOrderFilters(OrderFilterLevel level = OrderFilterLevel.All, bool applySelection = true)
        {
            _loadingOrderFilters = true;
            try
            {
                var selectedCustomer = _cmbOrderCustomer?.SelectedItem?.ToString() ?? "";
                var selectedModel = _cmbOrderModel?.SelectedItem?.ToString() ?? "";
                var selectedColor = _cmbOrderColor?.SelectedItem?.ToString() ?? "";
                if (level == OrderFilterLevel.All)
                    FillCombo(_cmbOrderCustomer, _orders.Orders.Select(order => order.Customer), selectedCustomer);
                if (level <= OrderFilterLevel.Customer)
                    FillCombo(_cmbOrderModel, _orders.Orders.Where(order => IsSelectedOrEmpty(_cmbOrderCustomer, order.Customer)).Select(order => order.ProductModel), selectedModel);
                if (level <= OrderFilterLevel.Model)
                    FillCombo(_cmbOrderColor, _orders.Orders.Where(order => IsSelectedOrEmpty(_cmbOrderCustomer, order.Customer) && IsSelectedOrEmpty(_cmbOrderModel, order.ProductModel)).Select(order => order.Color), selectedColor);
                FillCombo(_cmbOrderNumber, _orders.Orders.Where(order =>
                    IsSelectedOrEmpty(_cmbOrderCustomer, order.Customer) &&
                    IsSelectedOrEmpty(_cmbOrderModel, order.ProductModel) &&
                    IsSelectedOrEmpty(_cmbOrderColor, order.Color)).Select(order => order.OrderNumber), _cmbOrderNumber?.SelectedItem?.ToString() ?? "");
            }
            finally
            {
                _loadingOrderFilters = false;
            }
            RefreshPrintOrderSelector();
            if (applySelection && HasCompleteOrderSelection()) ApplySelectedOrder();
        }

        private bool HasCompleteOrderSelection()
        {
            return _cmbOrderCustomer?.SelectedItem != null &&
                   _cmbOrderModel?.SelectedItem != null &&
                   _cmbOrderColor?.SelectedItem != null &&
                   _cmbOrderNumber?.SelectedItem != null;
        }

        private static bool IsSelectedOrEmpty(ComboBox combo, string value)
        {
            var selected = combo?.SelectedItem?.ToString() ?? "";
            return string.IsNullOrEmpty(selected) || string.Equals(selected, value, StringComparison.OrdinalIgnoreCase);
        }

        private static void FillCombo(ComboBox combo, IEnumerable<string> values, string selected)
        {
            if (combo == null) return;
            combo.Items.Clear();
            foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, NaturalStringComparer.Instance))
                combo.Items.Add(value);
            if (!string.IsNullOrEmpty(selected) && combo.Items.Contains(selected))
                combo.SelectedItem = selected;
            else if (combo.Items.Count == 1)
                combo.SelectedIndex = 0;
        }

        private void SelectOrder(PackagingOrder order)
        {
            if (order == null) return;
            _loadingOrderFilters = true;
            try
            {
                FillCombo(_cmbOrderCustomer, _orders.Orders.Select(item => item.Customer), order.Customer);
                _cmbOrderCustomer.SelectedItem = order.Customer;
                FillCombo(_cmbOrderModel, _orders.Orders.Where(item => string.Equals(item.Customer, order.Customer, StringComparison.OrdinalIgnoreCase)).Select(item => item.ProductModel), order.ProductModel);
                _cmbOrderModel.SelectedItem = order.ProductModel;
                FillCombo(_cmbOrderColor, _orders.Orders.Where(item => string.Equals(item.Customer, order.Customer, StringComparison.OrdinalIgnoreCase) && string.Equals(item.ProductModel, order.ProductModel, StringComparison.OrdinalIgnoreCase)).Select(item => item.Color), order.Color);
                _cmbOrderColor.SelectedItem = order.Color;
                FillCombo(_cmbOrderNumber, _orders.Orders.Where(item => string.Equals(item.Customer, order.Customer, StringComparison.OrdinalIgnoreCase) && string.Equals(item.ProductModel, order.ProductModel, StringComparison.OrdinalIgnoreCase) && string.Equals(item.Color, order.Color, StringComparison.OrdinalIgnoreCase)).Select(item => item.OrderNumber), order.OrderNumber);
                _cmbOrderNumber.SelectedItem = order.OrderNumber;
            }
            finally
            {
                _loadingOrderFilters = false;
            }
        }

        private void ApplySelectedOrder()
        {
            if (_loadingOrderFilters) return;
            var previousOrder = _activeOrder;
            var order = _orders.Find(_cmbOrderCustomer?.SelectedItem?.ToString(), _cmbOrderModel?.SelectedItem?.ToString(), _cmbOrderColor?.SelectedItem?.ToString(), _cmbOrderNumber?.SelectedItem?.ToString());
            if (order == null)
            {
                if (previousOrder != null) SelectOrder(previousOrder);
                else ClearOrderSelection();
                return;
            }
            if (order.Templates == null || order.Templates.Count == 0)
            {
                MessageBox.Show(this, "订单未配置模板，请重新添加订单。", "订单模板", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                if (previousOrder != null) SelectOrder(previousOrder);
                else ClearOrderSelection();
                return;
            }
            if (!ApplyOrder(order))
            {
                if (_orderPagePanel.Visible)
                {
                    ShowOrderSettingsPage(order);
                    return;
                }
                if (previousOrder != null) SelectOrder(previousOrder);
                else ClearOrderSelection();
            }
            else if (_orderPagePanel.Visible)
            {
                ShowOrderSettingsPage(order);
            }
        }

        private bool ApplyOrder(PackagingOrder order, bool saveCurrentSettings = true, string preferredTemplateId = null)
        {
            if (saveCurrentSettings && !string.IsNullOrEmpty(_selectedTemplatePath)) SaveCurrentTemplateSettings();
            var template = order.Templates.FirstOrDefault(item => string.Equals(item.Id, preferredTemplateId, StringComparison.OrdinalIgnoreCase) && File.Exists(item.SourcePath))
                ?? order.Templates.FirstOrDefault(item => File.Exists(item.SourcePath))
                ?? order.Templates.FirstOrDefault();
            if (template == null || string.IsNullOrEmpty(template.SourcePath) || !File.Exists(template.SourcePath))
            { MessageBox.Show(this, "订单模板绝对路径无效，请在订单管理页面重新选择模板。", "订单模板", MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; }
            if (!ResolveTemplateUpdate(order, template)) return false;
            _activeOrder = order;
            _isLoadingConfig = true;
            _loadingOrderTemplate = true;
            try
            {
                _selectedTemplatePath = template.SourcePath;
                _templatesFolder = Path.GetDirectoryName(template.SourcePath) ?? "";
                txtTemplateDir.Text = _templatesFolder;
                cmbTemplate.Items.Clear();
                foreach (var orderTemplate in order.Templates.Where(item => File.Exists(item.SourcePath)))
                    cmbTemplate.Items.Add(new TemplateItem(orderTemplate.DisplayName, orderTemplate.SourcePath));
                var match = cmbTemplate.Items.Cast<TemplateItem>().FirstOrDefault(item => string.Equals(item.FullPath, template.SourcePath, StringComparison.OrdinalIgnoreCase));
                if (match != null) cmbTemplate.SelectedItem = match;
                lblSelectedTemplate.Text = template.DisplayName;
                _toolTips.SetToolTip(lblSelectedTemplate, template.SourcePath);
                _activeOrderTemplate = template;
            }
            finally
            {
                _loadingOrderTemplate = false;
                _isLoadingConfig = false;
            }
            ApplyTemplateSettings(template.Settings ?? new TemplateSettings());
            SyncPrintOrderSelection(order);
            UpdateEffectiveSummary();
            LoadHistory();
            RefreshStats();
            AddLog($"已选择订单: {order.DisplayName}", "INFO");
            if (!_isInitializing && _chkPreview?.Checked == true) _ = RefreshPreviewAsync();
            return true;
        }

        private bool ResolveTemplateUpdate(PackagingOrder order, OrderTemplate template)
        {
            if (template == null) return false;
            var status = _orders.GetSourceUpdateStatus(template);
            if (status == TemplateUpdateStatus.Unchanged) return true;
            if (status == TemplateUpdateStatus.CheckFailed)
            {
                MessageBox.Show(this, "订单模板绝对路径暂时无法读取，请检查文件位置和访问权限。", "模板读取失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            var originalTemplate = CloneOrderTemplate(template);
            try
            {
                if (!ReconcileUpdatedTemplateDataSources(template)) return false;
                _orders.RefreshSourceTemplate(order, template);
            }
            catch (Exception ex)
            {
                template.SourceLastWriteTimeUtcTicks = originalTemplate.SourceLastWriteTimeUtcTicks;
                template.SourceLength = originalTemplate.SourceLength;
                template.SourceSha256 = originalTemplate.SourceSha256;
                template.ArchivedPath = originalTemplate.ArchivedPath;
                template.Settings = originalTemplate.Settings;
                LoggerService.Error("更新订单模板失败", ex);
                MessageBox.Show(this, $"保存订单模板失败：{ex.Message}", "模板更新", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            if (ReferenceEquals(template, _activeOrderTemplate))
            {
                try { ApplyTemplateSettings(template.Settings ?? new TemplateSettings()); }
                catch (Exception ex) { LoggerService.Error("应用订单模板设置失败", ex); }
            }
            AddLog($"已刷新订单模板数据源: {template.DisplayName}", "SUCCESS");
            return true;
        }

        private bool ReconcileUpdatedTemplateDataSources(OrderTemplate template)
        {
            if (!_btService.IsConnected)
            {
                MessageBox.Show(this, "BarTender 未连接，暂时无法读取模板的最新数据源。", "模板数据源刷新",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            var fields = _btService.GetTemplateDataSources(template.SourcePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(field => field, NaturalStringComparer.Instance)
                .ToList();
            if (fields.Count == 0)
            {
                MessageBox.Show(this, "未读取到模板数据源，已保留当前订单设置。", "模板数据源刷新",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            var current = template.Settings?.DataSources ?? new List<DataSourceItem>();
            if (fields.Count == current.Count && fields.All(field => current.Any(source => string.Equals(source.Field, field, StringComparison.OrdinalIgnoreCase)))) return true;
            var oldFields = new HashSet<string>(current.Select(source => source.Field).Where(field => !string.IsNullOrWhiteSpace(field)), StringComparer.OrdinalIgnoreCase);
            var newFields = new HashSet<string>(fields, StringComparer.OrdinalIgnoreCase);
            var added = fields.Where(field => !oldFields.Contains(field)).ToList();
            var removed = current.Select(source => source.Field).Where(field => !string.IsNullOrWhiteSpace(field) && !newFields.Contains(field)).ToList();
            var kept = fields.Where(field => oldFields.Contains(field)).ToList();
            template.Settings ??= new TemplateSettings();
            template.FieldSnapshot = fields.ToList();
            template.Settings.TemplateFields = fields.ToList();
            template.Settings.DataSources = fields.Select(field =>
                current.FirstOrDefault(source => string.Equals(source.Field, field, StringComparison.OrdinalIgnoreCase)) is DataSourceItem existing
                    ? CloneDataSource(existing)
                    : new DataSourceItem { Name = field, Field = field, Enabled = true }).ToList();
            var diff = $"新增: {FormatFieldList(added)}\r\n删除: {FormatFieldList(removed)}\r\n保留: {FormatFieldList(kept)}";
            MessageBox.Show(this, "新版模板的数据源已变化，系统已保留同名字段设置并添加新字段，请在订单管理页面核对。\r\n\r\n" + diff, "模板数据源已更新",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
        }

        private static string FormatFieldList(List<string> fields)
        {
            return fields == null || fields.Count == 0 ? "无" : string.Join(", ", fields.Take(30)) + (fields.Count > 30 ? " ..." : "");
        }

        private bool ValidateTemplateFieldCoverage(string templatePath, IEnumerable<DataSourceItem> configuredSources, Dictionary<string, string> fieldValues, string title)
        {
            var templateFields = GetTemplateFieldsForValidation(templatePath, title);
            if (templateFields == null) return false;

            var issues = ValidationService.FindTemplateFieldIssues(templateFields, configuredSources, fieldValues);
            if (issues.Count == 0) return true;

            var sb = new StringBuilder();
            foreach (var issue in issues) sb.AppendLine(issue);
            MessageBox.Show(this, sb.ToString().Trim(), title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            AddLog($"模板字段完整性校验失败: {sb.ToString().Replace(Environment.NewLine, " ")}", "WARNING");
            return false;
        }

        private List<string> GetTemplateFieldsForValidation(string templatePath, string title)
        {
            if (!_btService.IsConnected)
            {
                MessageBox.Show(this, "BarTender 未连接，无法校验模板完整数据源集合。", title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }
            var fields = _btService.GetTemplateDataSources(templatePath)
                .Where(field => !string.IsNullOrWhiteSpace(field))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(field => field, NaturalStringComparer.Instance)
                .ToList();
            if (fields.Count == 0)
            {
                MessageBox.Show(this, "未读取到模板命名数据源，已阻止打印。", title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }
            return fields;
        }

        private void ShowAddOrderPage()
        {
            if (!ConfirmOrderEditorChanges()) return;
            BuildOrderEditor(null);
        }

        private void ShowOrderSettingsPage(PackagingOrder order)
        {
            if (order == null) return;
            BuildOrderEditor(order);
        }

        private bool ConfirmOrderEditorChanges()
        {
            if (!_orderEditorDirty) return true;
            var choice = MessageBox.Show(this, "订单管理页面有未保存的修改，是否保存设置？", "未保存的订单设置",
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (choice == DialogResult.Cancel) return false;
            if (choice == DialogResult.Yes)
            {
                SaveOrderFromPage();
                return !_orderEditorDirty;
            }
            if (_editingOrder != null) BuildOrderEditor(_editingOrder);
            else BuildOrderEditor(null);
            _orderEditorDirty = false;
            return true;
        }

        private bool CanPostToUi()
        {
            return !IsDisposed && !Disposing && IsHandleCreated;
        }

        private void PostToUi(Action action)
        {
            if (!CanPostToUi()) return;
            try
            {
                BeginInvoke((Action)(() =>
                {
                    if (CanPostToUi()) action();
                }));
            }
            catch (InvalidOperationException) { }
        }

        private void MarkOrderEditorDirty()
        {
            if (!_loadingOrderEditor) _orderEditorDirty = true;
        }

        private void BuildOrderEditor(PackagingOrder order)
        {
            _loadingOrderEditor = true;
            _orderContentPanel.Controls.Clear();
            _orderContentPanel.BackColor = MiuiTheme.Background;
            _orderTemplateDrafts.Clear();
            _selectedOrderTemplateDraft = null;
            _editingOrder = order;
            var editorScale = Math.Max(1F, DeviceDpi / 96F);
            var contentWidth = Math.Max(700, (int)Math.Round(_orderContentPanel.ClientSize.Width / editorScale) - 25);
            var fieldGap = 10;
            var fieldWidth = (contentWidth - 30 - fieldGap * 3) / 4;
            var addOrderTop = new Button { Text = "添加订单", Location = new Point(10, 10), Size = new Size(90, 28) };
            addOrderTop.Click += (s, e) => ShowAddOrderPage();
            _orderContentPanel.Controls.Add(addOrderTop);
            MiuiTheme.StyleButton(addOrderTop, order == null);

            _txtOrderCustomer = AddOrderPageComboBox("客户", 10, 50, fieldWidth, _orders.Orders.Select(item => item.Customer));
            _txtOrderModel = AddOrderPageComboBox("机型", 10 + (fieldWidth + fieldGap), 50, fieldWidth, GetOrderEditorModels());
            _txtOrderColor = AddOrderPageComboBox("颜色", 10 + (fieldWidth + fieldGap) * 2, 50, fieldWidth, GetOrderEditorColors());
            _txtOrderNumber = AddOrderPageComboBox("订单号", 10 + (fieldWidth + fieldGap) * 3, 50, fieldWidth, GetOrderEditorNumbers());
            if (order != null)
            {
                _txtOrderCustomer.Text = order.Customer;
                _txtOrderModel.Text = order.ProductModel;
                _txtOrderColor.Text = order.Color;
                _txtOrderNumber.Text = order.OrderNumber;
                _txtOrderNumber.Enabled = false;
                _orderTemplateDrafts.AddRange(_orderEditor.CloneTemplates(order.Templates));
            }

            var saveTop = new Button { Text = order == null ? "保存订单" : "保存设置", Location = new Point(Math.Max(110, contentWidth - 95), 10), Size = new Size(95, 28), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            saveTop.Click += (s, e) => SaveOrderFromPage();
            _orderContentPanel.Controls.Add(saveTop);
            MiuiTheme.StyleButton(saveTop, true);

            var templateLabel = new Label { Text = "模板配置（点击卡片切换，每个模板独立保存设置）", Location = new Point(10, 105), Size = new Size(contentWidth, 20), Font = MiuiTheme.SectionFont };
            _orderTemplateCards = new FlowLayoutPanel
            {
                Location = new Point(10, 130), Size = new Size(contentWidth, 130),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                AutoScroll = true, WrapContents = true, Padding = new Padding(2)
            };
            _orderContentPanel.Controls.Add(templateLabel);
            _orderContentPanel.Controls.Add(_orderTemplateCards);
            MiuiTheme.StyleLabel(templateLabel);

            const int actionWidth = 90;
            var templateActionX = 10 + contentWidth - actionWidth;
            _txtOrderTemplate = AddOrderPageTextBox("当前模板外部路径", 10, 268, contentWidth - actionWidth - fieldGap);
            _txtOrderTemplate.ReadOnly = true;
            var browseTemplate = new Button { Text = "添加模板", Location = new Point(templateActionX, 288), Size = new Size(actionWidth, 28), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            browseTemplate.Click += (s, e) => BrowseOrderTemplate();
            var loadFields = new Button { Text = "重新读取", Location = new Point(templateActionX, 323), Size = new Size(actionWidth, 28), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            loadFields.Click += (s, e) => LoadOrderDataSourceRows();
            var removeTemplate = new Button { Text = "删除模板", Location = new Point(templateActionX - actionWidth - fieldGap, 288), Size = new Size(actionWidth, 28), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            removeTemplate.Click += (s, e) => RemoveSelectedOrderTemplateDraft();
            var copyTemplateSettings = new Button { Text = "复制配置", Location = new Point(templateActionX - actionWidth - fieldGap, 323), Size = new Size(actionWidth, 28), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            copyTemplateSettings.Click += (s, e) => CopySelectedTemplateSettingsToOthers();
            _orderContentPanel.Controls.Add(browseTemplate);
            _orderContentPanel.Controls.Add(loadFields);
            _orderContentPanel.Controls.Add(removeTemplate);
            _orderContentPanel.Controls.Add(copyTemplateSettings);
            MiuiTheme.StyleButton(browseTemplate);
            MiuiTheme.StyleButton(loadFields);
            MiuiTheme.StyleButton(removeTemplate);
            MiuiTheme.StyleButton(copyTemplateSettings);

            var printerLabelX = 10;
            var printerLabel = new Label { Text = "打印机：", Location = new Point(printerLabelX, 363), Size = new Size(65, 18) };
            var copiesX = templateActionX - 105;
            _cmbOrderPrinter = new ComboBox { Location = new Point(printerLabelX + 65, 359), Size = new Size(Math.Max(120, copiesX - printerLabelX - 75), 25), DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            foreach (var printer in cmbPrinter.Items) _cmbOrderPrinter.Items.Add(printer);
            if (cmbPrinter.SelectedItem != null && _cmbOrderPrinter.Items.Contains(cmbPrinter.SelectedItem)) _cmbOrderPrinter.SelectedItem = cmbPrinter.SelectedItem;
            else if (_cmbOrderPrinter.Items.Count > 0) _cmbOrderPrinter.SelectedIndex = 0;
            var copiesLabel = new Label { Text = "份数：", Location = new Point(copiesX, 363), Size = new Size(50, 18), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            _numOrderCopies = new NumericUpDown { Location = new Point(copiesX + 50, 359), Size = new Size(55, 25), Minimum = 1, Maximum = 99, Value = numCopies.Value, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            _orderContentPanel.Controls.Add(printerLabel);
            _orderContentPanel.Controls.Add(_cmbOrderPrinter);
            _orderContentPanel.Controls.Add(copiesLabel);
            _orderContentPanel.Controls.Add(_numOrderCopies);
            MiuiTheme.StyleLabel(printerLabel);
            MiuiTheme.StyleLabel(copiesLabel);
            MiuiTheme.StyleComboBox(_cmbOrderPrinter);
            MiuiTheme.StyleNumericUpDown(_numOrderCopies);

            _chkOrderInputValidation = new CheckBox { Text = "本地完整匹配", Location = new Point(10, 397), Size = new Size(120, 22), Enabled = false };
            _chkOrderDuplicateValidation = new CheckBox { Text = "重复校验", Location = new Point(140, 397), Size = new Size(90, 22) };
            _chkOrderLengthValidation = new CheckBox { Text = "长度校验", Location = new Point(240, 397), Size = new Size(90, 22) };
            var globalLengthLabel = new Label { Text = "全局长度：", Location = new Point(340, 400), Size = new Size(75, 18) };
            _numOrderGlobalLength = new NumericUpDown { Location = new Point(415, 395), Size = new Size(70, 25), Minimum = 0, Maximum = 512 };
            var chooseValidationData = new Button { Text = "选择校验数据", Location = new Point(500, 393), Size = new Size(100, 28) };
            chooseValidationData.Click += (s, e) => SelectOrderValidationData();
            var manageValidationData = new Button { Text = "管理校验", Location = new Point(605, 393), Size = new Size(80, 28) };
            manageValidationData.Click += (s, e) => ManageOrderValidationData();
            _lblOrderLocalData = new Label { Text = "校验数据：未配置", Location = new Point(695, 400), Size = new Size(Math.Max(180, contentWidth - 685), 18), AutoEllipsis = true };
            _orderContentPanel.Controls.Add(_chkOrderInputValidation);
            _orderContentPanel.Controls.Add(_chkOrderDuplicateValidation);
            _orderContentPanel.Controls.Add(_chkOrderLengthValidation);
            _orderContentPanel.Controls.Add(globalLengthLabel);
            _orderContentPanel.Controls.Add(_numOrderGlobalLength);
            _orderContentPanel.Controls.Add(chooseValidationData);
            _orderContentPanel.Controls.Add(manageValidationData);
            _orderContentPanel.Controls.Add(_lblOrderLocalData);
            MiuiTheme.StyleLabel(globalLengthLabel);
            MiuiTheme.StyleLabel(_lblOrderLocalData, true);
            MiuiTheme.StyleCheckBox(_chkOrderInputValidation);
            MiuiTheme.StyleCheckBox(_chkOrderDuplicateValidation);
            MiuiTheme.StyleCheckBox(_chkOrderLengthValidation);
            MiuiTheme.StyleNumericUpDown(_numOrderGlobalLength);
            MiuiTheme.StyleButton(chooseValidationData);
            MiuiTheme.StyleButton(manageValidationData);

            var dataSourceTitle = new Label
            {
                Text = "数据源详细设置",
                Location = new Point(10, 432), Size = new Size(contentWidth, 20),
                Font = MiuiTheme.SectionFont
            };
            _chkOrderToggleAllSources = new CheckBox { Text = "全选数据源", Location = new Point(140, 431), Size = new Size(100, 22), Checked = true };
            _chkOrderToggleAllSources.CheckedChanged += (s, e) => { if (!_updatingOrderToggleAll) ToggleOrderDataSources(_chkOrderToggleAllSources.Checked); };
            var invertSources = new Button { Text = "反选数据源", Location = new Point(245, 429), Size = new Size(90, 24) };
            invertSources.Click += (s, e) => InvertOrderDataSources();
            _orderContentPanel.Controls.Add(dataSourceTitle);
            _orderContentPanel.Controls.Add(_chkOrderToggleAllSources);
            _orderContentPanel.Controls.Add(invertSources);
            MiuiTheme.StyleLabel(dataSourceTitle);
            MiuiTheme.StyleCheckBox(_chkOrderToggleAllSources);
            MiuiTheme.StyleButton(invertSources);

            _orderDataSourcesGrid = new DataGridView
            {
                Location = new Point(10, 456),
                Size = new Size(contentWidth, 285),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                ScrollBars = ScrollBars.Both,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                EnableHeadersVisualStyles = false,
                ColumnHeadersHeight = 34,
                RowTemplate = { Height = 30 }
            };
            _orderDataSourcesGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(242, 246, 252);
            _orderDataSourcesGrid.ColumnHeadersDefaultCellStyle.ForeColor = MiuiTheme.TextPrimary;
            _orderDataSourcesGrid.ColumnHeadersDefaultCellStyle.Font = MiuiTheme.SectionFont;
            _orderDataSourcesGrid.DefaultCellStyle.SelectionBackColor = MiuiTheme.PrimaryLight;
            _orderDataSourcesGrid.DefaultCellStyle.SelectionForeColor = MiuiTheme.TextPrimary;
            ConfigureOrderDataSourceGrid();
            _orderDataSourcesGrid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (_orderDataSourcesGrid.IsCurrentCellDirty) _orderDataSourcesGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _orderDataSourcesGrid.CellValueChanged += OrderDataSourcesGrid_CellValueChanged;
            _orderDataSourcesGrid.CellContentClick += OrderDataSourcesGrid_CellContentClick;
            _orderDataSourcesGrid.CellPainting += OrderDataSourcesGrid_CellPainting;
            _orderDataSourcesGrid.DataError += (s, e) => { e.ThrowException = false; };
            _orderContentPanel.Controls.Add(_orderDataSourcesGrid);
            MiuiTheme.StyleDataGridView(_orderDataSourcesGrid);
            _orderDataSourcesGrid.BorderStyle = BorderStyle.FixedSingle;
            _orderDataSourcesGrid.CellBorderStyle = DataGridViewCellBorderStyle.Single;
            _orderDataSourcesGrid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            _orderDataSourcesGrid.GridColor = MiuiTheme.Border;
            _orderDataSourcesGrid.ColumnHeadersHeight = ScaleUi(36);
            _orderDataSourcesGrid.RowTemplate.Height = ScaleUi(32);
            _orderDataSourcesGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(224, 236, 255);
            _orderDataSourcesGrid.DefaultCellStyle.SelectionForeColor = MiuiTheme.TextPrimary;

            _txtOrderTemplate.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _lblOrderLocalData.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            foreach (var control in new Control[] { _txtOrderCustomer, _txtOrderModel, _txtOrderColor, _txtOrderNumber })
                control.TextChanged += (s, e) => MarkOrderEditorDirty();
            _txtOrderCustomer.TextChanged += (s, e) => RefreshOrderEditorCascade(OrderFilterLevel.Customer);
            _txtOrderModel.TextChanged += (s, e) => RefreshOrderEditorCascade(OrderFilterLevel.Model);
            _txtOrderColor.TextChanged += (s, e) => RefreshOrderEditorCascade(OrderFilterLevel.Color);
            _txtOrderNumber.TextChanged += (s, e) => TryEnterOrderEditModeFromEditor();
            _cmbOrderPrinter.SelectedIndexChanged += (s, e) => MarkOrderEditorDirty();
            _numOrderCopies.ValueChanged += (s, e) => MarkOrderEditorDirty();
            _chkOrderInputValidation.CheckedChanged += (s, e) => { UpdateOrderValidationControls(); MarkOrderEditorDirty(); };
            _chkOrderDuplicateValidation.Checked = _duplicateValidationEnabled;
            _chkOrderDuplicateValidation.CheckedChanged += (s, e) => MarkOrderEditorDirty();
            _chkOrderLengthValidation.CheckedChanged += (s, e) => { UpdateOrderValidationControls(); ApplyOrderGlobalLengthToGrid(true); MarkOrderEditorDirty(); };
            _numOrderGlobalLength.ValueChanged += (s, e) => { ApplyOrderGlobalLengthToGrid(true); MarkOrderEditorDirty(); };
            UpdateOrderValidationControls();

            ScaleOrderEditorControls(editorScale);

            RefreshOrderTemplateCards();
            if (_orderTemplateDrafts.Count > 0) SelectOrderTemplateDraft(_orderTemplateDrafts[0]);
            _loadingOrderEditor = false;
            _orderEditorDirty = false;
        }

        private void ScaleOrderEditorControls(float scale)
        {
            if (scale <= 1F)
            {
                _orderContentPanel.AutoScrollMinSize = new Size(ScaleUi(740), ScaleUi(845));
                return;
            }
            foreach (Control control in _orderContentPanel.Controls)
            {
                var bounds = control.Bounds;
                control.Bounds = new Rectangle(
                    (int)Math.Round(bounds.X * scale),
                    (int)Math.Round(bounds.Y * scale),
                    (int)Math.Round(bounds.Width * scale),
                    (int)Math.Round(bounds.Height * scale));
            }
            _orderContentPanel.AutoScrollMinSize = new Size(ScaleUi(740), ScaleUi(845));
        }

        private TextBox AddOrderPageTextBox(string labelText, int x, int y, int width = 200)
        {
            var label = new Label { Text = labelText + "：", Location = new Point(x, y), Size = new Size(width, 18) };
            var text = new TextBox { Location = new Point(x, y + 22), Size = new Size(width, 25) };
            _orderContentPanel.Controls.Add(label);
            _orderContentPanel.Controls.Add(text);
            MiuiTheme.StyleLabel(label);
            MiuiTheme.StyleTextBox(text);
            return text;
        }

        private ComboBox AddOrderPageComboBox(string labelText, int x, int y, int width, IEnumerable<string> values)
        {
            var label = new Label { Text = labelText + "：", Location = new Point(x, y), Size = new Size(width, 18) };
            var combo = new ComboBox { Location = new Point(x, y + 22), Size = new Size(width, 25), DropDownStyle = ComboBoxStyle.DropDown };
            foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, NaturalStringComparer.Instance))
                combo.Items.Add(value);
            _orderContentPanel.Controls.Add(label);
            _orderContentPanel.Controls.Add(combo);
            MiuiTheme.StyleLabel(label);
            MiuiTheme.StyleComboBox(combo);
            return combo;
        }

        private IEnumerable<string> GetOrderEditorModels()
        {
            var customer = _txtOrderCustomer?.Text?.Trim() ?? "";
            return _orders.Orders.Where(order => string.IsNullOrWhiteSpace(customer) || string.Equals(order.Customer, customer, StringComparison.OrdinalIgnoreCase)).Select(order => order.ProductModel);
        }

        private IEnumerable<string> GetOrderEditorColors()
        {
            var customer = _txtOrderCustomer?.Text?.Trim() ?? "";
            var model = _txtOrderModel?.Text?.Trim() ?? "";
            return _orders.Orders.Where(order =>
                (string.IsNullOrWhiteSpace(customer) || string.Equals(order.Customer, customer, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(model) || string.Equals(order.ProductModel, model, StringComparison.OrdinalIgnoreCase))).Select(order => order.Color);
        }

        private IEnumerable<string> GetOrderEditorNumbers()
        {
            var customer = _txtOrderCustomer?.Text?.Trim() ?? "";
            var model = _txtOrderModel?.Text?.Trim() ?? "";
            var color = _txtOrderColor?.Text?.Trim() ?? "";
            return _orders.Orders.Where(order =>
                (string.IsNullOrWhiteSpace(customer) || string.Equals(order.Customer, customer, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(model) || string.Equals(order.ProductModel, model, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(color) || string.Equals(order.Color, color, StringComparison.OrdinalIgnoreCase))).Select(order => order.OrderNumber);
        }

        private void RefreshOrderEditorCascade(OrderFilterLevel level)
        {
            if (_loadingOrderEditor) return;
            if (level <= OrderFilterLevel.Customer) FillEditableCombo(_txtOrderModel, GetOrderEditorModels());
            if (level <= OrderFilterLevel.Model) FillEditableCombo(_txtOrderColor, GetOrderEditorColors());
            if (level <= OrderFilterLevel.Color) FillOrderNumberSuggestions();
            TryEnterOrderEditModeFromEditor();
        }

        private void FillEditableCombo(ComboBox combo, IEnumerable<string> values)
        {
            if (combo == null) return;
            var text = combo.Text;
            combo.Items.Clear();
            foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, NaturalStringComparer.Instance)) combo.Items.Add(value);
            combo.Text = text;
        }

        private void FillOrderNumberSuggestions()
        {
            if (_txtOrderNumber == null) return;
            var text = _txtOrderNumber.Text;
            var numbers = GetOrderEditorNumbers().Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, NaturalStringComparer.Instance).ToArray();
            _txtOrderNumber.Items.Clear();
            _txtOrderNumber.Items.AddRange(numbers.Cast<object>().ToArray());
            var source = new AutoCompleteStringCollection();
            source.AddRange(numbers);
            _txtOrderNumber.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            _txtOrderNumber.AutoCompleteSource = AutoCompleteSource.CustomSource;
            _txtOrderNumber.AutoCompleteCustomSource = source;
            _txtOrderNumber.Text = text;
        }

        private void TryEnterOrderEditModeFromEditor()
        {
            if (_loadingOrderEditor || _editingOrder != null) return;
            var order = _orders.Find(_txtOrderCustomer?.Text?.Trim(), _txtOrderModel?.Text?.Trim(), _txtOrderColor?.Text?.Trim(), _txtOrderNumber?.Text?.Trim());
            if (order == null) return;
            BuildOrderEditor(order);
        }

        private void BrowseOrderTemplate()
        {
            using (var ofd = new OpenFileDialog { Filter = "BarTender 模板|*.btw|所有文件|*.*", FileName = _txtOrderTemplate.Text })
                if (ofd.ShowDialog(this) == DialogResult.OK)
                {
                    AddOrderTemplateDraft(ofd.FileName);
                }
        }

        private void AddOrderTemplateDraft(string path)
        {
            path = Path.GetFullPath(path);
            if (_orderTemplateDrafts.Any(item => string.Equals(item.SourcePath, path, StringComparison.OrdinalIgnoreCase)))
            { MessageBox.Show(this, "该模板已添加。", "添加订单", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (!ValidateOrderDataSourceGrid()) return;
            if (!SaveSelectedOrderTemplateDraft()) return;
            if (_selectedOrderTemplateDraft != null && string.IsNullOrWhiteSpace(_selectedOrderTemplateDraft.SourcePath))
            {
                _selectedOrderTemplateDraft.SourcePath = path;
                _selectedOrderTemplateDraft.ArchivedPath = "";
                _selectedOrderTemplateDraft.SourceLastWriteTimeUtcTicks = 0;
                _selectedOrderTemplateDraft.SourceLength = 0;
                _selectedOrderTemplateDraft.SourceSha256 = "";
                _selectedOrderTemplateDraft.Settings ??= new TemplateSettings();
                _selectedOrderTemplateDraft.Settings.TemplateName = Path.GetFileName(path);
                _selectedOrderTemplateDraft.Settings.TemplatePath = path;
                SelectOrderTemplateDraft(_selectedOrderTemplateDraft);
                LoadOrderDataSourceRows();
                MarkOrderEditorDirty();
                return;
            }
            var draft = new OrderTemplate
            {
                SourcePath = path,
                Settings = new TemplateSettings
                {
                    TemplateName = Path.GetFileName(path),
                    TemplatePath = path,
                    Printer = cmbPrinter.SelectedItem?.ToString() ?? "",
                    Copies = (int)numCopies.Value,
                    DataSources = new List<DataSourceItem>()
                }
            };
            _orderTemplateDrafts.Add(draft);
            SelectOrderTemplateDraft(draft);
            LoadOrderDataSourceRows();
            MarkOrderEditorDirty();
        }

        private void RemoveSelectedOrderTemplateDraft()
        {
            if (_selectedOrderTemplateDraft == null)
            { MessageBox.Show(this, "请先选择要删除的模板卡片。", "订单设置", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (MessageBox.Show(this, $"确定从订单中移除模板“{_selectedOrderTemplateDraft.DisplayName}”？\r\n外部模板文件将保留。",
                "删除模板", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            var index = _orderTemplateDrafts.IndexOf(_selectedOrderTemplateDraft);
            _orderTemplateDrafts.Remove(_selectedOrderTemplateDraft);
            _selectedOrderTemplateDraft = null;
            MarkOrderEditorDirty();
            RefreshOrderTemplateCards();
            if (_orderTemplateDrafts.Count > 0)
                SelectOrderTemplateDraft(_orderTemplateDrafts[Math.Min(index, _orderTemplateDrafts.Count - 1)]);
            else
            {
                _txtOrderTemplate.Clear();
                LoadOrderSettingsIntoGrid(null);
            }
        }

        private void CopySelectedTemplateSettingsToOthers()
        {
            if (_selectedOrderTemplateDraft == null)
            { MessageBox.Show(this, "请先选择来源模板。", "复制配置", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (!SaveSelectedOrderTemplateDraft()) return;
            var targets = _orderTemplateDrafts.Where(template => !ReferenceEquals(template, _selectedOrderTemplateDraft)).ToList();
            if (targets.Count == 0)
            { MessageBox.Show(this, "当前订单没有其他模板可复制。", "复制配置", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (MessageBox.Show(this, $"确定将当前模板的数据源、校验、长度、打印机和份数配置复制到其他 {targets.Count} 个模板？", "复制配置", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            foreach (var target in targets)
            {
                var sourceSettings = CloneTemplateSettings(_selectedOrderTemplateDraft.Settings);
                sourceSettings.TemplateName = Path.GetFileName(target.SourcePath);
                sourceSettings.TemplatePath = target.SourcePath;
                target.Settings = sourceSettings;
            }
            RefreshOrderTemplateCards();
            MarkOrderEditorDirty();
            AddLog($"已复制模板配置到 {targets.Count} 个模板", "SUCCESS");
        }

        private void ShowTemplateGovernance()
        {
            if (_selectedOrderTemplateDraft == null)
            { MessageBox.Show(this, "请先选择模板。", "模板治理", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            var template = _selectedOrderTemplateDraft;
            var currentFields = _btService.IsConnected && File.Exists(template.SourcePath)
                ? _btService.GetTemplateDataSources(template.SourcePath).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(field => field, NaturalStringComparer.Instance).ToList()
                : new List<string>();
            var snapshot = template.FieldSnapshot ?? new List<string>();
            var added = currentFields.Where(field => !snapshot.Contains(field, StringComparer.OrdinalIgnoreCase)).ToList();
            var removed = snapshot.Where(field => !currentFields.Contains(field, StringComparer.OrdinalIgnoreCase)).ToList();
            var kept = currentFields.Where(field => snapshot.Contains(field, StringComparer.OrdinalIgnoreCase)).ToList();
            var message = $"模板: {template.DisplayName}\n路径: {template.SourcePath}\n哈希: {template.SourceSha256}\n修改Ticks: {template.SourceLastWriteTimeUtcTicks}\n大小: {template.SourceLength}\n\n新增字段: {FormatFieldList(added)}\n删除字段: {FormatFieldList(removed)}\n保留字段: {FormatFieldList(kept)}\n\n选择“是”审批当前字段快照并更新映射确认。";
            if (MessageBox.Show(this, message, "模板治理", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            template.FieldSnapshot = currentFields;
            template.Settings ??= new TemplateSettings();
            template.Settings.TemplateFields = currentFields;
            MarkOrderEditorDirty();
            AddLog("模板字段快照已审批更新", "SUCCESS");
        }

        private static OrderTemplate CloneOrderTemplate(OrderTemplate template)
        {
            return new OrderTemplate
            {
                Id = template.Id,
                SourcePath = template.SourcePath,
                ArchivedPath = template.ArchivedPath,
                SourceLastWriteTimeUtcTicks = template.SourceLastWriteTimeUtcTicks,
                SourceLength = template.SourceLength,
                SourceSha256 = template.SourceSha256,
                FieldSnapshot = (template.FieldSnapshot ?? new List<string>()).ToList(),
                Settings = CloneTemplateSettings(template.Settings)
            };
        }

        private static TemplateSettings CloneTemplateSettings(TemplateSettings settings)
        {
            settings ??= new TemplateSettings();
            return new TemplateSettings
            {
                SchemaVersion = settings.SchemaVersion,
                Scope = settings.Scope,
                OrderId = settings.OrderId,
                TemplateId = settings.TemplateId,
                TemplateName = settings.TemplateName,
                TemplatePath = settings.TemplatePath,
                Printer = settings.Printer,
                Copies = settings.Copies,
                InputValidation = settings.InputValidation,
                DuplicateValidation = settings.DuplicateValidation,
                LengthValidation = settings.LengthValidation,
                GlobalExpectedLength = settings.GlobalExpectedLength,
                GlobalLengthRevision = settings.GlobalLengthRevision,
                LengthRevisionCounter = settings.LengthRevisionCounter,
                LocalDataPath = settings.LocalDataPath,
                LocalDataStoragePath = settings.LocalDataStoragePath,
                LocalDataColumnName = settings.LocalDataColumnName,
                LocalDataTargetField = settings.LocalDataTargetField,
                TemplateFields = (settings.TemplateFields ?? new List<string>()).ToList(),
                LocalData = (settings.LocalData ?? new List<string>()).ToList(),
                DataSources = (settings.DataSources ?? new List<DataSourceItem>()).Select(CloneDataSource).ToList()
            };
        }

        private void RefreshOrderTemplateCards()
        {
            if (_orderTemplateCards == null) return;
            _orderTemplateCards.Controls.Clear();
            if (_orderTemplateDrafts.Count == 0)
            {
                _orderTemplateCards.Controls.Add(new Label
                {
                    Text = "尚未添加模板，请点击右侧“添加模板”开始配置。",
                    AutoSize = false,
                    Size = new Size(Math.Max(ScaleUi(320), _orderTemplateCards.ClientSize.Width - ScaleUi(20)), ScaleUi(52)),
                    Padding = new Padding(ScaleUi(10), ScaleUi(16), 0, 0),
                    ForeColor = MiuiTheme.TextSecondary
                });
                return;
            }
            foreach (var template in _orderTemplateDrafts)
            {
                var selected = ReferenceEquals(template, _selectedOrderTemplateDraft);
                var enabledCount = template.Settings?.DataSources?.Count(source => source.Enabled) ?? 0;
                var totalCount = template.Settings?.DataSources?.Count ?? 0;
                var card = new Button
                {
                    Text = string.IsNullOrWhiteSpace(template.SourcePath)
                        ? $"需重新选择外部模板\r\n已保留 {totalCount} 个旧设置"
                        : $"{template.DisplayName}\r\n已启用 {enabledCount} / 共 {totalCount} 个数据源",
                    Size = new Size(ScaleUi(210), ScaleUi(52)),
                    TextAlign = ContentAlignment.MiddleLeft,
                    Tag = template,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = selected ? MiuiTheme.PrimaryLight : Color.White,
                    ForeColor = selected ? MiuiTheme.PrimaryDark : MiuiTheme.TextPrimary,
                    Margin = new Padding(ScaleUi(4)),
                    Padding = new Padding(ScaleUi(8), ScaleUi(3), ScaleUi(8), ScaleUi(3)),
                    Cursor = Cursors.Hand
                };
                card.FlatAppearance.BorderColor = selected ? Color.FromArgb(55, 115, 205) : Color.FromArgb(205, 210, 220);
                card.Click += (s, e) => SelectOrderTemplateDraft((OrderTemplate)((Button)s).Tag);
                var menu = new ContextMenuStrip();
                menu.Items.Add("删除此模板", null, (s, e) => { SelectOrderTemplateDraft((OrderTemplate)card.Tag); RemoveSelectedOrderTemplateDraft(); });
                card.ContextMenuStrip = menu;
                _orderTemplateCards.Controls.Add(card);
            }
        }

        private void SelectOrderValidationData()
        {
            if (_selectedOrderTemplateDraft == null)
            { MessageBox.Show(this, "请先选择一个模板卡片。", "订单设置", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            using (var dialog = new OpenFileDialog { Filter = "Excel|*.xlsx;*.xls|CSV|*.csv|文本|*.txt|所有文件|*.*", FilterIndex = 1 })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var ext = Path.GetExtension(dialog.FileName).ToLowerInvariant();
                    if (ext == ".xlsx" || ext == ".xls")
                    {
                        LoadOrderValidationDataAsync(dialog.FileName, _selectedOrderTemplateDraft);
                        return;
                    }
                    var imported = ReadValidationDataFile(dialog.FileName);
                    if (imported == null) return;
                    ApplyOrderValidationData(dialog.FileName, imported);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"读取校验数据失败：{ex.Message}", "订单设置", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void ManageOrderValidationData()
        {
            if (_selectedOrderTemplateDraft?.Settings == null)
            { MessageBox.Show(this, "请先选择一个模板卡片。", "校验数据", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            var settings = _selectedOrderTemplateDraft.Settings;
            var count = GetTemplateLocalData(settings).Count;
            if (count == 0 && string.IsNullOrWhiteSpace(settings.LocalDataPath) && string.IsNullOrWhiteSpace(settings.LocalDataStoragePath))
            { MessageBox.Show(this, "当前模板未选择校验数据文件，请先点击“选择校验数据”。", "校验数据管理", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            var selectedFields = (settings.DataSources ?? new List<DataSourceItem>())
                .Where(source => source.Enabled && source.UseLocalDataValidation)
                .Select(source => source.Field);
            var message = $"路径: {settings.LocalDataPath}\n快照: {settings.LocalDataStoragePath}\n列: {settings.LocalDataColumnName}\n校验字段: {string.Join(", ", selectedFields)}\n数据量: {count}\n\n选择“是”替换校验数据，选择“否”清除当前模板校验数据。";
            var choice = MessageBox.Show(this, message, "校验数据管理", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (choice == DialogResult.Yes) { SelectOrderValidationData(); return; }
            if (choice != DialogResult.No) return;
            settings.LocalDataPath = "";
            settings.LocalDataStoragePath = "";
            settings.LocalDataColumnName = "";
            settings.LocalDataTargetField = "";
            settings.LocalData = new List<string>();
            settings.InputValidation = false;
            foreach (var source in settings.DataSources ?? new List<DataSourceItem>()) source.UseLocalDataValidation = false;
            _chkOrderInputValidation.Checked = false;
            _chkOrderInputValidation.Enabled = false;
            _lblOrderLocalData.Text = "校验数据：未配置";
            MarkOrderEditorDirty();
        }

        private void LoadOrderValidationDataAsync(string path, OrderTemplate targetTemplate)
        {
            SetStatus("正在加载订单校验数据...");
            RunSta(() =>
            {
                try
                {
                    var imported = ReadExcelValidationDataInBackground(path);
                    BeginInvoke((Action)(() =>
                    {
                        try
                        {
                            if (imported != null && targetTemplate != null && _orderTemplateDrafts.Contains(targetTemplate))
                                ApplyOrderValidationData(path, imported, targetTemplate);
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(this, $"保存校验数据失败：{ex.Message}", "订单设置", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                        finally { SetStatus("就绪"); }
                    }));
                }
                catch (Exception ex)
                {
                    BeginInvoke((Action)(() =>
                    {
                        MessageBox.Show(this, $"读取 Excel 失败：{ex.Message}", "订单设置", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        SetStatus("就绪");
                    }));
                }
            });
        }

        private static void RunSta(Action action)
        {
            var thread = new System.Threading.Thread(() =>
            {
                try { action(); }
                catch (Exception ex) { LoggerService.Error("后台 STA 任务失败", ex); }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
        }

        private void ApplyOrderValidationData(string path, LocalDataImportResult imported, OrderTemplate targetTemplate = null)
        {
            targetTemplate ??= _selectedOrderTemplateDraft;
            if (targetTemplate?.Settings == null) return;
            targetTemplate.Settings.LocalDataPath = path;
            targetTemplate.Settings.LocalDataColumnName = imported.ColumnName;
            targetTemplate.Settings.LocalDataTargetField = "";
            foreach (var source in targetTemplate.Settings.DataSources ?? new List<DataSourceItem>())
                source.UseLocalDataValidation = source.Enabled;
            var scope = FirstNonEmpty(_editingOrder?.OrderId, _activeOrder?.OrderId, _txtOrderNumber?.Text, "draft");
            targetTemplate.Settings.LocalDataStoragePath = SaveValidationDataSnapshot(scope, targetTemplate.Id, targetTemplate.SourcePath, imported.Values);
            targetTemplate.Settings.LocalData = new List<string>();
            if (ReferenceEquals(targetTemplate, _selectedOrderTemplateDraft))
            {
                _chkOrderInputValidation.Enabled = true;
                _chkOrderInputValidation.Checked = true;
                _lblOrderLocalData.Text = $"校验数据：{path}（{imported.Values.Count} 条，已默认勾选全部启用数据源）";
                _toolTips.SetToolTip(_lblOrderLocalData, _lblOrderLocalData.Text);
                LoadOrderSettingsIntoGrid(targetTemplate.Settings);
            }
            MessageBox.Show(this, $"校验数据导入完成\n总行数：{imported.TotalRows}\n去重后：{imported.Values.Count}\n重复数：{imported.DuplicateRows}\n空值数：{imported.EmptyRows}", "校验数据", MessageBoxButtons.OK, MessageBoxIcon.Information);
            MarkOrderEditorDirty();
        }

        private static string SaveValidationDataSnapshot(string orderScope, string templateId, string templatePath, HashSet<string> values)
        {
            AppPaths.Initialize();
            var hashInput = $"{orderScope}|{templateId}|{templatePath}";
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashInput))).Substring(0, 24);
            var targetPath = Path.Combine(AppPaths.ValidationDataDirectory, $"{hash}.txt");
            AtomicFileWriter.WriteAllLines(targetPath, values.OrderBy(value => value, NaturalStringComparer.Instance), Encoding.UTF8);
            return targetPath;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
        }

        private static HashSet<string> GetTemplateLocalData(TemplateSettings settings)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(settings?.LocalDataStoragePath) && File.Exists(settings.LocalDataStoragePath))
                    return new HashSet<string>(File.ReadLines(settings.LocalDataStoragePath).Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => line.Trim()), StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                LoggerService.Warn($"读取本地校验数据快照失败: {ex.Message}");
            }
            return new HashSet<string>(settings?.LocalData ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        }

        private string GetCurrentTemplateId()
        {
            return _activeOrderTemplate != null && string.Equals(_activeOrderTemplate.SourcePath, _selectedTemplatePath, StringComparison.OrdinalIgnoreCase)
                ? _activeOrderTemplate.Id ?? ""
                : "";
        }

        private string GetTemplateIdForPath(string templatePath)
        {
            return _orders.Orders
                .SelectMany(order => order.Templates ?? new List<OrderTemplate>())
                .FirstOrDefault(template => string.Equals(template.SourcePath, templatePath, StringComparison.OrdinalIgnoreCase))?.Id ?? "";
        }

        private LocalDataImportResult ReadValidationDataFile(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".csv") return ReadCsvValidationData(path);
            if (ext == ".xlsx" || ext == ".xls") return ReadExcelValidationData(path);
            var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in File.ReadLines(path))
            { var value = line.Trim(); if (!string.IsNullOrEmpty(value)) values.Add(value); }
            return new LocalDataImportResult(values, "文本", values.Count, 0, 0);
        }

        private LocalDataImportResult ReadCsvValidationData(string path)
        {
            using (var enumerator = File.ReadLines(path).GetEnumerator())
            {
                if (!enumerator.MoveNext()) { MessageBox.Show(this, "CSV 文件为空"); return null; }
                var firstRow = CsvUtils.ParseLine(enumerator.Current);
                var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (firstRow.Count <= 1)
                {
                    if (firstRow.Count == 1 && !string.IsNullOrWhiteSpace(firstRow[0])) values.Add(firstRow[0].Trim());
                    while (enumerator.MoveNext())
                    {
                        var columns = CsvUtils.ParseLine(enumerator.Current);
                        if (columns.Count > 0 && !string.IsNullOrWhiteSpace(columns[0])) values.Add(columns[0].Trim());
                    }
                    return new LocalDataImportResult(values, "单列");
                }
                var colIdx = PromptForColumnSelection(firstRow, Path.GetFileName(path));
                if (colIdx < 0) return null;
                while (enumerator.MoveNext())
                {
                    var columns = CsvUtils.ParseLine(enumerator.Current);
                    if (colIdx < columns.Count && !string.IsNullOrWhiteSpace(columns[colIdx])) values.Add(columns[colIdx].Trim());
                }
                return new LocalDataImportResult(values, firstRow[colIdx]);
            }
        }

        private LocalDataImportResult ReadExcelValidationData(string path)
        {
            var excelType = Type.GetTypeFromProgID("Excel.Application");
            if (excelType == null) { MessageBox.Show(this, "未安装 Excel，请保存为 CSV 格式后加载"); return null; }
            dynamic excel = null;
            dynamic wb = null;
            dynamic ws = null;
            dynamic usedRange = null;
            try
            {
                excel = Activator.CreateInstance(excelType);
                excel.Visible = false;
                excel.DisplayAlerts = false;
                wb = excel.Workbooks.Open(path, ReadOnly: true);
                ws = wb.ActiveSheet;
                usedRange = ws.UsedRange;
                int rows = usedRange.Rows.Count;
                int cols = usedRange.Columns.Count;
                if (rows < 1 || cols < 1) { MessageBox.Show(this, "Excel 文件为空"); return null; }
                dynamic allData = usedRange.Value2;
                if (rows == 1 && cols == 1)
                {
                    var singleValue = allData?.ToString()?.Trim();
                    var singleValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (!string.IsNullOrEmpty(singleValue)) singleValues.Add(singleValue);
                    return new LocalDataImportResult(singleValues, "单列");
                }
                var headers = new List<string>();
                for (int c = 1; c <= cols; c++) headers.Add(allData[1, c]?.ToString()?.Trim() ?? $"列{c}");
                var colIdx = 0;
                var startRow = 1;
                var columnName = "单列";
                if (cols > 1)
                {
                    colIdx = PromptForColumnSelection(headers, Path.GetFileName(path));
                    if (colIdx < 0) return null;
                    startRow = 2;
                    columnName = headers[colIdx];
                }
                var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int r = startRow; r <= rows; r++)
                {
                    var value = allData[r, colIdx + 1]?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(value)) values.Add(value);
                }
                return new LocalDataImportResult(values, columnName);
            }
            finally
            {
                try { wb?.Close(false); } catch { }
                try { excel?.Quit(); } catch { }
                try { if (usedRange != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(usedRange); } catch { }
                try { if (ws != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(ws); } catch { }
                try { if (wb != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(wb); } catch { }
                try { if (excel != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(excel); } catch { }
            }
        }

        private LocalDataImportResult ReadExcelValidationDataInBackground(string path)
        {
            var excelType = Type.GetTypeFromProgID("Excel.Application");
            if (excelType == null) throw new InvalidOperationException("未安装 Excel，请保存为 CSV 格式后加载");
            dynamic excel = null;
            dynamic wb = null;
            dynamic ws = null;
            dynamic usedRange = null;
            try
            {
                excel = Activator.CreateInstance(excelType);
                excel.Visible = false;
                excel.DisplayAlerts = false;
                wb = excel.Workbooks.Open(path, ReadOnly: true);
                ws = wb.ActiveSheet;
                usedRange = ws.UsedRange;
                int rows = usedRange.Rows.Count;
                int cols = usedRange.Columns.Count;
                if (rows < 1 || cols < 1) return null;
                dynamic allData = usedRange.Value2;
                if (rows == 1 && cols == 1)
                {
                    var singleValue = allData?.ToString()?.Trim();
                    var singleValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (!string.IsNullOrEmpty(singleValue)) singleValues.Add(singleValue);
                    return new LocalDataImportResult(singleValues, "单列");
                }
                var headers = new List<string>();
                for (int c = 1; c <= cols; c++) headers.Add(allData[1, c]?.ToString()?.Trim() ?? $"列{c}");
                var selectedCol = 0;
                var startRow = 1;
                var columnName = "单列";
                if (cols > 1)
                {
                    selectedCol = -1;
                    var evt = new System.Threading.ManualResetEvent(false);
                    BeginInvoke((Action)(() =>
                    {
                        selectedCol = PromptForColumnSelection(headers, Path.GetFileName(path));
                        evt.Set();
                    }));
                    evt.WaitOne();
                    if (selectedCol < 0) return null;
                    startRow = 2;
                    columnName = headers[selectedCol];
                }
                var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int r = startRow; r <= rows; r++)
                {
                    var value = allData[r, selectedCol + 1]?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(value)) values.Add(value);
                }
                return new LocalDataImportResult(values, columnName);
            }
            finally
            {
                try { wb?.Close(false); } catch { }
                try { excel?.Quit(); } catch { }
                try { if (usedRange != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(usedRange); } catch { }
                try { if (ws != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(ws); } catch { }
                try { if (wb != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(wb); } catch { }
                try { if (excel != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(excel); } catch { }
            }
        }

        private sealed class LocalDataImportResult
        {
            public LocalDataImportResult(HashSet<string> values, string columnName)
                : this(values, columnName, values?.Count ?? 0, 0, 0)
            {
            }

            public LocalDataImportResult(HashSet<string> values, string columnName, int totalRows, int duplicateRows, int emptyRows)
            {
                Values = values;
                ColumnName = columnName;
                TotalRows = totalRows;
                DuplicateRows = duplicateRows;
                EmptyRows = emptyRows;
            }

            public HashSet<string> Values { get; }
            public string ColumnName { get; }
            public int TotalRows { get; }
            public int DuplicateRows { get; }
            public int EmptyRows { get; }
        }

        private void SelectOrderTemplateDraft(OrderTemplate template)
        {
            if (_loadingOrderTemplate) return;
            if (!ValidateOrderDataSourceGrid()) return;
            if (!SaveSelectedOrderTemplateDraft()) return;
            var wasLoadingEditor = _loadingOrderEditor;
            _loadingOrderEditor = true;
            _selectedOrderTemplateDraft = template;
            _txtOrderTemplate.Text = _selectedOrderTemplateDraft?.SourcePath ?? "";
            _toolTips.SetToolTip(_txtOrderTemplate, _txtOrderTemplate.Text);
            var settings = _selectedOrderTemplateDraft?.Settings;
            if (settings != null)
            {
                ValidationService.MigrateLocalDataSelection(settings);
                _cmbOrderPrinter.SelectedIndex = -1;
                if (!string.IsNullOrWhiteSpace(settings.Printer))
                {
                    if (!_cmbOrderPrinter.Items.Contains(settings.Printer)) _cmbOrderPrinter.Items.Add(settings.Printer);
                    _cmbOrderPrinter.SelectedItem = settings.Printer;
                }
                _numOrderCopies.Value = Math.Max(_numOrderCopies.Minimum, Math.Min(_numOrderCopies.Maximum, settings.Copies));
                var localDataCount = GetTemplateLocalData(settings).Count;
                _chkOrderInputValidation.Enabled = localDataCount > 0;
                _chkOrderInputValidation.Checked = settings.InputValidation && _chkOrderInputValidation.Enabled;
                _chkOrderDuplicateValidation.Checked = settings.DuplicateValidation;
                _chkOrderLengthValidation.Checked = settings.LengthValidation;
                _numOrderGlobalLength.Value = Math.Max(_numOrderGlobalLength.Minimum, Math.Min(_numOrderGlobalLength.Maximum, settings.GlobalExpectedLength));
                _lblOrderLocalData.Text = string.IsNullOrWhiteSpace(settings.LocalDataPath)
                    ? "校验数据：未配置"
                    : $"校验数据：{settings.LocalDataPath}（{localDataCount} 条）";
                _toolTips.SetToolTip(_lblOrderLocalData, settings.LocalDataPath ?? "");
            }
            LoadOrderSettingsIntoGrid(_selectedOrderTemplateDraft?.Settings);
            _loadingOrderEditor = wasLoadingEditor;
            RefreshOrderTemplateCards();
        }

        private bool SaveSelectedOrderTemplateDraft()
        {
            if (_selectedOrderTemplateDraft == null || _orderDataSourcesGrid == null) return true;
            if (!ValidateOrderDataSourceGrid()) return false;
            _selectedOrderTemplateDraft.Settings = BuildOrderTemplateSettings(_selectedOrderTemplateDraft.SourcePath, BuildDataSourcesFromOrderGrid());
            return true;
        }

        private TemplateSettings BuildOrderTemplateSettings(string templatePath, List<DataSourceItem> dataSources)
        {
            var settings = BuildTemplateSettings(templatePath, dataSources);
            settings.SchemaVersion = 3;
            settings.Scope = "OrderTemplate";
            settings.OrderId = _editingOrder?.OrderId ?? "";
            settings.TemplateId = _selectedOrderTemplateDraft?.Id ?? "";
            settings.TemplateFields = _selectedOrderTemplateDraft?.FieldSnapshot?.ToList() ?? dataSources.Select(source => source.Field).ToList();
            settings.Printer = _cmbOrderPrinter?.SelectedItem?.ToString() ?? settings.Printer;
            settings.Copies = _numOrderCopies == null ? settings.Copies : (int)_numOrderCopies.Value;
            settings.InputValidation = _chkOrderInputValidation?.Checked ?? settings.InputValidation;
            settings.DuplicateValidation = _chkOrderDuplicateValidation?.Checked ?? _duplicateValidationEnabled;
            settings.LengthValidation = _chkOrderLengthValidation?.Checked ?? settings.LengthValidation;
            settings.GlobalExpectedLength = _numOrderGlobalLength == null ? settings.GlobalExpectedLength : (int)_numOrderGlobalLength.Value;
            if (_selectedOrderTemplateDraft?.Settings != null)
            {
                settings.LocalDataPath = _selectedOrderTemplateDraft.Settings.LocalDataPath;
                settings.LocalDataStoragePath = _selectedOrderTemplateDraft.Settings.LocalDataStoragePath;
                settings.LocalDataColumnName = _selectedOrderTemplateDraft.Settings.LocalDataColumnName;
                settings.LocalDataTargetField = _selectedOrderTemplateDraft.Settings.LocalDataTargetField;
                settings.LocalData = (_selectedOrderTemplateDraft.Settings.LocalData ?? new List<string>()).ToList();
                settings.GlobalLengthRevision = _selectedOrderTemplateDraft.Settings.GlobalLengthRevision;
                settings.LengthRevisionCounter = _selectedOrderTemplateDraft.Settings.LengthRevisionCounter;
            }
            return settings;
        }

        private void LoadOrderSettingsIntoGrid(TemplateSettings settings)
        {
            if (_orderDataSourcesGrid == null) return;
            _orderDataSourcesGrid.Rows.Clear();
            foreach (var source in settings?.DataSources ?? new List<DataSourceItem>())
                AddOrderDataSourceRow(source);
            ApplyOrderGlobalLengthToGrid(false);
            UpdateOrderToggleAllState();
        }

        private void ConfigureOrderDataSourceGrid()
        {
            _orderDataSourcesGrid.Columns.Clear();
            _orderDataSourcesGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Enabled", HeaderText = "使用", Width = ScaleUi(60) });
            _orderDataSourcesGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "UseLocalDataValidation", HeaderText = "使用校验数据", Width = ScaleUi(105) });
            _orderDataSourcesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Field", HeaderText = "字段名", ReadOnly = true, MinimumWidth = ScaleUi(180), Width = ScaleUi(220) });
            _orderDataSourcesGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "LockEnabled", HeaderText = "锁定", Visible = false });
            _orderDataSourcesGrid.Columns.Add(new DataGridViewButtonColumn { Name = "LockToggle", HeaderText = "锁定", Width = ScaleUi(52), FlatStyle = FlatStyle.Flat });
            _orderDataSourcesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "AutoStep", HeaderText = "步长（正增负减0不变）", Width = ScaleUi(150) });
            _orderDataSourcesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "LockedValue", HeaderText = "锁定后输入值", Width = ScaleUi(180) });
            _orderDataSourcesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ExpectedLength", HeaderText = "长度", Width = ScaleUi(80) });
        }

        private void ToggleOrderDataSources(bool enabled)
        {
            if (_orderDataSourcesGrid == null) return;
            if (!ValidateOrderDataSourceGrid()) { UpdateOrderToggleAllState(); return; }
            foreach (DataGridViewRow row in _orderDataSourcesGrid.Rows)
            {
                if (row.IsNewRow) continue;
                row.Cells["Enabled"].Value = enabled;
            }
            if (!SaveSelectedOrderTemplateDraft()) { UpdateOrderToggleAllState(); MarkOrderEditorDirty(); return; }
            RefreshOrderTemplateCards();
            MarkOrderEditorDirty();
            UpdateOrderToggleAllState();
        }

        private void InvertOrderDataSources()
        {
            if (_orderDataSourcesGrid == null) return;
            if (!ValidateOrderDataSourceGrid()) { UpdateOrderToggleAllState(); return; }
            foreach (DataGridViewRow row in _orderDataSourcesGrid.Rows)
            {
                if (row.IsNewRow) continue;
                row.Cells["Enabled"].Value = !ToBoolean(row.Cells["Enabled"].Value);
            }
            if (!SaveSelectedOrderTemplateDraft()) { UpdateOrderToggleAllState(); MarkOrderEditorDirty(); return; }
            RefreshOrderTemplateCards();
            MarkOrderEditorDirty();
            UpdateOrderToggleAllState();
        }

        private void UpdateOrderToggleAllState()
        {
            if (_chkOrderToggleAllSources == null || _orderDataSourcesGrid == null) return;
            var rows = _orderDataSourcesGrid.Rows.Cast<DataGridViewRow>().Where(row => !row.IsNewRow).ToList();
            var allEnabled = rows.Count > 0 && rows.All(row => ToBoolean(row.Cells["Enabled"].Value));
            _updatingOrderToggleAll = true;
            try { _chkOrderToggleAllSources.Checked = allEnabled; }
            finally { _updatingOrderToggleAll = false; }
        }

        private void UpdateOrderValidationControls()
        {
            if (_numOrderGlobalLength != null) _numOrderGlobalLength.Enabled = _chkOrderLengthValidation?.Checked == true;
            if (_chkOrderInputValidation != null && !_chkOrderInputValidation.Enabled) _chkOrderInputValidation.Checked = false;
            if (_lblOrderLocalData != null)
                _lblOrderLocalData.ForeColor = _chkOrderInputValidation?.Checked == true ? MiuiTheme.TextPrimary : MiuiTheme.TextSecondary;
        }

        private void ApplyOrderGlobalLengthToGrid(bool confirmOverwrite = false)
        {
            if (_orderDataSourcesGrid == null || _chkOrderLengthValidation?.Checked != true) return;
            if (_loadingOrderEditor && confirmOverwrite) return;
            var expectedLength = (int)(_numOrderGlobalLength?.Value ?? 0);
            if (expectedLength <= 0) return;
            _applyingOrderGlobalLength = true;
            foreach (DataGridViewRow row in _orderDataSourcesGrid.Rows)
            {
                if (row.IsNewRow) continue;
                int.TryParse(row.Cells["ExpectedLength"].Value?.ToString(), out var currentLength);
                var hasIndividualLength = row.Cells["ExpectedLength"].Tag is bool value && value;
                if (hasIndividualLength && currentLength > 0 && currentLength != expectedLength && confirmOverwrite)
                {
                    var fieldName = row.Cells["Field"].Value?.ToString() ?? "数据源";
                    var choice = MessageBox.Show(this, $"数据源“{fieldName}”已设置单独长度 {currentLength}，是否覆盖为全局长度 {expectedLength}？",
                        "覆盖单独长度", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (choice == DialogResult.No) continue;
                }
                if (!hasIndividualLength || currentLength == 0 || currentLength != expectedLength && confirmOverwrite)
                {
                    row.Cells["ExpectedLength"].Value = expectedLength;
                    row.Cells["ExpectedLength"].Tag = false;
                }
            }
            _applyingOrderGlobalLength = false;
        }

        private void OrderDataSourcesGrid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            var columnName = _orderDataSourcesGrid.Columns[e.ColumnIndex].Name;
            if (columnName == "Enabled")
            {
                if (!ValidateOrderDataSourceGrid()) return;
                if (!SaveSelectedOrderTemplateDraft()) { UpdateOrderToggleAllState(); MarkOrderEditorDirty(); return; }
                RefreshOrderTemplateCards();
                UpdateOrderToggleAllState();
            }
            if (columnName == "ExpectedLength" || columnName == "LockedValue" || columnName == "AutoStep" || columnName == "Enabled" || columnName == "UseLocalDataValidation")
            {
                if (columnName == "ExpectedLength" && !_applyingOrderGlobalLength)
                    _orderDataSourcesGrid.Rows[e.RowIndex].Cells["ExpectedLength"].Tag = true;
                MarkOrderEditorDirty();
            }
        }

        private void OrderDataSourcesGrid_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            if (_orderDataSourcesGrid.Columns[e.ColumnIndex].Name != "LockToggle") return;
            var row = _orderDataSourcesGrid.Rows[e.RowIndex];
            var locked = ToBoolean(row.Cells["LockEnabled"].Value);
            row.Cells["LockEnabled"].Value = !locked;
            UpdateOrderDataSourceRowState(row);
            MarkOrderEditorDirty();
        }

        private void UpdateOrderDataSourceRowState(DataGridViewRow row)
        {
            var lockEnabled = ToBoolean(row.Cells["LockEnabled"].Value);
            row.Cells["LockToggle"].Value = "";
            row.Cells["LockedValue"].ReadOnly = !lockEnabled;
            row.Cells["LockedValue"].Style.BackColor = lockEnabled ? SystemColors.Window : SystemColors.Control;
            if (!lockEnabled)
            {
                var stepIndex = _orderDataSourcesGrid.Columns["AutoStep"].Index;
                int.TryParse(row.Cells["AutoStep"].Value?.ToString(), out var currentStep);
                if (currentStep != 0 && MessageBox.Show(this, $"当前步长为 {currentStep}，关闭锁定将恢复默认步长 0。是否确认关闭？", "确认关闭锁定", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    row.Cells["LockEnabled"].Value = true;
                    return;
                }
                row.Tag = new OrderRowLockState
                {
                    AutoStep = row.Cells["AutoStep"].Value,
                    LockedValue = row.Cells["LockedValue"].Value?.ToString() ?? ""
                };
                row.Cells[stepIndex] = new DataGridViewTextBoxCell { Value = 0, Style = new DataGridViewCellStyle { BackColor = SystemColors.Control } };
                row.Cells[stepIndex].ReadOnly = true;
                row.Cells["LockedValue"].Value = "";
            }
            else
            {
                var savedState = row.Tag as OrderRowLockState;
                if (row.Cells["AutoStep"].ReadOnly)
                    row.Cells[_orderDataSourcesGrid.Columns["AutoStep"].Index] = new DataGridViewTextBoxCell { Value = savedState?.AutoStep is null || savedState.AutoStep.ToString() == "0" ? 1 : savedState.AutoStep, Style = new DataGridViewCellStyle { BackColor = SystemColors.Window } };
                row.Cells["AutoStep"].ReadOnly = false;
                if (savedState != null) row.Cells["LockedValue"].Value = savedState.LockedValue;
                row.Tag = null;
            }
        }

        private void OrderDataSourcesGrid_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || _orderDataSourcesGrid.Columns[e.ColumnIndex].Name != "LockToggle") return;
            e.Paint(e.CellBounds, DataGridViewPaintParts.Background | DataGridViewPaintParts.Border);
            var row = _orderDataSourcesGrid.Rows[e.RowIndex];
            var locked = ToBoolean(row.Cells["LockEnabled"].Value);
            var color = locked ? Color.FromArgb(45, 105, 210) : Color.FromArgb(150, 150, 150);
            var centerX = e.CellBounds.Left + e.CellBounds.Width / 2;
            var top = e.CellBounds.Top + Math.Max(4, (e.CellBounds.Height - 22) / 2);
            using (var pen = new Pen(color, 1.7F) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var brush = new SolidBrush(color))
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                DrawRoundedRectangle(e.Graphics, pen, new RectangleF(centerX - 7, top + 8, 14, 11), 2.2F);
                if (locked) e.Graphics.DrawArc(pen, centerX - 5, top + 1, 10, 11, 180, 180);
                else e.Graphics.DrawArc(pen, centerX - 2, top + 1, 10, 11, 190, 230);
                e.Graphics.FillEllipse(brush, centerX - 1.2F, top + 12.5F, 2.5F, 2.5F);
            }
            e.Handled = true;
        }

        private sealed class OrderRowLockState
        {
            public object AutoStep { get; set; }
            public string LockedValue { get; set; }
        }

        private static bool ToBoolean(object value)
        {
            if (value is bool boolValue) return boolValue;
            return bool.TryParse(value?.ToString(), out var parsed) && parsed;
        }

        private void LoadOrderDataSourceRows()
        {
            if (!ValidateOrderDataSourceGrid()) return;
            var templatePath = _selectedOrderTemplateDraft?.SourcePath;
            if (string.IsNullOrWhiteSpace(templatePath) || !File.Exists(templatePath))
            { MessageBox.Show(this, "请先选择有效模板文件。", "添加订单", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            var fields = _btService.IsConnected ? _btService.GetTemplateDataSources(templatePath) : new List<string>();
            if (fields.Count == 0)
            {
                MessageBox.Show(this, "未读取到模板数据源，请检查模板文件中的命名数据源。", "添加订单", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var current = BuildDataSourcesFromOrderGrid();
            _selectedOrderTemplateDraft.FieldSnapshot = fields.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(field => field, NaturalStringComparer.Instance).ToList();
            _orderDataSourcesGrid.Rows.Clear();
            foreach (var field in fields.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(field => field, NaturalStringComparer.Instance))
            {
                var existing = current.FirstOrDefault(item => string.Equals(item.Field, field, StringComparison.OrdinalIgnoreCase));
                AddOrderDataSourceRow(existing ?? new DataSourceItem
                {
                    Name = field,
                    Field = field,
                    Enabled = true,
                    ExpectedLength = _chkOrderLengthValidation.Checked ? (int)_numOrderGlobalLength.Value : 0
                });
            }
            ApplyOrderGlobalLengthToGrid(false);
            SaveSelectedOrderTemplateDraft();
            UpdateOrderToggleAllState();
            RefreshOrderTemplateCards();
            MarkOrderEditorDirty();
        }

        private void AddOrderDataSourceRow(DataSourceItem source)
        {
            var lockEnabled = source.IsLocked || source.LockAfterInput || source.AutoIncrement;
            var displayStep = source.AutoIncrement ? source.AutoStep : 0;
            var rowIndex = _orderDataSourcesGrid.Rows.Add(source.Enabled, source.UseLocalDataValidation, source.Field, lockEnabled,
                "",
                displayStep, source.LockedValue, source.ExpectedLength);
            _orderDataSourcesGrid.Rows[rowIndex].Cells["ExpectedLength"].Tag = source.LengthEdited;
            UpdateOrderDataSourceRowState(_orderDataSourcesGrid.Rows[rowIndex]);
        }

        private void SaveOrderFromPage()
        {
            if (!ValidateOrderDataSourceGrid()) return;
            if (!SaveSelectedOrderTemplateDraft()) return;
            if (!ValidateOrderTemplateDrafts()) return;
            var input = new OrderInput
            {
                Customer = _txtOrderCustomer.Text.Trim(),
                ProductModel = _txtOrderModel.Text.Trim(),
                Color = _txtOrderColor.Text.Trim(),
                OrderNumber = _txtOrderNumber.Text.Trim()
            };
            if (new[] { input.Customer, input.ProductModel, input.Color, input.OrderNumber }.Any(string.IsNullOrWhiteSpace) || _orderTemplateDrafts.Count == 0)
            { MessageBox.Show(this, "客户、机型、颜色、订单号和模板都不能为空。", "添加订单", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (_orderTemplateDrafts.Any(template => string.IsNullOrWhiteSpace(template.SourcePath) || !File.Exists(template.SourcePath)))
            { MessageBox.Show(this, "模板文件不存在。", "添加订单", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            var previousOrderKey = _editingOrder?.Key;
            var targetKey = PackagingOrder.BuildKey(input.Customer, input.ProductModel, input.Color, input.OrderNumber);
            if (!string.Equals(previousOrderKey, targetKey, StringComparison.OrdinalIgnoreCase) && _orders.Contains(input.Customer, input.ProductModel, input.Color, input.OrderNumber))
            { MessageBox.Show(this, "相同客户、机型、颜色和订单号的订单已存在。", "添加订单", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (_orderTemplateDrafts.Any(template => template.Settings?.DataSources == null || !template.Settings.DataSources.Any(source => source.Enabled)))
            { MessageBox.Show(this, "请至少选择一个数据源。", "添加订单", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            var missingLockValue = _orderTemplateDrafts
                .SelectMany(template => template.Settings?.DataSources ?? new List<DataSourceItem>())
                .FirstOrDefault(source => source.Enabled && source.LockAfterInput && string.IsNullOrWhiteSpace(source.LockedValue));
            if (missingLockValue != null)
            { MessageBox.Show(this, $"已勾选锁定的数据源“{missingLockValue.Name}”必须填写输入值。", "订单设置", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            var savedOrder = new PackagingOrder
            {
                Customer = input.Customer,
                ProductModel = input.ProductModel,
                Color = input.Color,
                OrderNumber = input.OrderNumber
            };
            SaveCurrentTemplateSettings();
            try
            {
                foreach (var draft in _orderTemplateDrafts)
                {
                    var template = _orders.CreateTemplateReference(draft.SourcePath, draft.Id);
                    template.Settings = CloneTemplateSettings(draft.Settings);
                    template.Settings.Scope = "OrderTemplate";
                    template.Settings.OrderId = savedOrder.OrderId;
                    template.Settings.TemplateId = template.Id;
                    template.FieldSnapshot = (draft.FieldSnapshot ?? new List<string>()).ToList();
                    template.Settings.TemplateFields = template.FieldSnapshot.ToList();
                    template.Settings.TemplateName = Path.GetFileName(template.SourcePath);
                    template.Settings.TemplatePath = template.SourcePath;
                    var localData = GetTemplateLocalData(template.Settings);
                    if (localData.Count > 0)
                    {
                        template.Settings.LocalDataStoragePath = SaveValidationDataSnapshot(savedOrder.OrderId, template.Id, template.SourcePath, localData);
                        template.Settings.LocalData = new List<string>();
                    }
                    savedOrder.Templates.Add(template);
                }
                _orders.Add(savedOrder, previousOrderKey);
            }
            catch (Exception ex)
            {
                LoggerService.Error("保存订单模板失败", ex);
                MessageBox.Show(this, $"保存订单模板失败：{ex.Message}", "添加订单", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            RefreshOrderFilters(OrderFilterLevel.All, false);
            _editingOrder = savedOrder;
            SelectOrder(savedOrder);
            var applied = ApplyOrder(savedOrder, false, _selectedOrderTemplateDraft?.Id);
            ShowOrderSettingsPage(savedOrder);
            _orderEditorDirty = false;
            AddLog($"已保存订单设置: {savedOrder.DisplayName}", "SUCCESS");
            MessageBox.Show(this,
                applied ? "订单设置已保存，打印页面已加载最新模板设置。" : "订单设置已保存，打印页面暂未切换到该订单，请检查模板文件。",
                "订单设置", MessageBoxButtons.OK, applied ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private bool ValidateOrderTemplateDrafts()
        {
            foreach (var template in _orderTemplateDrafts)
            foreach (var source in template.Settings?.DataSources ?? new List<DataSourceItem>())
            {
                if (!source.LockAfterInput && !source.IsLocked && !source.AutoIncrement) continue;
                if (source.AutoStep < -99 || source.AutoStep > 99)
                {
                    MessageBox.Show(this, $"模板“{template.DisplayName}”的数据源“{source.Name}”步长范围必须为 -99 到 99。", "订单设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
            }
            return true;
        }

        private bool ValidateOrderDataSourceGrid()
        {
            if (_orderDataSourcesGrid == null) return true;
            foreach (DataGridViewRow row in _orderDataSourcesGrid.Rows)
            {
                if (row.IsNewRow) continue;
                if (!ToBoolean(row.Cells["LockEnabled"].Value)) continue;
                if (!int.TryParse(row.Cells["AutoStep"].Value?.ToString(), out _))
                {
                    MessageBox.Show(this, $"数据源“{row.Cells["Field"].Value}”已锁定，请填写数字步长。正数为增序，负数为降序，0 为锁定不变。", "订单设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
                var step = int.Parse(row.Cells["AutoStep"].Value?.ToString() ?? "0");
                if (step < -99 || step > 99)
                {
                    MessageBox.Show(this, $"数据源“{row.Cells["Field"].Value}”的步长范围必须为 -99 到 99。正数为增序，负数为降序，0 为锁定不变。", "订单设置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
            }
            return true;
        }

        private void SyncPrintOrderSelection(PackagingOrder order)
        {
            if (_cmbPrintOrderNumber == null || order == null) return;
            _loadingPrintOrderFilters = true;
            try
            {
                SelectPrintOrder(order);
            }
            finally { _loadingPrintOrderFilters = false; }
        }

        private List<DataSourceItem> BuildDataSourcesFromOrderGrid()
        {
            var result = new List<DataSourceItem>();
            var original = _selectedOrderTemplateDraft?.Settings?.DataSources ?? new List<DataSourceItem>();
            foreach (DataGridViewRow row in _orderDataSourcesGrid.Rows)
            {
                if (row.IsNewRow) continue;
                var field = row.Cells["Field"].Value?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(field)) continue;
                var existing = original.FirstOrDefault(item => string.Equals(item.Field, field, StringComparison.OrdinalIgnoreCase));
                int.TryParse(row.Cells["AutoStep"].Value?.ToString(), out var step);
                int.TryParse(row.Cells["ExpectedLength"].Value?.ToString(), out var expectedLength);
                var hasIndividualLength = row.Cells["ExpectedLength"].Tag is bool value && value;
                var lockEnabled = ToBoolean(row.Cells["LockEnabled"].Value);
                var autoIncrement = lockEnabled && step != 0;
                var lockedValue = row.Cells["LockedValue"].Value?.ToString() ?? "";
                result.Add(new DataSourceItem
                {
                    Name = field,
                    Field = field,
                    Enabled = ToBoolean(row.Cells["Enabled"].Value),
                    UseLocalDataValidation = ToBoolean(row.Cells["UseLocalDataValidation"].Value),
                    AutoIncrement = autoIncrement,
                    AutoStep = lockEnabled ? Math.Max(-99, Math.Min(99, step)) : 0,
                    IsLocked = lockEnabled && !string.IsNullOrWhiteSpace(lockedValue),
                    LockAfterInput = lockEnabled,
                    LockedValue = lockEnabled ? lockedValue : "",
                    AutoIncrementLocked = autoIncrement && existing?.AutoIncrementLocked == true,
                    ExpectedLength = hasIndividualLength ? Math.Max(0, Math.Min(512, expectedLength)) : 0,
                    LengthRevision = existing?.LengthRevision ?? 0,
                    LengthEdited = hasIndividualLength
                });
            }
            return result;
        }


        private async void MainForm_Shown(object sender, EventArgs e)
        {
            AddLog("正在连接 BarTender...", "INFO");
            try
            {
                var historyTask = Task.Run(() => _history.Load());
                var connectTask = Task.Run(() => _btService.Connect());
                var printersTask = Task.Run(() => _btService.GetPrinters());
                var templatesTask = Task.Run(() => GetTemplateFiles(_templatesFolder));

                await Task.WhenAll(historyTask, connectTask, printersTask, templatesTask);
                if (IsDisposed) return;

                ApplyBarTenderConnection(connectTask.Result);
                PopulateTemplateList(templatesTask.Result);
                PopulatePrinters(printersTask.Result);
                RestoreStartupContext();
                LoadHistory();
                RefreshStats();
                _isInitializing = false;

                RestoreApplicationStateControls();

                AddLog("系统启动完成", "INFO");
            }
            catch (Exception ex)
            {
                _isInitializing = false;
                SetStatus("初始化失败，请查看日志");
                AddLog($"系统初始化失败: {ex.Message}", "ERROR");
            }
        }

        #region BarTender

        private void ApplyBarTenderConnection(bool connected)
        {
            lblConnection.Text = connected ? "BarTender 在线" : "离线模式";
            lblConnection.ForeColor = connected ? MiuiTheme.Success : MiuiTheme.Warning;
            if (connected)
            {
                SetStatus("BarTender 已连接");
                AddLog("BarTender 已连接", "SUCCESS");
                btnPrint.Enabled = true;
                btnPrint.Text = "打印";
            }
            else
            {
                SetStatus("离线模式 - BarTender 未安装");
                AddLog("BarTender 未安装，进入离线模式", "WARNING");
                btnPrint.Enabled = false;
                btnPrint.Text = "打印（需要安装 BarTender）";
            }
        }

        private void btnDiagnostics_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedTemplatePath) || !File.Exists(_selectedTemplatePath))
            {
                AddLog("请先选择一个模板文件", "WARNING");
                return;
            }

            AddLog("开始运行 BarTender 诊断...", "INFO");
            RunSta(() =>
            {
                try
                {
                    _btService.RunDiagnostics(_selectedTemplatePath);
                    BeginInvoke((Action)(() =>
                    {
                        AddLog("诊断完成，请查看日志文件获取详细信息", "INFO");
                        AddLog($"日志文件: {LoggerService.GetLogFile()}", "INFO");
                    }));
                }
                catch (Exception ex)
                {
                    BeginInvoke((Action)(() => AddLog($"诊断失败: {ex.Message}", "ERROR")));
                }
            });
        }

        #endregion

        #region Printer

        private async Task RefreshPrintersAsync()
        {
            var printers = await Task.Run(() => _btService.GetPrinters());
            PopulatePrinters(printers);
        }

        private void PopulatePrinters(string[] printers)
        {
            _availablePrinters.Clear();
            foreach (var printer in printers ?? Array.Empty<string>())
                if (!string.IsNullOrWhiteSpace(printer)) _availablePrinters.Add(printer);
            var configuredPrinter = _activeOrderTemplate?.Settings?.Printer;
            if (string.IsNullOrWhiteSpace(configuredPrinter)) configuredPrinter = cmbPrinter.SelectedItem?.ToString();
            if (string.IsNullOrWhiteSpace(configuredPrinter)) configuredPrinter = IniReadValue("General", "Printer", _configFile);
            var wasLoading = _isLoadingConfig;
            _isLoadingConfig = true;
            try
            {
                cmbPrinter.Items.Clear();
                foreach (var printer in printers ?? Array.Empty<string>()) cmbPrinter.Items.Add(printer);
                if (!string.IsNullOrWhiteSpace(configuredPrinter))
                {
                    if (!cmbPrinter.Items.Contains(configuredPrinter)) cmbPrinter.Items.Add(configuredPrinter);
                    cmbPrinter.SelectedItem = configuredPrinter;
                }
                else if (cmbPrinter.Items.Count > 0)
                {
                    cmbPrinter.SelectedIndex = 0;
                }
            }
            finally { _isLoadingConfig = wasLoading; }
        }

        private async void btnRefreshPrinter_Click(object sender, EventArgs e) => await RefreshPrintersAsync();

        #endregion

        #region Template

        private static string NormalizeStartupTemplatePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            try { return Path.GetFullPath(path.Trim('"')); }
            catch { return path.Trim('"'); }
        }

        private void ApplyStartupTemplateSelection()
        {
            if (string.IsNullOrEmpty(_startupTemplatePath) || !File.Exists(_startupTemplatePath)) return;
            _templatesFolder = Path.GetDirectoryName(_startupTemplatePath) ?? "";
            txtTemplateDir.Text = _templatesFolder;
            PopulateTemplateList(GetTemplateFiles(_templatesFolder));

            var match = cmbTemplate.Items.Cast<TemplateItem>()
                .FirstOrDefault(item => string.Equals(item.FullPath, _startupTemplatePath, StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                match = new TemplateItem(Path.GetFileName(_startupTemplatePath), _startupTemplatePath);
                cmbTemplate.Items.Add(match);
            }
            cmbTemplate.SelectedItem = match;
            _selectedTemplatePath = match.FullPath;
            lblSelectedTemplate.Text = match.Name;
            SaveTemplateFolderConfig();
            AddLog($"已通过右键菜单打开模板: {match.Name}", "INFO");
        }

        private void RestoreStartupContext()
        {
            if (!string.IsNullOrEmpty(_startupTemplatePath) && File.Exists(_startupTemplatePath))
            {
                ApplyStartupTemplateSelection();
                RestoreSelectedTemplate();
                return;
            }

            var recentOrder = _orders.Orders.FirstOrDefault(order =>
                string.Equals(order.OrderId, _applicationState.ActiveOrderId, StringComparison.OrdinalIgnoreCase));
            if (recentOrder != null && ApplyOrder(recentOrder, false, _applicationState.ActiveTemplateId))
            {
                SelectOrder(recentOrder);
                AddLog($"已恢复最近订单: {recentOrder.DisplayName}", "INFO");
                return;
            }

            if (!string.IsNullOrWhiteSpace(_applicationState.SelectedTemplatePath) && File.Exists(_applicationState.SelectedTemplatePath))
            {
                SelectStandaloneTemplate(_applicationState.SelectedTemplatePath);
                RestoreSelectedTemplate();
                AddLog($"已恢复最近模板: {Path.GetFileName(_applicationState.SelectedTemplatePath)}", "INFO");
                return;
            }

            RestoreSelectedTemplate();
        }

        private void SelectStandaloneTemplate(string templatePath)
        {
            _activeOrder = null;
            _activeOrderTemplate = null;
            _templatesFolder = Path.GetDirectoryName(templatePath) ?? "";
            txtTemplateDir.Text = _templatesFolder;
            PopulateTemplateList(GetTemplateFiles(_templatesFolder));
            var match = cmbTemplate.Items.Cast<TemplateItem>()
                .FirstOrDefault(item => string.Equals(item.FullPath, templatePath, StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                match = new TemplateItem(Path.GetFileName(templatePath), templatePath);
                cmbTemplate.Items.Add(match);
            }
            cmbTemplate.SelectedItem = match;
            _selectedTemplatePath = match.FullPath;
            lblSelectedTemplate.Text = match.Name;
        }

        private void RestoreSelectedTemplate()
        {
            if (string.IsNullOrEmpty(_selectedTemplatePath) || !File.Exists(_selectedTemplatePath) || !_btService.IsConnected) return;
            var item = cmbTemplate.SelectedItem as TemplateItem;
            if (_activeOrderTemplate != null)
                ApplyTemplateSettings(_activeOrderTemplate.Settings ?? new TemplateSettings());
            else if (item == null || !RestoreTemplateSettings(item.Name, item.FullPath))
                LoadTemplateDataSources(_selectedTemplatePath);
        }

        private void RestoreApplicationStateControls()
        {
            if (_applicationState.SchemaVersion <= 0)
            {
                UpdateEffectiveSummary();
                return;
            }
            var wasLoading = _isLoadingConfig;
            _isLoadingConfig = true;
            try
            {
                numCopies.Value = Math.Max(numCopies.Minimum, Math.Min(numCopies.Maximum, _applicationState.Copies));
                if (!string.IsNullOrWhiteSpace(_applicationState.Printer) && _availablePrinters.Contains(_applicationState.Printer))
                    cmbPrinter.SelectedItem = _applicationState.Printer;
                else if (!_availablePrinters.Contains(cmbPrinter.SelectedItem?.ToString() ?? "") && _availablePrinters.Count > 0)
                    cmbPrinter.SelectedItem = cmbPrinter.Items.Cast<object>().FirstOrDefault(item => _availablePrinters.Contains(item?.ToString() ?? ""));
            }
            finally { _isLoadingConfig = wasLoading; }

            if (_chkPreview.Enabled && _applicationState.PreviewEnabled)
                _chkPreview.Checked = true;
            UpdateEffectiveSummary();
        }

        private void btnBrowseDir_Click(object sender, EventArgs e)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                if (Directory.Exists(_templatesFolder)) fbd.SelectedPath = _templatesFolder;
                if (fbd.ShowDialog(this) == DialogResult.OK)
                {
                    _templatesFolder = fbd.SelectedPath;
                    txtTemplateDir.Text = _templatesFolder;
                    PopulateTemplateList(_templatesFolder);
                }
            }
        }

        private void PopulateTemplateList(string folder)
        {
            PopulateTemplateList(GetTemplateFiles(folder));
        }

        private static string[] GetTemplateFiles(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return new string[0];
            try { return Directory.GetFiles(folder, "*.btw"); }
            catch { return new string[0]; }
        }

        private void PopulateTemplateList(string[] files)
        {
            cmbTemplate.Items.Clear();
            foreach (var f in files)
                cmbTemplate.Items.Add(new TemplateItem(Path.GetFileName(f), f));
            if (cmbTemplate.Items.Count > 0) cmbTemplate.SelectedIndex = 0;
            else { lblSelectedTemplate.Text = "未找到模板"; _selectedTemplatePath = ""; }
        }

        private void cmbTemplate_SelectedIndexChanged(object sender, EventArgs e)
        {
            var item = cmbTemplate.SelectedItem as TemplateItem;
            if (item == null) return;
            if (_loadingOrderTemplate) return;
            if (!_isInitializing && !string.IsNullOrEmpty(_selectedTemplatePath)) SaveCurrentTemplateSettings();
            var orderTemplate = _activeOrder?.Templates?.FirstOrDefault(template => string.Equals(template.SourcePath, item.FullPath, StringComparison.OrdinalIgnoreCase));
            if (orderTemplate != null && !File.Exists(orderTemplate.SourcePath))
            {
                MessageBox.Show(this, "订单模板绝对路径无效，请在订单管理页面重新选择模板。", "订单模板", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                RestorePreviousTemplateSelection();
                return;
            }
            if (!_loadingOrderTemplate && orderTemplate != null && !ResolveTemplateUpdate(_activeOrder, orderTemplate))
            {
                RestorePreviousTemplateSelection();
                return;
            }
            _selectedTemplatePath = item.FullPath;
            _activeOrderTemplate = orderTemplate;
            if (orderTemplate == null && _activeOrder != null)
            {
                _activeOrder = null;
                _loadingPrintOrderFilters = true;
                try
                {
                    _cmbPrintCustomer.SelectedIndex = -1;
                    _cmbPrintModel.SelectedIndex = -1;
                    _cmbPrintColor.SelectedIndex = -1;
                    _cmbPrintOrderNumber.SelectedIndex = -1;
                    ClearOrderSelection();
                }
                finally { _loadingPrintOrderFilters = false; }
            }
            lblSelectedTemplate.Text = item.Name;

            if (_isInitializing || _isLoadingConfig) return;

            var restored = orderTemplate != null;
            if (restored) ApplyTemplateSettings(orderTemplate.Settings ?? new TemplateSettings());
            else restored = RestoreTemplateSettings(item.Name, item.FullPath);
            if (!restored) ResetTemplateState();
            LoadHistory();
            RefreshStats();
            if (!restored) LoadTemplateDataSources(_selectedTemplatePath);
            if (_chkPreview?.Checked == true) _ = RefreshPreviewAsync();
        }

        private void RestorePreviousTemplateSelection()
        {
            _loadingOrderTemplate = true;
            try
            {
                var previous = cmbTemplate.Items.Cast<TemplateItem>().FirstOrDefault(template => string.Equals(template.FullPath, _selectedTemplatePath, StringComparison.OrdinalIgnoreCase));
                if (previous != null) cmbTemplate.SelectedItem = previous;
            }
            finally { _loadingOrderTemplate = false; }
        }

        private void LoadTemplateDataSources(string path)
        {
            if (!_btService.IsConnected)
            {
                AddLog("离线模式：请手动配置数据源", "INFO");
                return;
            }
            var requestVersion = ++_dataSourceLoadVersion;
            var existingSources = (_legacyDataSourcesPending.Count > 0 ? _legacyDataSourcesPending : _dataSources)
                .Select(CloneDataSource).ToList();
            Task.Run(() =>
            {
                try
                {
                    var names = _btService.GetTemplateDataSources(path);
                    PostToUi(() =>
                    {
                        if (requestVersion != _dataSourceLoadVersion || !string.Equals(path, _selectedTemplatePath, StringComparison.OrdinalIgnoreCase)) return;
                        if (names.Count == 0) return;
                        var previousSources = _dataSources.Select(CloneDataSource).ToList();
                        _dataSources = MergeTemplateDataSources(names, existingSources);
                        UpdateLengthRevisions(previousSources, _dataSources);
                        _legacyDataSourcesPending.Clear();
                        _hasSavedDataSourceOrder = true;
                        RebuildInputFields();
                        SaveConfig();
                        SaveCurrentTemplateSettings();
                        AddLog($"已静默同步 {names.Count} 个模板数据源，启用 {_dataSources.Count(source => source.Enabled)} 个", "SUCCESS");
                    });
                }
                catch (Exception ex)
                {
                    PostToUi(() => AddLog($"读取模板数据源失败: {ex.Message}", "ERROR"));
                }
            });
        }

        internal static List<DataSourceItem> MergeTemplateDataSources(IEnumerable<string> fields, IEnumerable<DataSourceItem> existingSources)
        {
            var existing = (existingSources ?? new List<DataSourceItem>()).ToList();
            var templateFields = (fields ?? new List<string>())
                .Where(field => !string.IsNullOrWhiteSpace(field))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var fieldSet = new HashSet<string>(templateFields, StringComparer.OrdinalIgnoreCase);
            var merged = existing
                .Where(source => source != null && !string.IsNullOrWhiteSpace(source.Field) && fieldSet.Contains(source.Field))
                .GroupBy(source => source.Field, StringComparer.OrdinalIgnoreCase)
                .Select(group => CloneDataSource(group.First()))
                .ToList();
            merged.AddRange(templateFields
                .Where(field => !merged.Any(source => string.Equals(source.Field, field, StringComparison.OrdinalIgnoreCase)))
                .Select(field => new DataSourceItem { Name = field, Field = field, Enabled = true }));
            return merged;
        }

        private void ResetTemplateState()
        {
            _dataSources = new List<DataSourceItem>();
            _hasSavedDataSourceOrder = false;
            _useLocalDataValidation = false;
            _duplicateValidationEnabled = true;
            _lengthValidationEnabled = false;
            _globalExpectedLength = 0;
            _globalLengthRevision = 0;
            _lengthRevisionCounter = 0;
            _localDataPath = "";
            _localDataStoragePath = "";
            _localDataColumnName = "";
            _localDataTargetField = "";
            _localData.Clear();
            _isLoadingConfig = true;
            try
            {
                chkUseLocalData.Checked = false;
                chkDuplicateValidation.Checked = true;
                chkLengthValidation.Checked = false;
                btnGlobalLength.Enabled = false;
                numCopies.Value = 1;
                if (cmbPrinter.Items.Count > 0) cmbPrinter.SelectedIndex = 0;
                UpdateLocalDataLabel("");
                RebuildInputFields();
            }
            finally { _isLoadingConfig = false; }
        }

        private class TemplateItem
        {
            public string Name, FullPath;
            public TemplateItem(string n, string p) { Name = n; FullPath = p; }
            public override string ToString()
            {
                var folder = Path.GetFileName(Path.GetDirectoryName(FullPath) ?? "");
                return string.IsNullOrWhiteSpace(folder) ? Name : $"{Name}  [{folder}]";
            }
        }

        #endregion

        #region Data Source

        private void btnEditDataSources_Click(object sender, EventArgs e)
        {
            var fields = new List<string>();
            if (!string.IsNullOrEmpty(_selectedTemplatePath) && File.Exists(_selectedTemplatePath) && _btService.IsConnected)
                fields = _btService.GetTemplateDataSources(_selectedTemplatePath);
            if (fields.Count == 0) fields = _dataSources.Select(d => d.Field).ToList();
            if (fields.Count == 0)
            {
                var result = PromptForManualDataSources();
                if (result != null && result.Count > 0) fields = result;
                else fields = new List<string> { "IMEI1" };
            }
            var previousSources = _dataSources.ToList();
            using (var dlg = new DataSourceSelectDialog(fields, _dataSources, true, _lengthValidationEnabled, _globalExpectedLength))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _dataSources = dlg.SelectedSources;
                    UpdateLengthRevisions(previousSources, _dataSources);
                    _hasSavedDataSourceOrder = true;
                    RebuildInputFields();
                    SaveConfig();
                    SaveCurrentTemplateSettings();
                }
            }
        }

        private List<string> PromptForManualDataSources()
        {
            using (var f = new Form())
            {
                f.Text = "手动添加数据源";
                f.Size = new Size(350, 250);
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterParent;
                f.MaximizeBox = false; f.MinimizeBox = false;
                var lbl = new Label { Text = "输入数据源字段名（每行一个）：", Location = new Point(10, 10), Size = new Size(320, 20) };
                var txt = new TextBox { Location = new Point(10, 35), Size = new Size(315, 150), Multiline = true, ScrollBars = ScrollBars.Vertical };
                txt.Text = "IMEI1\nDS1";
                var ok = new Button { Text = "确定", Location = new Point(170, 195), Size = new Size(75, 25), DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "取消", Location = new Point(255, 195), Size = new Size(75, 25), DialogResult = DialogResult.Cancel };
                f.Controls.AddRange(new Control[] { lbl, txt, ok, cancel });
                f.AcceptButton = ok; f.CancelButton = cancel;
                StyleDialog(f, ok, cancel);
                return f.ShowDialog(this) == DialogResult.OK ? txt.Text.Split('\n').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList() : null;
            }
        }

        private TemplateSettings BuildTemplateSettings(string templatePath, List<DataSourceItem> dataSources)
        {
            return new TemplateSettings
            {
                Scope = _activeOrderTemplate == null ? "GlobalTemplate" : "OrderTemplate",
                OrderId = _activeOrder?.OrderId ?? "",
                TemplateId = _activeOrderTemplate?.Id ?? "",
                TemplateName = Path.GetFileName(templatePath),
                TemplatePath = templatePath,
                Printer = cmbPrinter.SelectedItem?.ToString() ?? "",
                Copies = (int)numCopies.Value,
                InputValidation = _useLocalDataValidation,
                DuplicateValidation = _duplicateValidationEnabled,
                LengthValidation = _lengthValidationEnabled,
                GlobalExpectedLength = _globalExpectedLength,
                GlobalLengthRevision = _globalLengthRevision,
                LengthRevisionCounter = _lengthRevisionCounter,
                LocalDataPath = _localDataPath,
                LocalDataStoragePath = _localDataStoragePath,
                LocalDataColumnName = _localDataColumnName,
                LocalDataTargetField = _localDataTargetField,
                TemplateFields = _activeOrderTemplate?.FieldSnapshot?.ToList() ?? _dataSources.Select(source => source.Field).ToList(),
                LocalData = string.IsNullOrWhiteSpace(_localDataStoragePath) ? _localData.ToList() : new List<string>(),
                DataSources = dataSources.Select(CloneDataSource).ToList()
            };
        }

        private class OrderInput
        {
            public string Customer;
            public string ProductModel;
            public string Color;
            public string OrderNumber;
        }

        #endregion

        #region Dynamic Input Fields

        private Dictionary<string, string> GetCurrentInputValues()
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var enabled = _dataSources.Where(d => d.Enabled).ToList();
            for (int i = 0; i < enabled.Count && i < _inputTextBoxes.Length; i++)
                values[enabled[i].Field] = _inputTextBoxes[i]?.Text ?? "";
            return values;
        }

        private void ShowDataSourceInputDialog(Dictionary<string, string> existingValues)
        {
            var enabled = _dataSources.Where(d => d.Enabled).ToList();
            if (!ValidateConfiguredInputs(enabled, existingValues)) return;
            var editable = enabled.Where(source =>
            {
                if (source.IsLocked || source.AutoIncrementLocked) return false;
                return existingValues == null || !existingValues.TryGetValue(source.Field, out var value) || string.IsNullOrWhiteSpace(value);
            }).ToList();
            var acceptedValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in enabled)
            {
                var lockedValue = source.LockedValue ?? "";
                if (existingValues != null && existingValues.TryGetValue(source.Field, out var currentValue))
                    lockedValue = currentValue;
                if (!string.IsNullOrWhiteSpace(lockedValue)) acceptedValues.Add(lockedValue.Trim());
            }
            for (int i = 0; i < editable.Count; i++)
            {
                var source = editable[i];
                var existingValue = "";
                if (existingValues != null)
                    existingValues.TryGetValue(source.Field, out existingValue);
                var expectedLength = GetExpectedLength(source);
                var inputIndex = enabled.FindIndex(d => string.Equals(d.Field, source.Field, StringComparison.OrdinalIgnoreCase));
                using (var dlg = new DataSourceInputDialog(source, existingValue ?? "", i + 1, editable.Count, expectedLength,
                    value =>
                    {
                        var message = GetDuplicateValidationMessage(source, value, acceptedValues);
                        if (!string.IsNullOrEmpty(message) && inputIndex >= 0 && inputIndex < _inputTextBoxes.Length)
                            _inputTextBoxes[inputIndex].Text = "";
                        return message;
                    }))
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    if (inputIndex >= 0 && inputIndex < _inputTextBoxes.Length)
                        _inputTextBoxes[inputIndex].Text = dlg.Value;
                    acceptedValues.Add(dlg.Value);
                }
            }

            DoPrint();
        }

        private bool ValidateConfiguredInputs(List<DataSourceItem> enabled, Dictionary<string, string> values)
        {
            var configuredValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in enabled.Where(item => item.IsLocked || item.AutoIncrementLocked))
            {
                var value = source.LockedValue ?? "";
                if (values != null && values.TryGetValue(source.Field, out var currentValue)) value = currentValue;
                value = value.Trim();
                if (string.IsNullOrEmpty(value))
                {
                    MessageBox.Show(this, $"锁定数据源 \"{source.Name}\" 不能为空。请先输入或解除锁定。", "数据源锁定", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }

                var expectedLength = GetExpectedLength(source);
                if (expectedLength > 0 && value.Length != expectedLength)
                {
                    MessageBox.Show(this, $"锁定数据源 \"{source.Name}\" 必须为 {expectedLength} 位。请在数据源配置中修正锁定值。", "长度校验", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
                if (_duplicateValidationEnabled && configuredValues.Contains(value))
                {
                    MessageBox.Show(this, $"锁定数据重复：{value}\n请在数据源配置中修正锁定值。", "数据校验", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
                configuredValues.Add(value);
            }
            return true;
        }

        private void RebuildInputFields()
        {
            foreach (Control control in inputPanel.Controls.Cast<Control>().ToArray())
                control.Dispose();
            inputPanel.Controls.Clear();
            var enabled = _dataSources.Where(d => d.Enabled).ToList();
            _inputTextBoxes = new TextBox[enabled.Count];
            _rowPanels = new Panel[enabled.Count];
            _lockButtons = new Button[enabled.Count];
            var scale = Math.Max(1F, DeviceDpi / 96F);
            int S(int value) => (int)Math.Round(value * scale);
            int y = S(4);
            for (int i = 0; i < enabled.Count; i++)
            {
                var rowPanel = new Panel
                {
                    Location = new Point(0, y),
                    Size = new Size(inputPanel.ClientSize.Width, S(28)),
                    Tag = i,
                    AllowDrop = true,
                    BackColor = Color.Transparent
                };
                rowPanel.DragEnter += RowPanel_DragEnter;
                rowPanel.DragDrop += RowPanel_DragDrop;
                rowPanel.DragOver += RowPanel_DragOver;

                var grip = new Label
                {
                    Text = "≡",
                    Location = new Point(S(2), S(3)),
                    Size = new Size(S(22), S(22)),
                    Cursor = Cursors.Hand,
                    Tag = i,
                    Font = MiuiTheme.DragHandleTextFont,
                    ForeColor = Color.FromArgb(160, 160, 160)
                };
                grip.MouseDown += Grip_MouseDown;

                var lbl = new Label
                {
                    Text = enabled[i].Name + "：",
                    Location = new Point(S(52), S(3)),
                    Size = new Size(S(75), S(20)),
                    TextAlign = ContentAlignment.MiddleRight
                };
                MiuiTheme.StyleLabel(lbl);

                var lockButton = new Button
                {
                    Location = new Point(rowPanel.Width - S(28), S(1)),
                    Size = new Size(S(24), S(24)),
                    Anchor = AnchorStyles.Top | AnchorStyles.Right,
                    Tag = i,
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Default,
                    BackColor = Color.Transparent,
                    AccessibleName = IsInputLocked(enabled[i]) ? "已锁定" : "未锁定"
                };
                lockButton.FlatAppearance.BorderSize = 0;
                lockButton.Paint += LockButton_Paint;
                lockButton.Click += LockButton_Click;

                var txt = new TextBox
                {
                    Location = new Point(S(130), 0),
                    Size = new Size(rowPanel.Width - S(164), S(25)),
                    Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                    Tag = i,
                    Text = enabled[i].IsLocked || enabled[i].AutoIncrementLocked ? enabled[i].LockedValue ?? "" : "",
                    ReadOnly = enabled[i].IsLocked || enabled[i].AutoIncrementLocked,
                    BackColor = enabled[i].IsLocked || enabled[i].AutoIncrementLocked ? SystemColors.Control : MiuiTheme.InputBackground
                };
                MiuiTheme.StyleTextBox(txt);
                txt.BackColor = txt.ReadOnly ? SystemColors.Control : MiuiTheme.InputBackground;
                txt.KeyDown += Input_KeyDown;
                txt.GotFocus += Input_GotFocus;

                rowPanel.Controls.Add(grip);
                rowPanel.Controls.Add(lockButton);
                rowPanel.Controls.Add(lbl);
                rowPanel.Controls.Add(txt);
                inputPanel.Controls.Add(rowPanel);

                _rowPanels[i] = rowPanel;
                _lockButtons[i] = lockButton;
                _inputTextBoxes[i] = txt;
                y += S(32);
            }

            int requiredHeight = Math.Max(S(40), y + S(4));
            int maxHeight = S(180);
            inputPanel.Height = Math.Min(requiredHeight, maxHeight);
            inputPanel.AutoScroll = true;
            inputPanel.AutoScrollMinSize = new Size(0, requiredHeight);

            btnPrint.Top = inputPanel.Bottom + S(8);
            btnPrint.Width = inputPanel.Width;
            tabBottom.Top = btnPrint.Bottom + S(8);
            tabBottom.Height = Math.Max(1, WorkspaceBottom - tabBottom.Top - S(8));
            ClampInputPanelScroll();
        }

        private void ClampInputPanelScroll()
        {
            if (inputPanel == null || !inputPanel.AutoScroll) return;
            var maxScroll = Math.Max(0, inputPanel.AutoScrollMinSize.Height - inputPanel.ClientSize.Height);
            var current = -inputPanel.AutoScrollPosition.Y;
            if (current < 0) inputPanel.AutoScrollPosition = new Point(0, 0);
            else if (current > maxScroll) inputPanel.AutoScrollPosition = new Point(0, maxScroll);
        }

        private void InputPanel_SizeChanged(object sender, EventArgs e)
        {
            var scale = Math.Max(1F, DeviceDpi / 96F);
            int S(int value) => (int)Math.Round(value * scale);
            int w = inputPanel.ClientSize.Width;
            for (int i = 0; i < _rowPanels.Length; i++)
            {
                if (_rowPanels[i] == null) continue;
                _rowPanels[i].Width = w;
                if (i < _inputTextBoxes.Length && _inputTextBoxes[i] != null)
                    _inputTextBoxes[i].Width = Math.Max(S(80), w - S(164));
                if (i < _lockButtons.Length && _lockButtons[i] != null)
                    _lockButtons[i].Left = w - S(28);
            }
        }

        private void LockButton_Paint(object sender, PaintEventArgs e)
        {
            var button = (Button)sender;
            var enabled = _dataSources.Where(d => d.Enabled).ToList();
            var index = (int)button.Tag;
            if (index < 0 || index >= enabled.Count) return;

            var isLocked = IsInputLocked(enabled[index]);
            var color = isLocked ? Color.FromArgb(45, 105, 210) : Color.FromArgb(150, 150, 150);
            using (var pen = new Pen(color, 1.8F) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var brush = new SolidBrush(color))
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                // Based on Heroicons MIT lock-open/lock-closed outline SVGs, redrawn to avoid bundled image files.
                DrawRoundedRectangle(e.Graphics, pen, new RectangleF(5.5F, 10.5F, 13F, 10F), 2.2F);
                if (isLocked)
                    e.Graphics.DrawArc(pen, 7F, 3.5F, 10F, 11F, 180F, 180F);
                else
                    e.Graphics.DrawArc(pen, 10F, 3.5F, 10F, 11F, 190F, 230F);
                e.Graphics.FillEllipse(brush, 11F, 14F, 2.5F, 2.5F);
            }
        }

        private void LockButton_Click(object sender, EventArgs e)
        {
            AddLog("打印页面仅显示锁定状态，请在订单管理页面修改锁定设置。", "INFO");
        }

        private static bool IsInputLocked(DataSourceItem source) => source != null && (source.IsLocked || source.AutoIncrementLocked);

        private static void DrawRoundedRectangle(Graphics graphics, Pen pen, RectangleF bounds, float radius)
        {
            using (var path = new GraphicsPath())
            {
                var diameter = radius * 2F;
                path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180F, 90F);
                path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270F, 90F);
                path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0F, 90F);
                path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90F, 90F);
                path.CloseFigure();
                graphics.DrawPath(pen, path);
            }
        }

        private void Grip_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            var grip = (Label)sender;
            var rowIdx = (int)grip.Tag;
            grip.DoDragDrop(rowIdx, DragDropEffects.Move);
        }

        private void Input_GotFocus(object sender, EventArgs e)
        {
            var txt = (TextBox)sender;
            if (!txt.ReadOnly) return;
            for (int i = 0; i < _inputTextBoxes.Length; i++)
            {
                if (_inputTextBoxes[i] == txt && i + 1 < _inputTextBoxes.Length)
                {
                    var next = _inputTextBoxes[i + 1];
                    if (next != null && !next.ReadOnly) { next.Focus(); next.SelectAll(); }
                    else if (next != null) Input_GotFocus(next, EventArgs.Empty);
                    return;
                }
            }
            for (int i = _inputTextBoxes.Length - 1; i >= 0; i--)
            {
                if (_inputTextBoxes[i] != null && !_inputTextBoxes[i].ReadOnly)
                { _inputTextBoxes[i].Focus(); _inputTextBoxes[i].SelectAll(); return; }
            }
        }

        private void RowPanel_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data?.GetDataPresent(typeof(int)) == true)
                e.Effect = DragDropEffects.Move;
        }

        private void RowPanel_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data?.GetDataPresent(typeof(int)) == true)
                e.Effect = DragDropEffects.Move;
        }

        private void RowPanel_DragDrop(object sender, DragEventArgs e)
        {
            if (e.Data?.GetDataPresent(typeof(int)) != true) return;
            var fromIdx = (int)e.Data.GetData(typeof(int));
            var toPanel = (Panel)sender;
            var toIdx = (int)toPanel.Tag;

            if (fromIdx == toIdx) return;

            var enabled = _dataSources.Where(d => d.Enabled).ToList();
            var savedValues = new Dictionary<string, string>();
            for (int i = 0; i < enabled.Count; i++)
                if (i < _inputTextBoxes.Length && _inputTextBoxes[i] != null)
                    savedValues[enabled[i].Field] = _inputTextBoxes[i].Text;

            var fromItem = enabled[fromIdx];
            enabled.RemoveAt(fromIdx);
            enabled.Insert(toIdx, fromItem);

            var newDs = new List<DataSourceItem>();
            int ei = 0;
            foreach (var ds in _dataSources)
            {
                if (ds.Enabled)
                    newDs.Add(enabled[ei++]);
                else
                    newDs.Add(ds);
            }
            _dataSources = newDs;

            RebuildInputFields();

            var newEnabled = _dataSources.Where(d => d.Enabled).ToList();
            for (int i = 0; i < newEnabled.Count; i++)
                if (i < _inputTextBoxes.Length && _inputTextBoxes[i] != null && savedValues.ContainsKey(newEnabled[i].Field))
                    _inputTextBoxes[i].Text = savedValues[newEnabled[i].Field];

            AddLog($"数据源排序已更新: {fromIdx + 1} -> {toIdx + 1}", "INFO");
        }

        private void Input_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            int idx = (int)((TextBox)sender).Tag;

            int nextIdx = idx + 1;
            while (nextIdx < _inputTextBoxes.Length && _inputTextBoxes[nextIdx].ReadOnly)
                nextIdx++;

            if (nextIdx < _inputTextBoxes.Length)
            { _inputTextBoxes[nextIdx].Focus(); _inputTextBoxes[nextIdx].SelectAll(); }
            else btnPrint.PerformClick();
        }

        private void ClearInputs()
        {
            foreach (var tb in _inputTextBoxes) if (tb != null) tb.Text = "";
            if (_inputTextBoxes.Length > 0) { _inputTextBoxes[0].Focus(); _inputTextBoxes[0].SelectAll(); }
        }

        private void ClearNonAutoIncrementInputs(List<DataSourceItem> enabled)
        {
            for (int i = 0; i < enabled.Count; i++)
            {
                if (i < _inputTextBoxes.Length && _inputTextBoxes[i] != null)
                {
                    if (!enabled[i].AutoIncrement && !enabled[i].IsLocked)
                    {
                        // Clear and enable non-auto-increment fields
                        _inputTextBoxes[i].Text = "";
                        _inputTextBoxes[i].ReadOnly = false;
                        _inputTextBoxes[i].BackColor = MiuiTheme.InputBackground;
                    }
                    else if (enabled[i].IsLocked || enabled[i].AutoIncrement)
                    {
                        _inputTextBoxes[i].Text = enabled[i].LockedValue ?? "";
                        _inputTextBoxes[i].ReadOnly = enabled[i].IsLocked || enabled[i].AutoIncrementLocked;
                        _inputTextBoxes[i].BackColor = SystemColors.Control;
                    }
                }
            }
            foreach (var button in _lockButtons)
            {
                if (button == null) continue;
                button.Enabled = true;
                button.Invalidate();
            }
            // Focus first non-auto-increment field
            for (int i = 0; i < enabled.Count; i++)
            {
                if (i < _inputTextBoxes.Length && !enabled[i].AutoIncrement && !enabled[i].IsLocked)
                {
                    _inputTextBoxes[i].Focus();
                    _inputTextBoxes[i].SelectAll();
                    return;
                }
            }
            // If all fields are auto-increment, focus first field
            if (_inputTextBoxes.Length > 0) { _inputTextBoxes[0].Focus(); _inputTextBoxes[0].SelectAll(); }
        }

        private void AutoIncrementFields(List<DataSourceItem> enabled)
        {
            for (int i = 0; i < enabled.Count; i++)
            {
                if (i < _inputTextBoxes.Length && _inputTextBoxes[i] != null && enabled[i].AutoIncrement)
                {
                    var currentVal = _inputTextBoxes[i].Text.Trim();
                    var step = enabled[i].AutoStep;
                    var newVal = IncrementValue(currentVal, step);
                    _inputTextBoxes[i].Text = newVal;
                    enabled[i].AutoIncrementLocked = true;
                    SaveAutoIncrementPendingValue(enabled[i], newVal);

                    // Lock auto-increment fields after first print
                    _inputTextBoxes[i].ReadOnly = true;
                    _inputTextBoxes[i].BackColor = SystemColors.Control;

                    AddLog($"增序: {enabled[i].Name} {currentVal} -> {newVal}", "INFO");
                }
            }
        }

        private string IncrementValue(string value, int step)
        {
            if (string.IsNullOrEmpty(value)) return value;

            // Find the numeric part at the end of the string
            int i = value.Length - 1;
            while (i >= 0 && char.IsDigit(value[i])) i--;
            i++;

            if (i == value.Length) return value; // No numeric part

            string prefix = value.Substring(0, i);
            string numStr = value.Substring(i);

            if (long.TryParse(numStr, out long num))
            {
                num += step;
                if (num < 0) num = 0;
                // Preserve leading zeros
                string newNumStr = num.ToString().PadLeft(numStr.Length, '0');
                return prefix + newNumStr;
            }

            return value;
        }

        private void SaveAutoIncrementPendingValue(DataSourceItem source, string candidate)
        {
            if (source == null || string.IsNullOrWhiteSpace(candidate)) return;
            if (string.IsNullOrWhiteSpace(source.LockedValue) || IsBetterAutoIncrementPendingValue(candidate, source.LockedValue, source.AutoStep))
                source.LockedValue = candidate;
        }

        private static bool IsBetterAutoIncrementPendingValue(string candidate, string current, int step)
        {
            if (TryGetNumericSuffix(candidate, out var candidatePrefix, out var candidateNumber) &&
                TryGetNumericSuffix(current, out var currentPrefix, out var currentNumber) &&
                string.Equals(candidatePrefix, currentPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return step < 0 ? candidateNumber < currentNumber : candidateNumber > currentNumber;
            }
            return string.Compare(candidate, current, StringComparison.OrdinalIgnoreCase) > 0;
        }

        private static bool TryGetNumericSuffix(string value, out string prefix, out long number)
        {
            prefix = value ?? "";
            number = 0;
            if (string.IsNullOrEmpty(value)) return false;
            int i = value.Length - 1;
            while (i >= 0 && char.IsDigit(value[i])) i--;
            i++;
            if (i == value.Length) return false;
            prefix = value.Substring(0, i);
            return long.TryParse(value.Substring(i), out number);
        }

        #endregion

        #region Local Data Validation

        private void btnLoadLocalData_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog { Filter = "Excel|*.xlsx;*.xls|CSV|*.csv|文本|*.txt|所有|*.*", FilterIndex = 1 })
            {
                if (ofd.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        var ext = Path.GetExtension(ofd.FileName).ToLower();
                        if (ext == ".csv") LoadCsvData(ofd.FileName);
                        else if (ext == ".xlsx" || ext == ".xls") LoadExcelData(ofd.FileName);
                        else LoadTextData(ofd.FileName);
                    }
                    catch (Exception ex) { MessageBox.Show(this, $"加载失败: {ex.Message}"); }
                }
            }
        }

        private void LoadCsvData(string path)
        {
            using (var enumerator = File.ReadLines(path).GetEnumerator())
            {
                if (!enumerator.MoveNext()) { MessageBox.Show(this, "CSV 文件为空"); return; }
                var firstRow = CsvUtils.ParseLine(enumerator.Current);
                var data = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (firstRow.Count <= 1)
                {
                    if (firstRow.Count == 1 && !string.IsNullOrWhiteSpace(firstRow[0])) data.Add(firstRow[0].Trim());
                    while (enumerator.MoveNext())
                    {
                        var cols = CsvUtils.ParseLine(enumerator.Current);
                        if (cols.Count > 0 && !string.IsNullOrWhiteSpace(cols[0])) data.Add(cols[0].Trim());
                    }
                    ApplyLoadedLocalData(path, data, "单列", "CSV");
                    return;
                }
                var colIdx = PromptForColumnSelection(firstRow, Path.GetFileName(path));
                if (colIdx < 0) return;
                while (enumerator.MoveNext())
                {
                    var cols = CsvUtils.ParseLine(enumerator.Current);
                    if (colIdx < cols.Count && !string.IsNullOrWhiteSpace(cols[colIdx]))
                        data.Add(cols[colIdx].Trim());
                }
                ApplyLoadedLocalData(path, data, firstRow[colIdx], "CSV");
            }
        }

        private void LoadExcelData(string path)
        {
            SetStatus("正在加载 Excel...");
            AddLog("正在加载 Excel 数据...", "INFO");

            RunSta(() =>
            {
                try
                {
                    var excelType = Type.GetTypeFromProgID("Excel.Application");
                    if (excelType == null)
                    {
                        BeginInvoke((Action)(() => MessageBox.Show(this, "未安装 Excel，请保存为 CSV 格式后加载")));
                        return;
                    }

                    dynamic excel = null;
                    dynamic wb = null;
                    dynamic ws = null;
                    dynamic usedRange = null;
                    try
                    {
                        excel = Activator.CreateInstance(excelType);
                        excel.Visible = false;
                        excel.DisplayAlerts = false;
                        wb = excel.Workbooks.Open(path, ReadOnly: true);
                        ws = wb.ActiveSheet;
                        usedRange = ws.UsedRange;
                        int rows = usedRange.Rows.Count;
                        int cols = usedRange.Columns.Count;

                        if (rows < 1 || cols < 1)
                        {
                            BeginInvoke((Action)(() => MessageBox.Show(this, "Excel 文件为空")));
                            wb.Close(false); excel.Quit();
                            return;
                        }

                        dynamic allData = usedRange.Value2;
                        if (rows == 1 && cols == 1)
                        {
                            var singleValue = allData?.ToString()?.Trim();
                            var singleValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            if (!string.IsNullOrEmpty(singleValue)) singleValues.Add(singleValue);
                            BeginInvoke((Action)(() =>
                            {
                                ApplyLoadedLocalData(path, singleValues, "单列", "Excel");
                                SetStatus("就绪");
                            }));
                            wb.Close(false); excel.Quit();
                            return;
                        }
                        var headers = new List<string>();
                        for (int c = 1; c <= cols; c++)
                            headers.Add(allData[1, c]?.ToString()?.Trim() ?? $"列{c}");

                        var colIdx = 0;
                        var startRow = 1;
                        var columnName = "单列";
                        if (cols > 1)
                        {
                            var selectedCol = -1;
                            var evt = new System.Threading.ManualResetEvent(false);
                            BeginInvoke((Action)(() =>
                            {
                                selectedCol = PromptForColumnSelection(headers, Path.GetFileName(path));
                                evt.Set();
                            }));
                            evt.WaitOne();
                            colIdx = selectedCol;
                            if (colIdx < 0) { wb.Close(false); excel.Quit(); return; }
                            startRow = 2;
                            columnName = headers[colIdx];
                        }

                        var data = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        for (int r = startRow; r <= rows; r++)
                        {
                            var val = allData[r, colIdx + 1]?.ToString()?.Trim();
                            if (!string.IsNullOrEmpty(val)) data.Add(val);
                        }

                        wb.Close(false); excel.Quit();

                        BeginInvoke((Action)(() =>
                        {
                            ApplyLoadedLocalData(path, data, columnName, "Excel");
                            SetStatus("就绪");
                        }));
                    }
                    finally
                    {
                        try { wb?.Close(false); } catch { }
                        try { excel?.Quit(); } catch { }
                        try { if (usedRange != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(usedRange); } catch { }
                        try { if (ws != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(ws); } catch { }
                        try { if (wb != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(wb); } catch { }
                        try { if (excel != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(excel); } catch { }
                    }
                }
                catch (Exception ex)
                {
                    BeginInvoke((Action)(() => { MessageBox.Show(this, $"读取 Excel 失败: {ex.Message}"); SetStatus("就绪"); }));
                }
                finally
                {
                    try { BeginInvoke((Action)(() => SetStatus("就绪"))); } catch { }
                }
            });
        }

        private void LoadTextData(string path)
        {
            var data = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in File.ReadLines(path))
            { var val = line.Trim(); if (!string.IsNullOrEmpty(val)) data.Add(val); }
            ApplyLoadedLocalData(path, data, "文本", "本地数据");
        }

        private void ApplyLoadedLocalData(string path, HashSet<string> data, string columnName, string sourceType)
        {
            _localData = data;
            _localDataPath = path;
            _localDataStoragePath = SaveValidationDataSnapshot(_activeOrder?.OrderId ?? "global", _activeOrderTemplate?.Id ?? "global", _selectedTemplatePath, data);
            _localDataColumnName = columnName;
            _localDataTargetField = "";
            foreach (var source in _dataSources) source.UseLocalDataValidation = source.Enabled;
            UpdateLocalDataValidationAvailability();
            _useLocalDataValidation = data.Count > 0;
            chkUseLocalData.Checked = _useLocalDataValidation;
            UpdateLocalDataLabel($"已加载: {data.Count} 条 [{columnName}，全部启用字段] ({Path.GetFileName(path)})");
            MessageBox.Show(this, $"校验数据导入完成\n总行数：{data.Count}\n去重后：{data.Count}\n重复数：0\n空值数：0", "校验数据", MessageBoxButtons.OK, MessageBoxIcon.Information);
            AddLog($"加载 {sourceType}: {data.Count} 条, 列: {columnName}", "SUCCESS");
            SaveCurrentConfigurationState();
        }

        private int PromptForColumnSelection(List<string> columns, string fileName)
        {
            using (var f = new Form())
            {
                f.Text = "选择校验列";
                f.Size = new Size(350, 250);
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterParent;
                f.MaximizeBox = false; f.MinimizeBox = false;
                var lbl = new Label { Text = $"文件: {fileName}\n选择用于校验的列：", Location = new Point(10, 10), Size = new Size(320, 40) };
                var lst = new ListBox { Location = new Point(10, 55), Size = new Size(315, 130) };
                foreach (var col in columns) lst.Items.Add(col);
                if (lst.Items.Count > 0) lst.SelectedIndex = 0;
                var ok = new Button { Text = "确定", Location = new Point(170, 195), Size = new Size(75, 25), DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "取消", Location = new Point(255, 195), Size = new Size(75, 25), DialogResult = DialogResult.Cancel };
                f.Controls.AddRange(new Control[] { lbl, lst, ok, cancel });
                f.AcceptButton = ok; f.CancelButton = cancel;
                StyleDialog(f, ok, cancel);
                return f.ShowDialog(this) == DialogResult.OK ? lst.SelectedIndex : -1;
            }
        }

        private void chkUseLocalData_CheckedChanged(object sender, EventArgs e)
        {
            if (_isInitializing || _isLoadingConfig)
            {
                _useLocalDataValidation = chkUseLocalData.Checked && _localData.Count > 0;
                return;
            }
            if (chkUseLocalData.Checked && _localData.Count == 0)
            {
                chkUseLocalData.Checked = false;
                MessageBox.Show(this, "请先导入本地校验数据。", "本地完整匹配", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _useLocalDataValidation = chkUseLocalData.Checked;
            if (!_isInitializing) SaveCurrentConfigurationState();
        }

        private void chkDuplicateValidation_CheckedChanged(object sender, EventArgs e)
        {
            _duplicateValidationEnabled = chkDuplicateValidation.Checked;
            if (!_isInitializing && !_isLoadingConfig) SaveCurrentConfigurationState();
        }

        private void UpdateLocalDataValidationAvailability()
        {
            var hasLocalData = _localData.Count > 0;
            chkUseLocalData.Enabled = hasLocalData;
            if (!hasLocalData)
            {
                _useLocalDataValidation = false;
                chkUseLocalData.Checked = false;
            }
        }

        private void chkLengthValidation_CheckedChanged(object sender, EventArgs e)
        {
            _lengthValidationEnabled = chkLengthValidation.Checked;
            btnGlobalLength.Enabled = chkLengthValidation.Checked;
            if (_isInitializing || _isLoadingConfig) return;
            if (!chkLengthValidation.Checked)
            {
                SaveCurrentConfigurationState();
                return;
            }
            if (_globalExpectedLength == 0 && !PromptForGlobalLength())
            {
                chkLengthValidation.Checked = false;
                return;
            }
            SaveCurrentConfigurationState();
        }

        private void btnGlobalLength_Click(object sender, EventArgs e)
        {
            if (PromptForGlobalLength()) SaveCurrentConfigurationState();
        }

        private void numCopies_ValueChanged(object sender, EventArgs e)
        {
            if (!_isInitializing && !_isLoadingConfig) SaveCurrentConfigurationState();
        }

        private bool PromptForGlobalLength()
        {
            using (var form = new Form())
            {
                form.Text = "设置全局数据长度";
                form.Size = new Size(460, 170);
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterParent;
                form.MaximizeBox = false;
                form.MinimizeBox = false;
                var label = new Label { Text = "请输入一条符合模板要求的样例数据，系统将使用样例位数作为全局长度：", Location = new Point(12, 12), Size = new Size(420, 38) };
                var input = new TextBox { Location = new Point(12, 52), Size = new Size(420, 25), MaxLength = 512 };
                var ok = new Button { Text = "确定", Location = new Point(272, 92), Size = new Size(75, 28), DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "取消", Location = new Point(357, 92), Size = new Size(75, 28), DialogResult = DialogResult.Cancel };
                form.Controls.AddRange(new Control[] { label, input, ok, cancel });
                form.AcceptButton = ok;
                form.CancelButton = cancel;
                StyleDialog(form, ok, cancel);
                form.Shown += (s, e) => input.Focus();
                ok.Click += (s, e) =>
                {
                    if (string.IsNullOrEmpty(input.Text))
                    {
                        MessageBox.Show(form, "样例数据不能为空", "长度校验", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        form.DialogResult = DialogResult.None;
                    }
                };
                if (form.ShowDialog(this) != DialogResult.OK) return false;
                _globalExpectedLength = input.Text.Length;
                _globalLengthRevision = ++_lengthRevisionCounter;
                AddLog($"全局数据长度已设置为 {_globalExpectedLength} 位", "SUCCESS");
                return true;
            }
        }

        private int GetExpectedLength(DataSourceItem source)
        {
            return ValidationService.GetExpectedLength(source, _lengthValidationEnabled, _globalExpectedLength);
        }

        private string GetDuplicateValidationMessage(DataSourceItem source, string value, HashSet<string> acceptedValues)
        {
            if (!_duplicateValidationEnabled) return null;
            if (acceptedValues.Contains(value))
                return $"输入数据重复：{value}\n请重新输入 {source.Name}。";
            if (_history.ContainsAnyValue(Path.GetFileName(_selectedTemplatePath), _selectedTemplatePath, GetCurrentTemplateId(), value))
                return $"该数据已存在于打印历史：{value}\n请重新输入 {source.Name}。";
            return null;
        }

        private void UpdateLengthRevisions(List<DataSourceItem> previous, List<DataSourceItem> current)
        {
            foreach (var source in current)
            {
                var old = previous.FirstOrDefault(item => string.Equals(item.Field, source.Field, StringComparison.OrdinalIgnoreCase));
                if (source.ExpectedLength <= 0)
                {
                    source.LengthRevision = 0;
                }
                else if (source.LengthEdited || old == null || old.ExpectedLength != source.ExpectedLength)
                {
                    source.LengthRevision = ++_lengthRevisionCounter;
                }
                else
                {
                    source.LengthRevision = old.LengthRevision;
                }
            }
        }

        private void UpdateLocalDataLabel(string text)
        {
            // Truncate text if too long
            if (text.Length > 30)
                text = text.Substring(0, 27) + "...";
            if (lblLocalData.InvokeRequired)
                lblLocalData.Invoke((Action)(() => lblLocalData.Text = text));
            else
                lblLocalData.Text = text;
        }

        private bool ValidateLocalData(Dictionary<string, string> fieldValues, HashSet<string> localData = null, IEnumerable<DataSourceItem> sources = null)
        {
            localData ??= _localData;
            if (localData.Count == 0) return true;
            sources ??= _dataSources;
            var notInLocal = ValidationService.FindLocalDataMismatches(fieldValues, localData, sources);
            if (notInLocal.Count > 0)
            {
                var msg = $"以下数据未完整匹配本地校验数据：\n{string.Join("\n", notInLocal)}";
                MessageBox.Show(this, msg, "本地完整匹配", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                AddLog($"本地完整匹配失败: {string.Join(", ", notInLocal)}", "WARNING");
                return false;
            }
            return true;
        }

        #endregion

        #region Print

        private void btnPrint_Click(object sender, EventArgs e)
        {
            if (!CanStartPrint()) return;
            ShowDataSourceInputDialog(GetCurrentInputValues());
        }

        private bool CanStartPrint()
        {
            if (string.IsNullOrEmpty(_selectedTemplatePath) || !File.Exists(_selectedTemplatePath))
            { MessageBox.Show(this, "请先选择模板文件"); return false; }
            if (!_btService.IsConnected)
            { MessageBox.Show(this, "BarTender 未连接，请确认已安装 BarTender"); return false; }
            if (cmbPrinter.SelectedItem == null)
            { MessageBox.Show(this, "请选择打印机"); return false; }
            if (!_dataSources.Any(d => d.Enabled))
            { MessageBox.Show(this, "请配置数据源"); return false; }
            return true;
        }

        private string GetOperatorName()
        {
            var value = _txtOperator?.Text?.Trim();
            return string.IsNullOrWhiteSpace(value) ? Environment.UserName ?? "" : value;
        }

        private string GetTemplateVersion(OrderTemplate template)
        {
            return _printWorkflow.BuildTemplateVersion(template);
        }

        private void DoPrint()
        {
            if (string.IsNullOrEmpty(_selectedTemplatePath) || !File.Exists(_selectedTemplatePath))
            { MessageBox.Show(this, "请先选择模板文件"); return; }
            if (!_btService.IsConnected)
            {
                MessageBox.Show(this, "BarTender 未连接，请确认已安装 BarTender");
                return;
            }
            var printer = cmbPrinter.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(printer)) { MessageBox.Show(this, "请选择打印机"); return; }

            var enabled = _dataSources.Where(d => d.Enabled).ToList();
            if (enabled.Count == 0) { MessageBox.Show(this, "请配置数据源"); return; }

            var fieldValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < enabled.Count; i++)
            {
                var val = _inputTextBoxes[i]?.Text?.Trim() ?? "";
                if (string.IsNullOrEmpty(val) && !enabled[i].IsLocked)
                { MessageBox.Show(this, $"\"{enabled[i].Name}\" 不能为空"); _inputTextBoxes[i]?.Focus(); return; }
                fieldValues[enabled[i].Field] = val;
            }

            if (!ValidateInputValues(enabled, fieldValues)) return;

            if (!ValidateTemplateFieldCoverage(_selectedTemplatePath, _dataSources, fieldValues, "打印字段完整性")) return;

            // Local data validation - only if enabled
            if (_useLocalDataValidation)
            {
                if (!ValidateLocalData(fieldValues))
                { AddLog("用户取消（本地数据校验失败）", "WARNING"); return; }
            }

            int copies = (int)numCopies.Value;
            var templatePath = _selectedTemplatePath;
            var templateName = Path.GetFileName(templatePath);
            var templateVersion = GetTemplateVersion(_activeOrderTemplate);
            var operatorName = GetOperatorName();
            var templateId = GetCurrentTemplateId();
            var orderName = _activeOrder?.DisplayName ?? "";
            var orderId = _activeOrder?.OrderId ?? "";
            var templateFields = _activeOrderTemplate?.FieldSnapshot?.ToList() ?? new List<string>();
            AddPendingPrintValues(templateId, enabled, fieldValues);
            _pendingPrintJobCount++;
            AdvanceSuccessfulPrintState(enabled);
            SetStatus($"打印队列: {_pendingPrintJobCount}");
            AddLog($"打印作业已入队: {string.Join(", ", fieldValues.Select(kv => $"{kv.Key}={kv.Value}"))}", "INFO");
            _ = ProcessQueuedPrintAsync(templateName, templatePath, templateId, fieldValues, printer, copies,
                operatorName, templateVersion, orderName, orderId, templateFields,
                enabled.Where(ShouldTrackPendingValue).Select(source => source.Field).ToList());
        }

        private async Task ProcessQueuedPrintAsync(string templateName, string templatePath, string templateId,
            Dictionary<string, string> fieldValues, string printer, int copies, string operatorName,
            string templateVersion, string orderName, string orderId, List<string> templateFields,
            List<string> pendingFields)
        {
            PrintResult result;
            try
            {
                result = await _btService.PrintAsync(templatePath, fieldValues, printer, copies);
            }
            catch (Exception ex)
            {
                LoggerService.Error("打印失败", ex);
                result = new PrintResult(false, ex.Message, $"type={ex.GetType().Name};template={templatePath};printer={printer};copies={copies};message={ex.Message}");
            }
            PostToUi(() =>
            {
                RemovePendingPrintValues(templateId, pendingFields, fieldValues);
                _pendingPrintJobCount = Math.Max(0, _pendingPrintJobCount - 1);
                if (result.Success)
                {
                    AddLog("打印作业已提交", "SUCCESS");
                    if (!_printWorkflow.RecordPrintResult(_history, templateName, templatePath, templateId, fieldValues,
                        "PASS", printer, copies, operatorName, "", templateVersion, result.DiagnosticDetails,
                        orderName, orderId, templateFields))
                    {
                        AddLog("打印作业已提交，但历史记录保存失败；输入状态已经推进。", "ERROR");
                    }
                }
                else
                {
                    AddLog($"打印提交失败: {result.ErrorMessage}", "ERROR");
                    if (!_printWorkflow.RecordPrintResult(_history, templateName, templatePath, templateId, fieldValues,
                        "FAIL", printer, copies, operatorName, "", templateVersion, result.DiagnosticDetails,
                        orderName, orderId, templateFields))
                        AddLog("失败打印历史记录保存失败。", "ERROR");
                }
                if (_pendingPrintJobCount == 0 && _chkPreview?.Checked == true)
                {
                    var previewValues = result.Success && string.Equals(templatePath, _selectedTemplatePath, StringComparison.OrdinalIgnoreCase)
                        ? new Dictionary<string, string>(fieldValues, StringComparer.OrdinalIgnoreCase)
                        : null;
                    _ = RefreshPreviewAsync(previewValues);
                }
                LoadHistory();
                RefreshStats();
                SetStatus(_pendingPrintJobCount > 0 ? $"打印队列: {_pendingPrintJobCount}" : result.Success ? "就绪" : "打印提交失败");
            });
        }

        private string GetPendingPrintValueKey(string templateId, string value)
        {
            return $"{templateId ?? ""}\u001f{value?.Trim() ?? ""}";
        }

        private static bool ShouldTrackPendingValue(DataSourceItem source)
        {
            return source != null && (!source.IsLocked && !source.AutoIncrementLocked || source.AutoIncrement || source.AutoIncrementLocked);
        }

        private void AddPendingPrintValues(string templateId, List<DataSourceItem> enabled, Dictionary<string, string> fieldValues)
        {
            foreach (var source in enabled.Where(ShouldTrackPendingValue))
            {
                if (!fieldValues.TryGetValue(source.Field, out var value) || string.IsNullOrWhiteSpace(value)) continue;
                var key = GetPendingPrintValueKey(templateId, value);
                _pendingPrintValues.TryGetValue(key, out var count);
                _pendingPrintValues[key] = count + 1;
            }
        }

        private void RemovePendingPrintValues(string templateId, List<string> pendingFields, Dictionary<string, string> fieldValues)
        {
            foreach (var field in pendingFields)
            {
                if (!fieldValues.TryGetValue(field, out var value) || string.IsNullOrWhiteSpace(value)) continue;
                var key = GetPendingPrintValueKey(templateId, value);
                if (!_pendingPrintValues.TryGetValue(key, out var count)) continue;
                if (count <= 1) _pendingPrintValues.Remove(key);
                else _pendingPrintValues[key] = count - 1;
            }
        }

        private bool ValidateInputValues(List<DataSourceItem> enabled, Dictionary<string, string> fieldValues, bool checkDuplicates = true, TemplateSettings settings = null)
        {
            var lengthValidationEnabled = settings?.LengthValidation ?? _lengthValidationEnabled;
            var globalExpectedLength = settings?.GlobalExpectedLength ?? _globalExpectedLength;
            if (lengthValidationEnabled)
            {
                for (int i = 0; i < enabled.Count; i++)
                {
                    fieldValues.TryGetValue(enabled[i].Field, out var value);
                    value ??= "";
                    var expectedLength = enabled[i].ExpectedLength > 0 ? enabled[i].ExpectedLength : globalExpectedLength;
                    if (expectedLength > 0 && value.Length != expectedLength)
                    {
                        MessageBox.Show(this, $"\"{enabled[i].Name}\" 必须为 {expectedLength} 位，当前为 {value.Length} 位。", "长度校验", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        if (settings == null && i < _inputTextBoxes.Length && !_inputTextBoxes[i].ReadOnly)
                        {
                            _inputTextBoxes[i].Text = "";
                            _inputTextBoxes[i].Focus();
                        }
                        return false;
                    }
                }
            }

            var duplicateValidationEnabled = settings?.DuplicateValidation ?? _duplicateValidationEnabled;
            if (!duplicateValidationEnabled || !checkDuplicates) return true;
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < enabled.Count; i++)
            {
                fieldValues.TryGetValue(enabled[i].Field, out var value);
                value ??= "";
                if (string.IsNullOrWhiteSpace(value)) continue;
                var isEditable = !enabled[i].IsLocked && !enabled[i].AutoIncrementLocked;
                var shouldCheckHistory = isEditable || enabled[i].AutoIncrement || enabled[i].AutoIncrementLocked;
                var templatePath = settings?.TemplatePath ?? _selectedTemplatePath;
                var templateName = settings?.TemplateName ?? Path.GetFileName(templatePath);
                var templateId = settings == null ? GetCurrentTemplateId() : GetTemplateIdForPath(templatePath);
                if (seen.ContainsKey(value) || shouldCheckHistory && _pendingPrintValues.ContainsKey(GetPendingPrintValueKey(templateId, value)) ||
                    shouldCheckHistory && _history.ContainsAnyValue(templateName, templatePath, templateId, value))
                {
                    MessageBox.Show(this, $"重复数据：{value}\n请重新输入 {enabled[i].Name}。", "数据校验", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    if (settings == null && isEditable && i < _inputTextBoxes.Length)
                    {
                        _inputTextBoxes[i].Text = "";
                        _inputTextBoxes[i].Focus();
                        _inputTextBoxes[i].SelectAll();
                    }
                    AddLog($"数据重复: {enabled[i].Field}={value}", "WARNING");
                    return false;
                }
                seen[value] = i;
            }
            return true;
        }

        private void AdvanceSuccessfulPrintState(List<DataSourceItem> enabled)
        {
            for (int i = 0; i < enabled.Count && i < _inputTextBoxes.Length; i++)
            {
                if (enabled[i].LockAfterInput && !IsInputLocked(enabled[i]))
                {
                    enabled[i].LockedValue = _inputTextBoxes[i].Text.Trim();
                    if (enabled[i].AutoIncrement)
                        enabled[i].AutoIncrementLocked = true;
                    else
                        enabled[i].IsLocked = true;
                }
            }

            AutoIncrementFields(enabled);
            ClearNonAutoIncrementInputs(enabled);
            SaveConfig();
            SaveCurrentTemplateSettings();
        }

        #endregion

        #region History

        private void txtSearch_TextChanged(object sender, EventArgs e)
        {
            _historyPageIndex = 0;
            _historySearchTimer.Stop();
            _historySearchTimer.Start();
        }
        private void chkExactSearch_CheckedChanged(object sender, EventArgs e) { _historyPageIndex = 0; LoadHistory(); }
        private void btnClearSearch_Click(object sender, EventArgs e) { _historyPageIndex = 0; txtSearch.Text = ""; }
        private void btnClearHistory_Click(object sender, EventArgs e)
        {
            UpdateSession();
            if (!_session.CanDeleteHistory)
            { _dialogs.ShowWarning(this, "当前角色无历史清空权限。", "权限不足"); return; }
            if (string.IsNullOrEmpty(_selectedTemplatePath))
            { MessageBox.Show(this, "请先选择模板"); return; }
            var templateName = Path.GetFileName(_selectedTemplatePath);
            var templateId = GetCurrentTemplateId();
            var activeCount = _history.Count(templateName, _selectedTemplatePath, templateId);
            var warning = $"这是高风险操作，将从当前历史控件、重复校验和各项检测中排除当前模板的 {activeCount} 条活动记录。\n\n原始记录仍保存在数据库、JSONL 和独立历史副本中。确定继续？";
            if (MessageBox.Show(this, warning, "清空历史控件确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                if (!_history.Clear(templateName, _selectedTemplatePath, templateId, GetOperatorName(), "清空当前模板历史控件"))
                { MessageBox.Show(this, "清空历史控件失败，请检查文件权限。", "历史记录", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
                AuditLogger.Append(GetOperatorName(), "ExcludeTemplateHistory", $"template={templateId};count={activeCount}");
                AddLog($"已从历史控件和检测中排除当前模板 {activeCount} 条记录，原始数据已保留", "WARNING");
                LoadHistory(); RefreshStats();
            }
        }
        private void btnExportHistory_Click(object sender, EventArgs e)
        {
            var records = GetCurrentHistoryRecords(true);
            if (records.Count == 0) { MessageBox.Show(this, "当前模板没有可导出的记录"); return; }
            using (var dialog = new FolderBrowserDialog { Description = "选择打印历史 CSV 导出目录", UseDescriptionForTitle = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var templateFields = _dataSources.Select(source => source.Field).ToList();
                    IReadOnlyList<string> paths;
                    try
                    {
                        paths = BusinessHistoryCsvExporter.Export(dialog.SelectedPath, records, _orders.Orders, templateFields, DateTime.Now);
                    }
                    catch (IOException ex) when (ex.Message.StartsWith("导出文件已存在：", StringComparison.Ordinal))
                    {
                        if (MessageBox.Show(this, ex.Message + "\n\n是否覆盖目标目录中本次导出的同名文件？", "确认覆盖", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                            return;
                        paths = BusinessHistoryCsvExporter.Export(dialog.SelectedPath, records, _orders.Orders, templateFields, DateTime.Now, true);
                    }
                    MessageBox.Show(this, $"导出成功，共生成 {paths.Count} 个 CSV 文件。", "导出历史", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void btnImportHistory_Click(object sender, EventArgs e)
        {
            if (dgvHistory.SelectedRows.Count == 0)
            { MessageBox.Show(this, "请先选择一条历史记录"); return; }

            var recordId = dgvHistory.SelectedRows[0].Cells["记录ID"].Value?.ToString() ?? "";
            var record = _history.GetById(recordId);
            if (record == null || record.FieldValues == null || record.FieldValues.Count == 0)
            { MessageBox.Show(this, "该历史记录缺少完整字段数据，无法导入"); return; }

            var fields = ShowHistoryImportDialog(record);
            if (fields == null || fields.Count == 0) return;
            ImportHistoryFields(record, fields);
        }

        private HashSet<string> ShowHistoryImportDialog(PrintRecord record)
        {
            using (var form = new Form())
            {
                form.Text = "导入历史数据";
                form.Size = new Size(520, 430);
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterParent;
                form.MaximizeBox = false;
                form.MinimizeBox = false;

                var label = new Label { Text = "选择要导入到输入框的数据源：", Location = new Point(12, 12), Size = new Size(480, 22) };
                var list = new CheckedListBox { Location = new Point(12, 40), Size = new Size(480, 285), CheckOnClick = true };
                var enabledFields = new HashSet<string>(_dataSources.Where(item => item.Enabled).Select(item => item.Field), StringComparer.OrdinalIgnoreCase);
                foreach (var item in record.FieldValues.Where(item => enabledFields.Contains(item.Key)))
                    list.Items.Add(new HistoryImportField(item.Key, item.Value), true);
                if (list.Items.Count == 0)
                { MessageBox.Show(this, "该历史记录没有匹配当前模板输入框的数据源"); return null; }

                var selectAll = new Button { Text = "全选", Location = new Point(12, 345), Size = new Size(55, 28) };
                selectAll.Click += (s, e) => { for (int i = 0; i < list.Items.Count; i++) list.SetItemChecked(i, true); };
                var selectNone = new Button { Text = "全不选", Location = new Point(75, 345), Size = new Size(65, 28) };
                selectNone.Click += (s, e) => { for (int i = 0; i < list.Items.Count; i++) list.SetItemChecked(i, false); };
                var ok = new Button { Text = "导入", Location = new Point(325, 345), Size = new Size(75, 28), DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "取消", Location = new Point(415, 345), Size = new Size(75, 28), DialogResult = DialogResult.Cancel };
                form.Controls.AddRange(new Control[] { label, list, selectAll, selectNone, ok, cancel });
                form.AcceptButton = ok;
                StyleDialog(form, ok, cancel);
                form.CancelButton = cancel;

                if (form.ShowDialog(this) != DialogResult.OK) return null;
                return new HashSet<string>(list.CheckedItems.Cast<HistoryImportField>().Select(item => item.Field), StringComparer.OrdinalIgnoreCase);
            }
        }

        private void ImportHistoryFields(PrintRecord record, HashSet<string> selectedFields)
        {
            var enabled = _dataSources.Where(item => item.Enabled).ToList();
            var imported = 0;
            for (int i = 0; i < enabled.Count && i < _inputTextBoxes.Length; i++)
            {
                var source = enabled[i];
                if (!selectedFields.Contains(source.Field) || !record.FieldValues.TryGetValue(source.Field, out var value)) continue;
                _inputTextBoxes[i].Text = value ?? "";
                if (source.AutoIncrement)
                {
                    source.LockedValue = _inputTextBoxes[i].Text.Trim();
                    source.AutoIncrementLocked = true;
                    source.IsLocked = false;
                    _inputTextBoxes[i].ReadOnly = true;
                    _inputTextBoxes[i].BackColor = SystemColors.Control;
                }
                else if (source.IsLocked)
                    source.LockedValue = _inputTextBoxes[i].Text.Trim();
                imported++;
            }

            if (imported > 0)
            {
                SaveConfig();
                SaveCurrentTemplateSettings();
                AddLog($"已从历史记录导入 {imported} 个数据源", "SUCCESS");
            }
        }
        private void btnReprintHistory_Click(object sender, EventArgs e)
        {
            if (dgvHistory.SelectedRows.Count == 0)
            { MessageBox.Show(this, "请先选择一条历史记录"); return; }

            var row = dgvHistory.SelectedRows[0];
            var recordId = row.Cells["记录ID"].Value?.ToString() ?? "";
            var record = _history.GetById(recordId);
            if (record == null || record.FieldValues == null || record.FieldValues.Count == 0)
            { MessageBox.Show(this, "该历史记录缺少完整字段数据，无法直接补打印"); return; }

            var printer = ShowReprintConfirmDialog(record);
            if (!string.IsNullOrEmpty(printer)) PrintHistoryRecord(record, printer);
        }

        private string ShowReprintConfirmDialog(PrintRecord record)
        {
            if (cmbPrinter.Items.Count == 0)
            { MessageBox.Show(this, "当前没有可用打印机，无法补打印"); return null; }

            using (var form = new Form())
            {
                form.Text = "确认补打印";
                form.Size = new Size(520, 430);
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterParent;
                form.MaximizeBox = false;
                form.MinimizeBox = false;

                var summary = $"模板: {record.TemplateName}\r\n历史打印机: {record.Printer}\r\n份数: {record.Copies}\r\n\r\n字段数据:";
                var lblDetails = new Label
                {
                    Text = summary,
                    Location = new Point(12, 12),
                    Size = new Size(480, 85),
                    AutoEllipsis = true
                };
                var txtDetails = new TextBox
                {
                    Text = string.Join(Environment.NewLine, record.FieldValues.Select(item => $"{item.Key}: {item.Value}")),
                    Location = new Point(12, 100),
                    Size = new Size(480, 185),
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = ScrollBars.Vertical
                };
                var lblPrinter = new Label { Text = "本次补打印机：", Location = new Point(12, 300), Size = new Size(110, 22) };
                var cmbReprintPrinter = new ComboBox
                {
                    Location = new Point(125, 297),
                    Size = new Size(365, 25),
                    DropDownStyle = ComboBoxStyle.DropDownList
                };
                foreach (var item in cmbPrinter.Items) cmbReprintPrinter.Items.Add(item);

                if (!string.IsNullOrEmpty(record.Printer) && cmbReprintPrinter.Items.Contains(record.Printer))
                    cmbReprintPrinter.SelectedItem = record.Printer;
                else if (cmbPrinter.SelectedItem != null && cmbReprintPrinter.Items.Contains(cmbPrinter.SelectedItem))
                    cmbReprintPrinter.SelectedItem = cmbPrinter.SelectedItem;
                else
                    cmbReprintPrinter.SelectedIndex = 0;

                var ok = new Button { Text = "补打印", Location = new Point(325, 350), Size = new Size(75, 28), DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "取消", Location = new Point(415, 350), Size = new Size(75, 28), DialogResult = DialogResult.Cancel };
                ok.Click += (s, e) =>
                {
                    UpdateSession();
                    if (!_session.CanApproveReprint)
                    {
                        MessageBox.Show(form, "当前角色无补打印审批权限，请切换 Supervisor 或 Admin。", "补打印审批", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        form.DialogResult = DialogResult.None;
                        return;
                    }
                };
                form.Controls.AddRange(new Control[] { lblDetails, txtDetails, lblPrinter, cmbReprintPrinter, ok, cancel });
                form.AcceptButton = ok;
                form.CancelButton = cancel;
                StyleDialog(form, ok, cancel);

                return form.ShowDialog(this) == DialogResult.OK ? cmbReprintPrinter.SelectedItem?.ToString() : null;
            }
        }

        private void PrintHistoryRecord(PrintRecord record, string printer)
        {
            if (!File.Exists(record.TemplatePath))
            { MessageBox.Show(this, $"历史模板文件不存在：\n{record.TemplatePath}"); return; }
            if (string.IsNullOrEmpty(printer) || !cmbPrinter.Items.Contains(printer))
            { MessageBox.Show(this, $"本次补打印机当前不可用：{printer}"); return; }
            var values = new Dictionary<string, string>(record.FieldValues, StringComparer.OrdinalIgnoreCase);
            if (!TryGetHistoryTemplateSettings(record, out var settings, out var warning)) return;
            var currentVersion = GetTemplateVersionForPath(record.TemplatePath);
            if (!string.IsNullOrWhiteSpace(record.TemplateVersion) && !string.IsNullOrWhiteSpace(currentVersion) &&
                !string.Equals(record.TemplateVersion, currentVersion, StringComparison.OrdinalIgnoreCase) &&
                MessageBox.Show(this, "历史记录的模板版本与当前模板文件版本不一致，继续补打印可能导致版面或字段不一致。\n\n是否继续？", "模板版本差异", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            var configuredSources = settings?.DataSources ?? new List<DataSourceItem>();
            var enabled = configuredSources.Where(source => source.Enabled).ToList();
            if (enabled.Count == 0)
            { MessageBox.Show(this, "历史模板缺少已启用的数据源设置，无法补打印。", "补打印校验", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (!ValidateTemplateFieldCoverage(record.TemplatePath, configuredSources, values, "补打印字段完整性")) return;
            if (!ValidateInputValues(enabled, values, false, settings)) return;
            if (settings.InputValidation && !ValidateLocalData(values, GetTemplateLocalData(settings), settings.DataSources)) return;
            if (!string.IsNullOrEmpty(warning) && MessageBox.Show(this, warning + "\n\n是否继续补打印？", "补打印设置确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            SetPrintEnvironmentEnabled(false);
            SetStatus("补打印中...");
            Task.Run(() =>
            {
                PrintResult result;
                try { result = _btService.Print(record.TemplatePath, values, printer, record.Copies); }
                catch (Exception ex) { result = new PrintResult(false, ex.Message, $"type={ex.GetType().Name};template={record.TemplatePath};printer={printer};copies={record.Copies};message={ex.Message}"); }
                PostToUi(() =>
                {
                    var historySaved = true;
                    try
                    {
                        historySaved = _printWorkflow.RecordPrintResult(_history, record.TemplateName, record.TemplatePath, record.TemplateId, values,
                            result.Success ? "REPRINT_PASS" : "REPRINT_FAIL", printer, record.Copies,
                            GetOperatorName(), "", record.TemplateVersion, result.DiagnosticDetails, record.OrderName, record.OrderId, record.TemplateFields);
                        if (result.Success && historySaved) RestoreAutoIncrementInputsToPendingValues();
                        if (!historySaved)
                            AddLog(result.Success ? "补打印作业已提交，但历史记录保存失败。" : "补打印失败，且失败历史记录保存失败。", "ERROR");
                        else if (result.Success)
                        {
                            AuditLogger.Append(GetOperatorName(), "Reprint", $"record={record.RecordId}");
                            AddLog("补打印作业已提交", "SUCCESS");
                            if (_chkPreview?.Checked == true && string.Equals(record.TemplatePath, _selectedTemplatePath, StringComparison.OrdinalIgnoreCase))
                                _ = RefreshPreviewAsync(new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase));
                        }
                        else
                            AddLog($"历史记录补打印失败: {result.ErrorMessage}", "ERROR");
                    }
                    finally
                    {
                        SetPrintEnvironmentEnabled(true);
                        LoadHistory();
                        RefreshStats();
                        SetStatus(result.Success ? (historySaved ? "补打印作业已提交" : "补打印作业已提交，历史保存失败") : (historySaved ? "补打印失败" : "补打印失败，历史保存失败"));
                    }
                });
            });
        }

        private bool TryGetHistoryTemplateSettings(PrintRecord record, out TemplateSettings settings, out string warning)
        {
            warning = "";
            settings = null;
            var orderTemplate = _orders.Orders
                .SelectMany(order => order.Templates ?? new List<OrderTemplate>())
                .FirstOrDefault(template =>
                    (!string.IsNullOrWhiteSpace(record.TemplateId) && string.Equals(template.Id, record.TemplateId, StringComparison.OrdinalIgnoreCase)) ||
                    string.Equals(template.SourcePath, record.TemplatePath, StringComparison.OrdinalIgnoreCase));
            if (orderTemplate?.Settings != null)
            {
                settings = orderTemplate.Settings;
                return true;
            }
            if (_templateSettings.TryGet(record.TemplateName, record.TemplatePath, out settings)) return true;
            if (string.Equals(record.TemplatePath, _selectedTemplatePath, StringComparison.OrdinalIgnoreCase) && _dataSources.Any(source => source.Enabled))
            {
                settings = BuildTemplateSettings(record.TemplatePath, _dataSources);
                warning = "未找到历史模板保存设置，将使用当前页面设置进行补打印校验。";
                return true;
            }
            MessageBox.Show(this, "未找到历史模板保存设置，无法确认长度、本地完整匹配和字段启用规则。", "补打印校验", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        private string GetTemplateVersionForPath(string templatePath)
        {
            var template = _orders.Orders.SelectMany(order => order.Templates ?? new List<OrderTemplate>())
                .FirstOrDefault(item => string.Equals(item.SourcePath, templatePath, StringComparison.OrdinalIgnoreCase));
            return GetTemplateVersion(template);
        }

        private void SetPrintEnvironmentEnabled(bool enabled)
        {
            btnPrint.Enabled = enabled;
            btnImportHistory.Enabled = enabled;
            btnReprintHistory.Enabled = enabled;
            cmbTemplate.Enabled = enabled;
            cmbPrinter.Enabled = enabled;
            numCopies.Enabled = enabled;
            btnEditDataSources.Enabled = enabled;
            btnBrowseDir.Enabled = enabled;
            btnLoadConfig.Enabled = enabled;
            inputPanel.Enabled = enabled;
        }

        private List<PrintRecord> GetCurrentHistoryRecords(bool includeAllPages = false)
        {
            var status = _cmbHistoryStatus?.SelectedItem?.ToString() ?? "全部状态";
            if (string.Equals(status, "全部状态", StringComparison.OrdinalIgnoreCase)) status = "";
            var date = _txtHistoryDate?.Text?.Trim() ?? "";
            return _history.Search(Path.GetFileName(_selectedTemplatePath), _selectedTemplatePath, GetCurrentTemplateId(), txtSearch?.Text ?? "", chkExactSearch.Checked,
                includeAllPages ? 0 : HistoryPageSize, true, includeAllPages ? 0 : _historyPageIndex * HistoryPageSize, status, date, "", "");
        }

        private void LoadHistory()
        {
            dgvHistory.DataSource = null;
            var dt = HistoryPresenter.BuildTable(GetCurrentHistoryRecords());
            dgvHistory.DataSource = dt;
            dgvHistory.Columns["记录ID"].Visible = false;
            foreach (DataGridViewColumn column in dgvHistory.Columns)
                if (_historyColumnWidths.TryGetValue(column.Name, out var width) && width > 20) column.Width = width;

            // Apply color formatting to status column
            foreach (DataGridViewRow row in dgvHistory.Rows)
            {
                var statusCell = row.Cells["状态"];
                if (statusCell?.Value?.ToString().EndsWith("PASS", StringComparison.Ordinal) == true)
                {
                    statusCell.Style.ForeColor = MiuiTheme.Success;
                    statusCell.Style.Font = MiuiTheme.EmphasizedBodyFont;
                }
                else if (statusCell?.Value?.ToString().EndsWith("FAIL", StringComparison.Ordinal) == true)
                {
                    statusCell.Style.ForeColor = MiuiTheme.Error;
                    statusCell.Style.Font = MiuiTheme.EmphasizedBodyFont;
                }
                statusCell.Style.SelectionBackColor = Color.FromArgb(224, 236, 255);
                statusCell.Style.SelectionForeColor = MiuiTheme.TextPrimary;
            }
            dgvHistory.ClearSelection();
            if (_lblHistoryPage != null) _lblHistoryPage.Text = $"第 {_historyPageIndex + 1} 页";
            if (_btnPrevHistoryPage != null) _btnPrevHistoryPage.Enabled = _historyPageIndex > 0;
            if (_btnNextHistoryPage != null) _btnNextHistoryPage.Enabled = dt.Rows.Count >= HistoryPageSize;
        }
        private void RefreshStats()
        {
            var templateName = Path.GetFileName(_selectedTemplatePath);
            var templateId = GetCurrentTemplateId();
            var todayCount = _history.TodayCount(templateName, _selectedTemplatePath, templateId);
            var totalCount = _history.Count(templateName, _selectedTemplatePath, templateId);
            lblTodayCount.Text = todayCount.ToString();
            lblTotalCount.Text = totalCount.ToString();
            lblTodayStatus.Text = $"今日 {todayCount}";
            lblTotalStatus.Text = $"累计 {totalCount}";
        }

        private void DgvHistory_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            var row = dgvHistory.Rows[e.RowIndex];
            var recordId = row.Cells["记录ID"].Value?.ToString() ?? "";
            var record = _history.GetById(recordId);
            var imei = row.Cells["数据"].Value?.ToString() ?? "";
            var time = row.Cells["打印时间"].Value?.ToString() ?? "";
            var status = row.Cells["状态"].Value?.ToString() ?? "";

            var parts = imei.Split('|');
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"打印时间: {time}");
            sb.AppendLine($"状态: {status}");
            if (record != null)
            {
                sb.AppendLine($"操作员: {record.OperatorName}");
                sb.AppendLine($"订单: {record.OrderName}");
                sb.AppendLine($"订单ID: {record.OrderId}");
                if (!string.IsNullOrWhiteSpace(record.ReprintReason)) sb.AppendLine($"补打印原因: {record.ReprintReason}");
                sb.AppendLine($"模板ID: {record.TemplateId}");
                sb.AppendLine($"模板版本: {record.TemplateVersion}");
                if (!string.IsNullOrWhiteSpace(record.DiagnosticDetails)) sb.AppendLine($"诊断详情: {record.DiagnosticDetails}");
            }
            sb.AppendLine();
            sb.AppendLine("数据详情:");
            for (int i = 0; i < parts.Length; i++)
                sb.AppendLine($"  {i + 1}. {parts[i]}");

            MessageBox.Show(this, sb.ToString(), "打印详情", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void DgvHistory_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
            if (e.RowIndex < 0)
            {
                dgvHistory.ClearSelection();
                return;
            }
            dgvHistory.ClearSelection();
            dgvHistory.Rows[e.RowIndex].Selected = true;
            var columnIndex = e.ColumnIndex >= 0 && dgvHistory.Columns[e.ColumnIndex].Visible ? e.ColumnIndex : GetFirstVisibleHistoryColumnIndex();
            if (columnIndex >= 0) dgvHistory.CurrentCell = dgvHistory.Rows[e.RowIndex].Cells[columnIndex];
        }

        private void HistoryMenu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            var point = dgvHistory.PointToClient(Cursor.Position);
            var hit = dgvHistory.HitTest(point.X, point.Y);
            if (hit.RowIndex < 0)
            {
                dgvHistory.ClearSelection();
                e.Cancel = true;
            }
        }

        private int GetFirstVisibleHistoryColumnIndex()
        {
            foreach (DataGridViewColumn column in dgvHistory.Columns)
                if (column.Visible) return column.Index;
            return -1;
        }

        private void DeleteSelectedHistoryRecord_Click(object sender, EventArgs e)
        {
            UpdateSession();
            if (!_session.CanDeleteHistory)
            { _dialogs.ShowWarning(this, "当前角色无历史删除权限。", "权限不足"); return; }
            if (dgvHistory.SelectedRows.Count == 0)
            { MessageBox.Show(this, "请先选择一条历史记录"); return; }

            var row = dgvHistory.SelectedRows[0];
            var recordId = row.Cells["记录ID"].Value?.ToString() ?? "";
            var data = row.Cells["数据"].Value?.ToString() ?? "";
            if (!_dialogs.Confirm(this, $"这是高风险操作，将从历史控件、重复校验和各项检测中排除此记录。\n\n原始记录仍保存在数据库、JSONL 和独立历史副本中。\n\n{data}\n\n确定继续？", "排除历史记录确认"))
                return;
            var existedBeforeDelete = _history.GetById(recordId) != null;

            if (_history.Delete(recordId, GetOperatorName(), "从历史控件排除单条记录"))
            {
                AuditLogger.Append(GetOperatorName(), "ExcludeHistory", recordId);
                AddLog("已从历史控件和检测中排除单条记录，原始数据已保留", "WARNING");
                LoadHistory();
                RefreshStats();
            }
            else
            {
                MessageBox.Show(this, existedBeforeDelete ? "排除历史记录失败，请检查文件权限。" : "该活动历史记录已不存在，请刷新后重试。", "排除历史记录", MessageBoxButtons.OK, existedBeforeDelete ? MessageBoxIcon.Error : MessageBoxIcon.Information);
            }
        }

        #endregion

        #region Config

        private void btnSaveConfig_Click(object sender, EventArgs e)
        { SaveConfig(); SaveCurrentTemplateSettings(); MessageBox.Show(this, "配置已保存"); AddLog("配置已保存", "SUCCESS"); }
        private void btnLoadConfig_Click(object sender, EventArgs e)
        { LoadConfig(_configFile); PopulateTemplateList(_templatesFolder); RebuildInputFields(); MessageBox.Show(this, "配置已加载"); }

        private void SaveCurrentTemplateSettings()
        {
            if (string.IsNullOrEmpty(_selectedTemplatePath)) return;
            try
            {
                var settings = new TemplateSettings
                {
                    TemplateName = Path.GetFileName(_selectedTemplatePath),
                    TemplatePath = _selectedTemplatePath,
                    Printer = cmbPrinter.SelectedItem?.ToString() ?? "",
                    Copies = (int)numCopies.Value,
                    InputValidation = _useLocalDataValidation,
                    DuplicateValidation = _duplicateValidationEnabled,
                    LengthValidation = _lengthValidationEnabled,
                    GlobalExpectedLength = _globalExpectedLength,
                    GlobalLengthRevision = _globalLengthRevision,
                    LengthRevisionCounter = _lengthRevisionCounter,
                    LocalDataPath = _localDataPath,
                    LocalDataStoragePath = _localDataStoragePath,
                    LocalDataColumnName = _localDataColumnName,
                    LocalDataTargetField = _localDataTargetField,
                    LocalData = string.IsNullOrWhiteSpace(_localDataStoragePath) ? _localData.ToList() : new List<string>(),
                    DataSources = _dataSources.Select(CloneDataSource).ToList()
                };
                if (_activeOrderTemplate != null && string.Equals(_activeOrderTemplate.SourcePath, _selectedTemplatePath, StringComparison.OrdinalIgnoreCase))
                {
                    _activeOrderTemplate.Settings = settings;
                    if (_activeOrder != null) _orders.Add(_activeOrder);
                }
                else
                {
                    _templateSettings.Save(settings);
                }
            }
            catch (Exception ex)
            {
                LoggerService.Error("保存模板设置失败", ex);
            }
        }

        private void RestoreAutoIncrementInputsToPendingValues()
        {
            var enabled = _dataSources.Where(item => item.Enabled).ToList();
            for (int i = 0; i < enabled.Count && i < _inputTextBoxes.Length; i++)
            {
                if (!enabled[i].AutoIncrement || string.IsNullOrEmpty(enabled[i].LockedValue)) continue;
                enabled[i].AutoIncrementLocked = true;
                _inputTextBoxes[i].Text = enabled[i].LockedValue;
                _inputTextBoxes[i].ReadOnly = true;
                _inputTextBoxes[i].BackColor = SystemColors.Control;
            }
            SaveConfig();
            SaveCurrentTemplateSettings();
        }

        private bool RestoreTemplateSettings(string templateName, string templatePath)
        {
            if (!_templateSettings.TryGet(templateName, templatePath, out var settings)) return false;
            _legacyDataSourcesPending.Clear();
            ApplyTemplateSettings(settings);
            AddLog($"已恢复模板设置: {templateName}", "INFO");
            return true;
        }

        private void ApplyTemplateSettings(TemplateSettings settings)
        {
            ValidationService.MigrateLocalDataSelection(settings);
            _legacyDataSourcesPending.Clear();
            _isLoadingConfig = true;
            try
            {
                _dataSources = (settings.DataSources ?? new List<DataSourceItem>()).Select(CloneDataSource).ToList();
                AdvancePrintedAutoIncrementLockedValues(_dataSources, settings.TemplateName, settings.TemplatePath, settings.TemplateId);
                _hasSavedDataSourceOrder = _dataSources.Count > 0;
                _useLocalDataValidation = settings.InputValidation;
                _duplicateValidationEnabled = settings.DuplicateValidation;
                _lengthValidationEnabled = settings.LengthValidation;
                _globalExpectedLength = settings.GlobalExpectedLength;
                _globalLengthRevision = settings.GlobalLengthRevision;
                _lengthRevisionCounter = settings.LengthRevisionCounter;
                _localDataPath = settings.LocalDataPath ?? "";
                _localDataStoragePath = settings.LocalDataStoragePath ?? "";
                _localDataColumnName = settings.LocalDataColumnName ?? "";
                _localDataTargetField = settings.LocalDataTargetField ?? "";
                _localData = GetTemplateLocalData(settings);
                if (_localData.Count == 0) _useLocalDataValidation = false;
                UpdateLocalDataValidationAvailability();
                chkUseLocalData.Checked = _useLocalDataValidation;
                chkDuplicateValidation.Checked = _duplicateValidationEnabled;
                chkLengthValidation.Checked = _lengthValidationEnabled;
                btnGlobalLength.Enabled = _lengthValidationEnabled;
                numCopies.Value = Math.Max(1, Math.Min(99, settings.Copies));
                if (!string.IsNullOrEmpty(settings.Printer))
                {
                    if (!cmbPrinter.Items.Contains(settings.Printer)) cmbPrinter.Items.Add(settings.Printer);
                    cmbPrinter.SelectedItem = settings.Printer;
                }
                else if (cmbPrinter.Items.Count > 0)
                    cmbPrinter.SelectedIndex = 0;
                UpdateLocalDataLabel(_localData.Count > 0 ? $"已恢复: {_localData.Count} 条 {(_localDataTargetField.Length > 0 ? "->" + _localDataTargetField : "")}" : "");
                RebuildInputFields();
                UpdateEffectiveSummary();
            }
            finally
            {
                _isLoadingConfig = false;
            }
        }

        private void AdvancePrintedAutoIncrementLockedValues(List<DataSourceItem> sources, string templateName, string templatePath, string templateId)
        {
            foreach (var source in sources ?? new List<DataSourceItem>())
            {
                if (!source.AutoIncrement || !source.AutoIncrementLocked || string.IsNullOrWhiteSpace(source.LockedValue)) continue;
                var guard = 0;
                while (guard++ < 100 && _history.ContainsAnyValue(templateName, templatePath, templateId, source.LockedValue))
                    source.LockedValue = IncrementValue(source.LockedValue, source.AutoStep == 0 ? 1 : source.AutoStep);
            }
        }

        private static DataSourceItem CloneDataSource(DataSourceItem source)
        {
            return new DataSourceItem
            {
                Name = source.Name,
                Field = source.Field,
                Enabled = source.Enabled,
                AutoIncrement = source.AutoIncrement,
                AutoStep = source.AutoStep,
                IsLocked = source.IsLocked,
                LockAfterInput = source.LockAfterInput,
                LockedValue = source.LockedValue,
                AutoIncrementLocked = source.AutoIncrementLocked,
                ExpectedLength = source.ExpectedLength,
                LengthRevision = source.LengthRevision,
                LengthEdited = source.LengthEdited,
                UseLocalDataValidation = source.UseLocalDataValidation
            };
        }

        private void SaveApplicationState()
        {
            try
            {
                if (_activeOrder == null && !string.IsNullOrWhiteSpace(_selectedTemplatePath))
                    _applicationState.SelectedTemplatePath = _selectedTemplatePath;
                _applicationState.ActiveOrderId = _activeOrder?.OrderId ?? "";
                _applicationState.ActiveTemplateId = _activeOrderTemplate?.Id ?? "";
                _applicationState.Printer = cmbPrinter.SelectedItem?.ToString() ?? "";
                _applicationState.Copies = (int)numCopies.Value;
                _applicationState.PreviewEnabled = _chkPreview?.Checked == true;
                _applicationStateManager.Save(_applicationState);
            }
            catch (Exception ex)
            {
                LoggerService.Warn($"保存最近使用状态失败: {ex.Message}");
            }
        }

        private void SaveConfig()
        {
            var dir = Path.GetDirectoryName(_configFile); if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var tempFile = _configFile + ".tmp";
            try
            {
                try
                {
                    if (File.Exists(_configFile)) File.Copy(_configFile, _configFile + ".bak", true);
                }
                catch (Exception ex)
                {
                    LoggerService.Warn($"备份配置失败: {ex.Message}");
                }
                var sb = new StringBuilder();
                AppendIniSection(sb, "General", new Dictionary<string, string>
                {
                    ["TemplatesFolder"] = _templatesFolder ?? "",
                    ["Printer"] = cmbPrinter.SelectedItem?.ToString() ?? "",
                    ["Copies"] = numCopies.Value.ToString(),
                    ["InputValidation"] = _useLocalDataValidation.ToString(),
                    ["DuplicateValidation"] = _duplicateValidationEnabled.ToString(),
                    ["LengthValidation"] = _lengthValidationEnabled.ToString(),
                    ["GlobalExpectedLength"] = _globalExpectedLength.ToString(),
                    ["GlobalLengthRevision"] = _globalLengthRevision.ToString(),
                    ["LengthRevisionCounter"] = _lengthRevisionCounter.ToString(),
                    ["HistoryColumnWidths"] = string.Join(";", _historyColumnWidths.Select(item => $"{item.Key}:{item.Value}")),
                    ["DSCount"] = _dataSources.Count.ToString()
                });
                for (int i = 0; i < _dataSources.Count; i++)
                {
                    AppendIniSection(sb, $"DS{i}", new Dictionary<string, string>
                    {
                        ["Name"] = _dataSources[i].Name,
                        ["Field"] = _dataSources[i].Field,
                        ["Enabled"] = _dataSources[i].Enabled.ToString(),
                        ["AutoIncrement"] = _dataSources[i].AutoIncrement.ToString(),
                        ["AutoStep"] = _dataSources[i].AutoStep.ToString(),
                        ["IsLocked"] = _dataSources[i].IsLocked.ToString(),
                        ["LockAfterInput"] = _dataSources[i].LockAfterInput.ToString(),
                        ["LockedValue"] = _dataSources[i].LockedValue ?? "",
                        ["AutoIncrementLocked"] = _dataSources[i].AutoIncrementLocked.ToString(),
                        ["ExpectedLength"] = _dataSources[i].ExpectedLength.ToString(),
                        ["LengthRevision"] = _dataSources[i].LengthRevision.ToString(),
                        ["LengthEdited"] = _dataSources[i].LengthEdited.ToString(),
                        ["UseLocalDataValidation"] = _dataSources[i].UseLocalDataValidation.ToString()
                    });
                }
                AtomicFileWriter.WriteAllText(_configFile, sb.ToString(), Encoding.Unicode);
            }
            catch (Exception ex)
            {
                LoggerService.Warn($"保存配置失败: {ex.Message}");
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            }
        }

        private static void AppendIniSection(StringBuilder sb, string section, Dictionary<string, string> values)
        {
            sb.AppendLine($"[{section}]");
            foreach (var item in values)
                sb.AppendLine($"{item.Key}={EscapeIniValue(item.Value)}");
            sb.AppendLine();
        }

        private static string EscapeIniValue(string value)
        {
            return (value ?? "").Replace("\r", " ").Replace("\n", " ");
        }

        private void LoadHistoryColumnWidths(string value)
        {
            _historyColumnWidths.Clear();
            foreach (var part in (value ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var index = part.LastIndexOf(':');
                if (index <= 0) continue;
                if (int.TryParse(part.Substring(index + 1), out var width))
                    _historyColumnWidths[part.Substring(0, index)] = width;
            }
        }

        private void SaveCurrentConfigurationState()
        {
            if (_isInitializing || _isLoadingConfig) return;
            SaveConfig();
            SaveCurrentTemplateSettings();
        }

        private void SaveTemplateFolderConfig()
        {
            SaveConfig();
        }

        private void LoadConfig(string path)
        {
            _isLoadingConfig = true;
            try
            {
                _templatesFolder = IniReadValue("General", "TemplatesFolder", path);
                if (string.IsNullOrWhiteSpace(_templatesFolder)) _templatesFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "templates");
                txtTemplateDir.Text = _templatesFolder;
                _localData.Clear();
                _localDataPath = "";
                _localDataStoragePath = "";
                _localDataColumnName = "";
                _localDataTargetField = "";
                UpdateLocalDataLabel("");
                var copies = 1; int.TryParse(IniReadValue("General", "Copies", path), out copies); numCopies.Value = Math.Max(1, Math.Min(99, copies));
                bool.TryParse(IniReadValue("General", "InputValidation", path), out _useLocalDataValidation);
                if (!bool.TryParse(IniReadValue("General", "DuplicateValidation", path), out _duplicateValidationEnabled)) _duplicateValidationEnabled = true;
                chkDuplicateValidation.Checked = _duplicateValidationEnabled;
                bool.TryParse(IniReadValue("General", "LengthValidation", path), out _lengthValidationEnabled);
                int.TryParse(IniReadValue("General", "GlobalExpectedLength", path), out _globalExpectedLength);
                long.TryParse(IniReadValue("General", "GlobalLengthRevision", path), out _globalLengthRevision);
                long.TryParse(IniReadValue("General", "LengthRevisionCounter", path), out _lengthRevisionCounter);
                LoadHistoryColumnWidths(IniReadValue("General", "HistoryColumnWidths", path));
                _lengthRevisionCounter = Math.Max(_lengthRevisionCounter, _globalLengthRevision);
                chkLengthValidation.Checked = _lengthValidationEnabled;
                btnGlobalLength.Enabled = _lengthValidationEnabled;
                int.TryParse(IniReadValue("General", "DSCount", path), out int count);
                _hasSavedDataSourceOrder = count > 0;
                _dataSources = new List<DataSourceItem>();
                for (int i = 0; i < count; i++)
                {
                    var en = true; bool.TryParse(IniReadValue($"DS{i}", "Enabled", path), out en);
                    var autoInc = false; bool.TryParse(IniReadValue($"DS{i}", "AutoIncrement", path), out autoInc);
                    var autoStep = 1; int.TryParse(IniReadValue($"DS{i}", "AutoStep", path), out autoStep);
                    var isLocked = false; bool.TryParse(IniReadValue($"DS{i}", "IsLocked", path), out isLocked);
                    var lockAfterInput = false; bool.TryParse(IniReadValue($"DS{i}", "LockAfterInput", path), out lockAfterInput);
                    var autoIncrementLocked = false; bool.TryParse(IniReadValue($"DS{i}", "AutoIncrementLocked", path), out autoIncrementLocked);
                    var lengthEdited = false; bool.TryParse(IniReadValue($"DS{i}", "LengthEdited", path), out lengthEdited);
                    var useLocalDataValidation = false; bool.TryParse(IniReadValue($"DS{i}", "UseLocalDataValidation", path), out useLocalDataValidation);
                    int.TryParse(IniReadValue($"DS{i}", "ExpectedLength", path), out int expectedLength);
                    long.TryParse(IniReadValue($"DS{i}", "LengthRevision", path), out long lengthRevision);
                    _lengthRevisionCounter = Math.Max(_lengthRevisionCounter, lengthRevision);
                    _dataSources.Add(new DataSourceItem
                    {
                        Name = IniReadValue($"DS{i}", "Name", path),
                        Field = IniReadValue($"DS{i}", "Field", path),
                        Enabled = en,
                        AutoIncrement = autoInc,
                        AutoStep = autoStep,
                        IsLocked = isLocked,
                        LockAfterInput = lockAfterInput,
                        LockedValue = IniReadValue($"DS{i}", "LockedValue", path),
                        AutoIncrementLocked = autoIncrementLocked,
                        ExpectedLength = expectedLength,
                        LengthRevision = lengthRevision,
                        LengthEdited = lengthEdited,
                        UseLocalDataValidation = useLocalDataValidation
                    });
                }
                _legacyDataSourcesPending = count > 0 ? _dataSources.Select(CloneDataSource).ToList() : new List<DataSourceItem>();
                UpdateLocalDataValidationAvailability();
                chkUseLocalData.Checked = _useLocalDataValidation;
                if (_dataSources.Count == 0) _dataSources.Add(new DataSourceItem { Name = "IMEI", Field = "IMEI1", Enabled = true });
            }
            finally
            {
                _isLoadingConfig = false;
            }
        }

        #endregion

        #region Log

        private void btnExportLog_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtLog.Text)) { MessageBox.Show(this, "日志为空"); return; }
            using (var sfd = new SaveFileDialog { Filter = "文本|*.txt", FileName = $"log_{DateTime.Now:yyyyMMdd_HHmmss}.log" })
            {
                if (sfd.ShowDialog(this) == DialogResult.OK)
                { try { File.WriteAllText(sfd.FileName, txtLog.Text, Encoding.UTF8); AddLog("日志已导出", "SUCCESS"); } catch (Exception ex) { MessageBox.Show(this, ex.Message); } }
            }
        }
        private void btnClearLog_Click(object sender, EventArgs e) { txtLog.Clear(); }

        private void AddLog(string msg, string level = "INFO")
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] [{level}] {msg}";
            if (txtLog.InvokeRequired) txtLog.Invoke((Action)(() => { txtLog.AppendText(line + Environment.NewLine); }));
            else { txtLog.AppendText(line + Environment.NewLine); }
            if (level == "ERROR") LoggerService.Error(msg); else LoggerService.Info(msg);
        }

        #endregion

        #region Status & INI

        private void SetStatus(string text)
        {
            if (IsDisposed || Disposing || !IsHandleCreated) return;
            if (statusStrip.InvokeRequired)
            {
                try { statusStrip.BeginInvoke((Action)(() => { if (!IsDisposed && !Disposing) lblStatus.Text = text; })); }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
                return;
            }
            lblStatus.Text = text;
        }

        [System.Runtime.InteropServices.DllImport("kernel32", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern long WritePrivateProfileString(string s, string k, string v, string p);
        [System.Runtime.InteropServices.DllImport("kernel32", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int GetPrivateProfileString(string s, string k, string d, StringBuilder r, int n, string p);
        private static void IniWriteValue(string s, string k, string v, string p) => WritePrivateProfileString(s, k, v, p);
        private static string IniReadValue(string s, string k, string p) { var sb = new StringBuilder(2048); GetPrivateProfileString(s, k, "", sb, sb.Capacity, p); return sb.ToString(); }

        #endregion
    }

    public class HistoryImportField
    {
        public string Field { get; }
        private readonly string _value;

        public HistoryImportField(string field, string value)
        {
            Field = field ?? "";
            _value = value ?? "";
        }

        public override string ToString() => $"{Field} = {_value}";
    }

    public class DataSourceSelectDialog : Form
    {
        public List<DataSourceItem> SelectedSources { get; private set; }
        private readonly List<DataSourceRow> _rows = new List<DataSourceRow>();
        private Panel _scrollPanel;
        private CheckBox chkSelectAll;

        private class DataSourceRow
        {
            public string Field;
            public Panel RowPanel;
            public CheckBox CbEnabled;
            public CheckBox CbUseLocalDataValidation;
            public Label LblField;
            public CheckBox CbAutoInc;
            public NumericUpDown NumStep;
            public ComboBox CmbLockMode;
            public TextBox TxtLockedValue;
            public NumericUpDown NumExpectedLength;
            public bool LengthEdited;
            public int InitialExpectedLength;
            public bool WasInputLocked;
            public bool AutoIncrementLocked;
            public Label Grip;
        }

        private readonly bool _lengthValidationEnabled;
        private readonly int _globalExpectedLength;

        public DataSourceSelectDialog(List<string> fields, List<DataSourceItem> current, bool preserveExistingOrder = true,
            bool lengthValidationEnabled = false, int globalExpectedLength = 0)
        {
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            _lengthValidationEnabled = lengthValidationEnabled;
            _globalExpectedLength = globalExpectedLength;
            Text = "选择数据源 - 拖拽排序"; Size = new Size(980, 460);
            FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false;

            var lbl = new Label { Text = $"模板包含 {fields.Count} 个数据源，拖拽 ≡ 排序，勾选使用：", Location = new Point(10, 10), Size = new Size(940, 20) };

            chkSelectAll = new CheckBox { Text = "全选/全不选", Location = new Point(10, 32), Size = new Size(100, 20) };

            var hdrGrip = new Label { Text = "排序", Location = new Point(15, 55), Size = new Size(22, 16), Font = MiuiTheme.SectionFont };
            var hdrName = new Label { Text = "字段名", Location = new Point(40, 55), Size = new Size(180, 16), Font = MiuiTheme.SectionFont };
            var hdrValidation = new Label { Text = "校验", Location = new Point(245, 55), Size = new Size(40, 16), Font = MiuiTheme.SectionFont };
            var hdrAuto = new Label { Text = "增序", Location = new Point(300, 55), Size = new Size(30, 16), Font = MiuiTheme.SectionFont };
            var hdrStep = new Label { Text = "步长", Location = new Point(340, 55), Size = new Size(60, 16), Font = MiuiTheme.SectionFont };
            var hdrLock = new Label { Text = "锁定方式", Location = new Point(410, 55), Size = new Size(80, 16), Font = MiuiTheme.SectionFont };
            var hdrLockedValue = new Label { Text = "锁定值（可空）", Location = new Point(535, 55), Size = new Size(120, 16), Font = MiuiTheme.SectionFont };
            var hdrLength = new Label { Text = "长度", Location = new Point(825, 55), Size = new Size(70, 16), Font = MiuiTheme.SectionFont };

            _scrollPanel = new Panel { Location = new Point(10, 75), Size = new Size(940, 255), AutoScroll = true, BorderStyle = BorderStyle.FixedSingle };

            var fieldSet = new HashSet<string>(fields, StringComparer.OrdinalIgnoreCase);
            var orderedFields = new List<string>();
            if (preserveExistingOrder)
            {
                foreach (var c in current)
                    if (fieldSet.Contains(c.Field) && !orderedFields.Contains(c.Field, StringComparer.OrdinalIgnoreCase))
                        orderedFields.Add(c.Field);
            }
            foreach (var f in fields
                .Where(f => !orderedFields.Contains(f, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => f, NaturalStringComparer.Instance))
                    orderedFields.Add(f);

            for (int i = 0; i < orderedFields.Count; i++)
            {
                var field = orderedFields[i];
                var existing = current.FirstOrDefault(d => string.Equals(d.Field, field, StringComparison.OrdinalIgnoreCase));
                bool isChecked = existing != null ? existing.Enabled : (!preserveExistingOrder || current.Count == 0);
                CreateRow(field, isChecked, existing?.Name ?? field, existing?.AutoIncrement ?? false, existing?.AutoStep ?? 1,
                    existing?.IsLocked ?? false, existing?.LockAfterInput ?? false, existing?.LockedValue ?? "",
                    existing?.AutoIncrementLocked ?? false, existing?.ExpectedLength ?? 0, existing?.UseLocalDataValidation ?? false);
            }

            chkSelectAll.Checked = _rows.Count > 0 && _rows.All(row => row.CbEnabled.Checked);
            chkSelectAll.CheckedChanged += (s, e) => { foreach (var row in _rows) row.CbEnabled.Checked = chkSelectAll.Checked; };
            RelayoutRows();

            var infoLbl = new Label { Text = "拖拽整行或 ≡ 可调整排序；长度显示全局值，改动后按单项长度保存", Location = new Point(10, 340), Size = new Size(620, 16), ForeColor = Color.Gray };

            var btnSelectAll = new Button { Text = "全选", Location = new Point(10, 365), Size = new Size(50, 25) };
            btnSelectAll.Click += (s, e) => { foreach (var r in _rows) r.CbEnabled.Checked = true; };
            var btnSelectNone = new Button { Text = "全不选", Location = new Point(65, 365), Size = new Size(55, 25) };
            btnSelectNone.Click += (s, e) => { foreach (var r in _rows) r.CbEnabled.Checked = false; };

            var ok = new Button { Text = "确定", Location = new Point(780, 365), Size = new Size(75, 28), DialogResult = DialogResult.OK };
            ok.Click += (s, e) =>
            {
                SelectedSources = new List<DataSourceItem>();
                var lockWithoutIncrement = _rows.Any(r => r.CbEnabled.Checked && r.CmbLockMode.SelectedIndex != 0 && !r.CbAutoInc.Checked);
                var zeroStepAutoIncrement = _rows.Any(r => r.CbEnabled.Checked && r.CbAutoInc.Checked && r.NumStep.Value == 0);
                if (zeroStepAutoIncrement)
                {
                    MessageBox.Show(this, "启用增降序的数据源步长不能为 0。", "数据源配置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    DialogResult = DialogResult.None;
                    return;
                }
                if (lockWithoutIncrement && MessageBox.Show(this,
                    "存在已开启锁定且未启用增降序的数据源，确定按固定锁定保存吗？",
                    "确认固定锁定", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    DialogResult = DialogResult.None;
                    return;
                }
                foreach (var r in _rows)
                    if (r.CbEnabled.Checked)
                        SelectedSources.Add(new DataSourceItem
                        {
                            Name = r.Field,
                            Field = r.Field,
                            Enabled = true,
                            UseLocalDataValidation = r.CbUseLocalDataValidation.Checked,
                            AutoIncrement = r.CbAutoInc.Checked,
                            AutoStep = (int)r.NumStep.Value,
                            IsLocked = r.CmbLockMode.SelectedIndex == 1 || (r.CmbLockMode.SelectedIndex == 2 && !string.IsNullOrWhiteSpace(r.TxtLockedValue.Text)),
                            LockAfterInput = r.CmbLockMode.SelectedIndex == 2,
                            LockedValue = r.CmbLockMode.SelectedIndex == 1 || r.CmbLockMode.SelectedIndex == 2 || (r.CbAutoInc.Checked && r.AutoIncrementLocked)
                                ? r.TxtLockedValue.Text.Trim()
                                : "",
                            AutoIncrementLocked = r.CbAutoInc.Checked && r.AutoIncrementLocked,
                            ExpectedLength = r.LengthEdited ? (int)r.NumExpectedLength.Value : r.InitialExpectedLength,
                            LengthEdited = r.LengthEdited
                        });
            };
            var cancel = new Button { Text = "取消", Location = new Point(865, 365), Size = new Size(75, 28), DialogResult = DialogResult.Cancel };

            Controls.AddRange(new Control[] { lbl, chkSelectAll, hdrGrip, hdrName, hdrValidation, hdrAuto, hdrStep, hdrLock, hdrLockedValue, hdrLength, _scrollPanel, infoLbl, btnSelectAll, btnSelectNone, ok, cancel });
            AcceptButton = ok; CancelButton = cancel;
            MiuiTheme.ApplyTheme(this);
            MiuiTheme.StyleButton(btnSelectAll);
            MiuiTheme.StyleButton(btnSelectNone);
            MiuiTheme.StyleButton(ok, true);
            MiuiTheme.StyleButton(cancel);
        }

        private void CreateRow(string field, bool checkedVal, string displayName, bool autoInc, int autoStep,
            bool isLocked, bool lockAfterInput, string lockedValue, bool autoIncrementLocked, int expectedLength, bool useLocalDataValidation)
        {
            var displayLength = expectedLength > 0 ? expectedLength : (_lengthValidationEnabled ? _globalExpectedLength : 0);
            var row = new DataSourceRow
            {
                Field = field,
                InitialExpectedLength = expectedLength,
                WasInputLocked = isLocked && lockAfterInput,
                AutoIncrementLocked = autoIncrementLocked
            };

            row.RowPanel = new Panel
            {
                Size = new Size(930, 28),
                AllowDrop = true,
                Tag = _rows.Count,
                BackColor = Color.Transparent
            };
            row.RowPanel.DragEnter += Row_DragEnter;
            row.RowPanel.DragOver += Row_DragOver;
            row.RowPanel.DragDrop += Row_DragDrop;
            row.RowPanel.MouseDown += Row_MouseDown;

            row.Grip = new Label
            {
                Text = "≡",
                Location = new Point(0, 3),
                Size = new Size(22, 22),
                Cursor = Cursors.Hand,
                Tag = _rows.Count,
                Font = MiuiTheme.DragHandleTextFont,
                ForeColor = Color.FromArgb(160, 160, 160)
            };
            row.Grip.MouseDown += Grip_MouseDown;

            row.CbEnabled = new CheckBox { Location = new Point(25, 2), Size = new Size(20, 20), Checked = checkedVal };

            row.LblField = new Label { Text = field, Location = new Point(50, 4), Size = new Size(190, 18), Cursor = Cursors.SizeAll };
            row.LblField.MouseDown += Row_MouseDown;

            row.CbUseLocalDataValidation = new CheckBox { Location = new Point(250, 2), Size = new Size(20, 20), Checked = useLocalDataValidation };

            row.CbAutoInc = new CheckBox { Location = new Point(300, 2), Size = new Size(20, 20), Checked = autoInc };

            row.NumStep = new NumericUpDown { Location = new Point(335, 0), Size = new Size(55, 25), Minimum = -99, Maximum = 99, Value = Math.Max(-99, Math.Min(99, autoStep)) };

            row.CmbLockMode = new ComboBox { Location = new Point(405, 0), Size = new Size(120, 25), DropDownStyle = ComboBoxStyle.DropDownList };
            row.CmbLockMode.Items.AddRange(new object[] { "不锁定", "固定锁定", "输入后锁定" });
            row.CmbLockMode.SelectedIndex = lockAfterInput ? 2 : isLocked ? 1 : 0;
            row.TxtLockedValue = new TextBox { Location = new Point(535, 0), Size = new Size(250, 25), Text = lockedValue ?? "" };
            row.NumExpectedLength = new NumericUpDown { Location = new Point(825, 0), Size = new Size(70, 25), Minimum = 0, Maximum = 512, Value = Math.Max(0, Math.Min(512, displayLength)) };
            row.NumExpectedLength.ValueChanged += (s, e) => row.LengthEdited = true;
            row.TxtLockedValue.Enabled = row.CmbLockMode.SelectedIndex != 0;
            row.CmbLockMode.SelectedIndexChanged += (s, e) =>
            {
                row.TxtLockedValue.Enabled = row.CmbLockMode.SelectedIndex != 0;
                if (row.CmbLockMode.SelectedIndex == 0)
                {
                    row.WasInputLocked = false;
                    row.TxtLockedValue.Text = "";
                }
                else if (row.CmbLockMode.SelectedIndex == 1)
                {
                    row.WasInputLocked = false;
                }
                else if (!lockAfterInput) row.WasInputLocked = false;
            };

            row.RowPanel.Controls.AddRange(new Control[] { row.Grip, row.CbEnabled, row.LblField, row.CbUseLocalDataValidation, row.CbAutoInc, row.NumStep, row.CmbLockMode, row.TxtLockedValue, row.NumExpectedLength });
            _scrollPanel.Controls.Add(row.RowPanel);

            _rows.Add(row);
        }

        private void RelayoutRows()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                _rows[i].RowPanel.Location = new Point(0, i * 30 + 2);
                _rows[i].RowPanel.Tag = i;
                _rows[i].Grip.Tag = i;
            }
            _scrollPanel.AutoScrollMinSize = new Size(0, _rows.Count * 30 + 4);
        }

        private void Grip_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            var grip = (Label)sender;
            var rowIdx = (int)grip.Tag;
            grip.DoDragDrop(rowIdx, DragDropEffects.Move);
        }

        private void Row_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            var control = sender as Control;
            var panel = control as Panel ?? control?.Parent as Panel;
            if (panel?.Tag is int rowIdx)
                panel.DoDragDrop(rowIdx, DragDropEffects.Move);
        }

        private void Row_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data?.GetDataPresent(typeof(int)) == true)
                e.Effect = DragDropEffects.Move;
        }

        private void Row_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data?.GetDataPresent(typeof(int)) == true)
                e.Effect = DragDropEffects.Move;
        }

        private void Row_DragDrop(object sender, DragEventArgs e)
        {
            if (e.Data?.GetDataPresent(typeof(int)) != true) return;
            var fromIdx = (int)e.Data.GetData(typeof(int));
            var toPanel = (Panel)sender;
            var toIdx = (int)toPanel.Tag;

            if (fromIdx == toIdx) return;

            var fromRow = _rows[fromIdx];
            _rows.RemoveAt(fromIdx);
            _rows.Insert(toIdx, fromRow);

            _scrollPanel.Controls.Clear();
            foreach (var r in _rows)
                _scrollPanel.Controls.Add(r.RowPanel);

            RelayoutRows();
        }
    }

    public class DataSourceInputDialog : Form
    {
        public string Value { get; private set; }
        private readonly DataSourceItem _source;
        private readonly TextBox _input;
        private readonly int _expectedLength;
        private readonly Func<string, string> _validateDuplicate;

        public DataSourceInputDialog(DataSourceItem source, string existingValue, int position, int total, int expectedLength, Func<string, string> validateDuplicate)
        {
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            _source = source;
            _expectedLength = expectedLength;
            _validateDuplicate = validateDuplicate;
            Value = existingValue ?? "";
            Text = $"输入数据源 ({position}/{total})";
            Size = new Size(500, 190);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;

            var title = new Label
            {
                Text = $"请输入 {source.Name}：",
                Location = new Point(12, 12),
                Size = new Size(455, 22),
                Font = MiuiTheme.DialogHeadingFont
            };
            var progress = new Label
            {
                Text = $"当前第 {position} 项，共 {total} 项" + (expectedLength > 0 ? $"，要求 {expectedLength} 位" : ""),
                Location = new Point(12, 38),
                Size = new Size(455, 18),
                ForeColor = Color.Gray
            };
            var label = new Label
            {
                Text = source.Name + "：",
                Location = new Point(12, 68),
                Size = new Size(100, 22),
                TextAlign = ContentAlignment.MiddleRight
            };
            _input = new TextBox
            {
                Location = new Point(118, 66),
                Size = new Size(350, 25),
                Text = existingValue ?? ""
            };
            _input.KeyDown += Input_KeyDown;
            MiuiTheme.StyleLabel(label);
            MiuiTheme.StyleTextBox(_input);

            var ok = new Button
            {
                Text = position == total ? "完成并打印" : "下一项",
                Location = new Point(288, 110),
                Size = new Size(99, 28)
            };
            ok.Click += Confirm_Click;
            var cancel = new Button
            {
                Text = "取消",
                Location = new Point(397, 110),
                Size = new Size(75, 28),
                DialogResult = DialogResult.Cancel
            };
            MiuiTheme.StyleButton(ok, true);
            MiuiTheme.StyleButton(cancel);

            Controls.Add(title);
            Controls.Add(progress);
            Controls.Add(label);
            Controls.Add(_input);
            Controls.Add(ok);
            Controls.Add(cancel);
            MiuiTheme.ApplyTheme(this);
            MiuiTheme.StyleButton(ok, true);
            MiuiTheme.StyleButton(cancel);
            progress.ForeColor = MiuiTheme.TextSecondary;
            AcceptButton = ok;
            CancelButton = cancel;
            Shown += (s, e) => { _input.Focus(); _input.SelectAll(); };
        }

        private void Input_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            ConfirmInput();
        }

        private void Confirm_Click(object sender, EventArgs e) => ConfirmInput();

        private void ConfirmInput()
        {
            var value = _input.Text.Trim();
            if (string.IsNullOrEmpty(value))
            {
                MessageBox.Show(this, $"\"{_source.Name}\" 不能为空", "数据源输入", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _input.Focus();
                return;
            }
            if (_expectedLength > 0 && value.Length != _expectedLength)
            {
                MessageBox.Show(this, $"\"{_source.Name}\" 必须为 {_expectedLength} 位，当前为 {value.Length} 位。", "长度校验", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _input.Clear();
                _input.Focus();
                return;
            }
            var duplicateMessage = _validateDuplicate?.Invoke(value);
            if (!string.IsNullOrEmpty(duplicateMessage))
            {
                MessageBox.Show(this, duplicateMessage, "数据校验", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _input.Clear();
                _input.Focus();
                return;
            }
            Value = value;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    public sealed class NaturalStringComparer : IComparer<string>
    {
        public static readonly NaturalStringComparer Instance = new NaturalStringComparer();

        public int Compare(string left, string right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left == null) return -1;
            if (right == null) return 1;

            int leftIndex = 0;
            int rightIndex = 0;
            while (leftIndex < left.Length && rightIndex < right.Length)
            {
                if (char.IsDigit(left[leftIndex]) && char.IsDigit(right[rightIndex]))
                {
                    int leftEnd = leftIndex;
                    int rightEnd = rightIndex;
                    while (leftEnd < left.Length && char.IsDigit(left[leftEnd])) leftEnd++;
                    while (rightEnd < right.Length && char.IsDigit(right[rightEnd])) rightEnd++;

                    int leftSignificant = leftIndex;
                    int rightSignificant = rightIndex;
                    while (leftSignificant < leftEnd - 1 && left[leftSignificant] == '0') leftSignificant++;
                    while (rightSignificant < rightEnd - 1 && right[rightSignificant] == '0') rightSignificant++;

                    int leftDigits = leftEnd - leftSignificant;
                    int rightDigits = rightEnd - rightSignificant;
                    if (leftDigits != rightDigits) return leftDigits.CompareTo(rightDigits);

                    for (int i = 0; i < leftDigits; i++)
                    {
                        int digitComparison = left[leftSignificant + i].CompareTo(right[rightSignificant + i]);
                        if (digitComparison != 0) return digitComparison;
                    }

                    int runLengthComparison = (leftEnd - leftIndex).CompareTo(rightEnd - rightIndex);
                    if (runLengthComparison != 0) return runLengthComparison;
                    leftIndex = leftEnd;
                    rightIndex = rightEnd;
                    continue;
                }

                int characterComparison = char.ToUpperInvariant(left[leftIndex]).CompareTo(char.ToUpperInvariant(right[rightIndex]));
                if (characterComparison != 0) return characterComparison;
                leftIndex++;
                rightIndex++;
            }

            return (left.Length - leftIndex).CompareTo(right.Length - rightIndex);
        }
    }

    public class DataSourceItem
    {
        public string Name { get; set; }
        public string Field { get; set; }
        public bool Enabled { get; set; }
        public bool AutoIncrement { get; set; }
        public int AutoStep { get; set; } = 1; // +1 for increment, -1 for decrement
        public bool IsLocked { get; set; }
        public bool LockAfterInput { get; set; }
        public string LockedValue { get; set; } = "";
        public bool AutoIncrementLocked { get; set; }
        public int ExpectedLength { get; set; }
        public long LengthRevision { get; set; }
        public bool LengthEdited { get; set; }
        public bool UseLocalDataValidation { get; set; }
    }
}
