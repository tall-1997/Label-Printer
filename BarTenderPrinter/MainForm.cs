using System;
using System.Collections.Generic;
using System.Data;
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
        private readonly BarTenderService _btService = new BarTenderService();
        private readonly HistoryManager _history = new HistoryManager();
        private readonly TemplateSettingsManager _templateSettings = new TemplateSettingsManager();
        private readonly OrderManager _orders = new OrderManager();
        private readonly System.Windows.Forms.Timer _historySearchTimer = new System.Windows.Forms.Timer { Interval = 180 };
        private readonly string _startupTemplatePath;
        private readonly string _configFile;
        private readonly string _version = "v5.7.43";

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
        private bool _useLocalDataValidation = false;
        private bool _duplicateValidationEnabled = true;
        private bool _isInitializing = true;
        private bool _isLoadingConfig;
        private bool _hasSavedDataSourceOrder;
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
        private ComboBox _cmbPrintOrder;
        private Panel _orderPagePanel;
        private Panel _orderContentPanel;
        private ComboBox _txtOrderCustomer;
        private ComboBox _txtOrderModel;
        private ComboBox _txtOrderColor;
        private TextBox _txtOrderNumber;
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
        private bool _loadingOrderEditor;
        private bool _orderEditorDirty;
        private bool _applyingOrderGlobalLength;

        public MainForm(string startupTemplatePath = null)
        {
            _startupTemplatePath = NormalizeStartupTemplatePath(startupTemplatePath);
            InitializeComponent();
            InstallOrderSidebar();
            _configFile = AppPaths.ConfigFile;
            Text = $"BarTender 标签打印工具 {_version}";
            titleLabel.Text = $"BarTender 标签打印工具 {_version}";
            MiuiTheme.ApplyTheme(this);
            Load += MainForm_Load;
            Shown += MainForm_Shown;
            FormClosing += (s, e) =>
            {
                if (MessageBox.Show(this, "确定退出软件？", "退出确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                { e.Cancel = true; return; }
                if (!ConfirmOrderEditorChanges()) { e.Cancel = true; return; }
                SaveCurrentTemplateSettings();
                _historySearchTimer.Dispose();
                _btService.Dispose();
            };
            inputPanel.SizeChanged += InputPanel_SizeChanged;
            SizeChanged += (s, e) => RebuildPrintPageLayout();
            dgvHistory.CellDoubleClick += DgvHistory_CellDoubleClick;
            dgvHistory.CellMouseDown += DgvHistory_CellMouseDown;
            var historyMenu = new ContextMenuStrip();
            historyMenu.Items.Add("删除此条记录", null, DeleteSelectedHistoryRecord_Click);
            historyMenu.Opening += HistoryMenu_Opening;
            dgvHistory.ContextMenuStrip = historyMenu;
            cmbPrinter.SelectedIndexChanged += (s, e) => SaveCurrentConfigurationState();
            _historySearchTimer.Tick += (s, e) => { _historySearchTimer.Stop(); LoadHistory(); };
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
            const int collapsedWidth = 44;
            const int orderSelectorHeight = 40;
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
                Size = new Size(collapsedWidth, groupBoxLog.Top - titlePanel.Bottom),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left,
                BackColor = Color.FromArgb(245, 246, 250)
            };
            _btnSidebarToggle = new Button { Text = "", Location = new Point(7, 12), Size = new Size(30, 30) };
            _btnSidebarToggle.Click += (s, e) => SetSidebarExpanded(!_sidebarExpanded);
            _btnSidebarToggle.Paint += SidebarToggle_Paint;
            _btnPrintPage = new Button { Text = "打印页面", Location = new Point(12, 54), Size = new Size(120, 34), Visible = false };
            _btnOrderPage = new Button { Text = "订单管理", Location = new Point(12, 96), Size = new Size(120, 34), Visible = false };
            _btnPrintPage.Click += (s, e) => { ShowPrintPage(); SetSidebarExpanded(false); };
            _btnOrderPage.Click += (s, e) => { ShowOrderManagementPage(); SetSidebarExpanded(false); };
            _navPanel.Controls.AddRange(new Control[] { _btnSidebarToggle, _btnPrintPage, _btnOrderPage });
            Controls.Add(_navPanel);
            _navPanel.BringToFront();
            MiuiTheme.StyleButton(_btnSidebarToggle);
            MiuiTheme.StyleButton(_btnPrintPage, true);
            MiuiTheme.StyleButton(_btnOrderPage);

            _printOrderPanel = new Panel
            {
                Location = new Point(collapsedWidth + 10, titlePanel.Bottom + 4),
                Size = new Size(ClientSize.Width - collapsedWidth - 20, 34),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = BackColor
            };
            var printOrderLabel = new Label { Text = "当前订单：", Location = new Point(0, 8), Size = new Size(72, 20) };
            _cmbPrintOrder = new ComboBox
            {
                Location = new Point(75, 4),
                Size = new Size(_printOrderPanel.Width - 75, 25),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                DropDownStyle = ComboBoxStyle.DropDownList,
                DisplayMember = nameof(PackagingOrder.DisplayName)
            };
            _cmbPrintOrder.SelectedIndexChanged += (s, e) => ApplyPrintOrderSelection();
            _printOrderPanel.Controls.Add(printOrderLabel);
            _printOrderPanel.Controls.Add(_cmbPrintOrder);
            Controls.Add(_printOrderPanel);
            MiuiTheme.StyleLabel(printOrderLabel);

            _orderPagePanel = new Panel
            {
                Location = new Point(collapsedWidth, titlePanel.Bottom),
                Size = new Size(ClientSize.Width - collapsedWidth, groupBoxLog.Top - titlePanel.Bottom),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Visible = false,
                BackColor = BackColor
            };
            _orderPanel = new GroupBox
            {
                Text = "包装 MES 订单",
                Dock = DockStyle.Left,
                Width = 250,
                Padding = new Padding(12)
            };
            _orderContentPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12), AutoScroll = true };
            var y = 28;
            _btnAddOrder = new Button { Text = "添加订单", Location = new Point(12, y), Size = new Size(200, 30) };
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
            RefreshPrintOrderSelector();
        }

        private void RebuildPrintPageLayout()
        {
            if (_printOrderPanel == null) return;
            var left = _printOrderPanel.Left;
            var width = Math.Max(500, ClientSize.Width - left - 10);
            cmbTemplate.Location = new Point(left, titlePanel.Bottom + 44);
            cmbTemplate.Size = new Size(width, 25);
            lblSelectedTemplate.Location = new Point(left, cmbTemplate.Bottom + 4);
            lblSelectedTemplate.Size = new Size(Math.Min(420, width), 18);

            lblPrinter.Location = new Point(left, lblSelectedTemplate.Bottom + 16);
            cmbPrinter.Location = new Point(left + 58, lblSelectedTemplate.Bottom + 12);
            btnRefreshPrinter.Location = new Point(left + width - 140, lblSelectedTemplate.Bottom + 11);
            lblCopies.Location = new Point(left + width - 86, lblSelectedTemplate.Bottom + 15);
            numCopies.Location = new Point(left + width - 45, lblSelectedTemplate.Bottom + 12);
            cmbPrinter.Size = new Size(Math.Max(180, btnRefreshPrinter.Left - cmbPrinter.Left - 6), 25);

            inputPanel.Location = new Point(left, cmbPrinter.Bottom + 10);
            inputPanel.Width = width;
            btnPrint.Location = new Point(left, inputPanel.Bottom + 8);
            btnPrint.Width = width;
            tabBottom.Location = new Point(left, btnPrint.Bottom + 8);
            tabBottom.Size = new Size(width, Math.Max(120, groupBoxLog.Top - tabBottom.Top - 8));
        }

        private void SetSidebarExpanded(bool expanded)
        {
            _sidebarExpanded = expanded;
            _navPanel.Width = expanded ? 150 : 44;
            _btnPrintPage.Visible = expanded;
            _btnOrderPage.Visible = expanded;
            _navPanel.BringToFront();
        }

        private void SidebarToggle_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(MiuiTheme.TextPrimary, 2F) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                e.Graphics.DrawLine(pen, 8, 9, 22, 9);
                e.Graphics.DrawLine(pen, 8, 15, 22, 15);
                e.Graphics.DrawLine(pen, 8, 21, 22, 21);
            }
        }

        private void ShowPrintPage()
        {
            if (_orderPagePanel.Visible && !ConfirmOrderEditorChanges()) return;
            if (_activeOrderTemplate != null && !ResolveTemplateUpdate(_activeOrder, _activeOrderTemplate)) return;
            SaveSelectedOrderTemplateDraft();
            _orderPagePanel.Visible = false;
            _printOrderPanel.Visible = true;
            _printOrderPanel.BringToFront();
            MiuiTheme.StyleButton(_btnPrintPage, true);
            MiuiTheme.StyleButton(_btnOrderPage);
        }

        private void ShowOrderManagementPage()
        {
            _printOrderPanel.Visible = false;
            _orderPagePanel.Visible = true;
            _orderPagePanel.BringToFront();
            MiuiTheme.StyleButton(_btnOrderPage, true);
            MiuiTheme.StyleButton(_btnPrintPage);
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

        private void RefreshPrintOrderSelector()
        {
            if (_cmbPrintOrder == null) return;
            _loadingOrderFilters = true;
            try
            {
                _cmbPrintOrder.Items.Clear();
                foreach (var order in _orders.Orders) _cmbPrintOrder.Items.Add(order);
                if (_activeOrder != null) _cmbPrintOrder.SelectedItem = _activeOrder;
            }
            finally { _loadingOrderFilters = false; }
        }

        private void ApplyPrintOrderSelection()
        {
            if (_loadingOrderFilters || !(_cmbPrintOrder?.SelectedItem is PackagingOrder order)) return;
            var previousOrder = _activeOrder;
            SelectOrder(order);
            if (ApplyOrder(order)) return;
            _loadingOrderFilters = true;
            try
            {
                _cmbPrintOrder.SelectedItem = previousOrder;
                if (previousOrder != null) SelectOrder(previousOrder);
                else ClearOrderSelection();
            }
            finally { _loadingOrderFilters = false; }
        }

        private void ClearOrderSelection()
        {
            foreach (var combo in new[] { _cmbOrderCustomer, _cmbOrderModel, _cmbOrderColor, _cmbOrderNumber })
                if (combo != null) combo.SelectedIndex = -1;
        }

        private ComboBox AddOrderCombo(string labelText, int x, int y)
        {
            var label = new Label { Text = labelText + "：", Location = new Point(x, y - 2), Size = new Size(200, 18) };
            var combo = new ComboBox { Location = new Point(x, y + 22), Size = new Size(200, 25), DropDownStyle = ComboBoxStyle.DropDownList };
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
                _activeOrderTemplate = template;
            }
            finally
            {
                _loadingOrderTemplate = false;
                _isLoadingConfig = false;
            }
            ApplyTemplateSettings(template.Settings ?? new TemplateSettings());
            SyncPrintOrderSelection(order);
            LoadHistory();
            RefreshStats();
            AddLog($"已选择订单: {order.DisplayName}", "INFO");
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
            template.Settings ??= new TemplateSettings();
            template.Settings.DataSources = fields.Select(field =>
                current.FirstOrDefault(source => string.Equals(source.Field, field, StringComparison.OrdinalIgnoreCase)) is DataSourceItem existing
                    ? CloneDataSource(existing)
                    : new DataSourceItem { Name = field, Field = field, Enabled = true }).ToList();
            MessageBox.Show(this, "新版模板的数据源已变化，系统已保留同名字段设置并添加新字段，请在订单管理页面核对。", "模板数据源已更新",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
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
            _orderContentPanel.AutoScrollMinSize = new Size(740, 775);
            _orderContentPanel.BackColor = MiuiTheme.Background;
            _orderTemplateDrafts.Clear();
            _selectedOrderTemplateDraft = null;
            _editingOrder = order;
            var contentWidth = Math.Max(700, _orderContentPanel.ClientSize.Width - 25);
            var fieldGap = 10;
            var fieldWidth = (contentWidth - 30 - fieldGap * 3) / 4;
            var addOrderTop = new Button { Text = "添加订单", Location = new Point(10, 10), Size = new Size(90, 28) };
            addOrderTop.Click += (s, e) => ShowAddOrderPage();
            _orderContentPanel.Controls.Add(addOrderTop);
            MiuiTheme.StyleButton(addOrderTop, order == null);

            var orderSelectLabel = new Label
            {
                Text = "选择订单：",
                Location = new Point(110, 16), Size = new Size(75, 18)
            };
            var orderSelector = new ComboBox
            {
                Location = new Point(185, 12),
                Size = new Size(Math.Max(220, contentWidth - 185), 25),
                DropDownStyle = ComboBoxStyle.DropDownList,
                DisplayMember = nameof(PackagingOrder.DisplayName),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            foreach (var item in _orders.Orders) orderSelector.Items.Add(item);
            if (order != null)
            {
                var selected = orderSelector.Items.Cast<PackagingOrder>().FirstOrDefault(item => string.Equals(item.Key, order.Key, StringComparison.OrdinalIgnoreCase));
                if (selected != null) orderSelector.SelectedItem = selected;
            }
            orderSelector.SelectedIndexChanged += (s, e) =>
            {
                if (_loadingOrderEditor || !(orderSelector.SelectedItem is PackagingOrder selectedOrder)) return;
                if (!ConfirmOrderEditorChanges())
                {
                    _loadingOrderEditor = true;
                    try
                    {
                        if (order != null)
                            orderSelector.SelectedItem = orderSelector.Items.Cast<PackagingOrder>().FirstOrDefault(item => string.Equals(item.Key, order.Key, StringComparison.OrdinalIgnoreCase));
                        else
                            orderSelector.SelectedIndex = -1;
                    }
                    finally { _loadingOrderEditor = false; }
                    return;
                }
                var previousOrder = _activeOrder ?? order;
                SelectOrder(selectedOrder);
                if (!ApplyOrder(selectedOrder))
                {
                    _loadingOrderEditor = true;
                    try
                    {
                        if (previousOrder != null)
                        {
                            SelectOrder(previousOrder);
                            orderSelector.SelectedItem = orderSelector.Items.Cast<PackagingOrder>().FirstOrDefault(item => string.Equals(item.Key, previousOrder.Key, StringComparison.OrdinalIgnoreCase));
                        }
                        else orderSelector.SelectedIndex = -1;
                    }
                    finally { _loadingOrderEditor = false; }
                    return;
                }
                ShowOrderSettingsPage(selectedOrder);
            };
            _orderContentPanel.Controls.Add(orderSelectLabel);
            _orderContentPanel.Controls.Add(orderSelector);
            MiuiTheme.StyleLabel(orderSelectLabel);

            _txtOrderCustomer = AddOrderPageComboBox("客户", 10, 50, fieldWidth, _orders.Orders.Select(item => item.Customer));
            _txtOrderModel = AddOrderPageComboBox("机型", 10 + (fieldWidth + fieldGap), 50, fieldWidth, _orders.Orders.Select(item => item.ProductModel));
            _txtOrderColor = AddOrderPageComboBox("颜色", 10 + (fieldWidth + fieldGap) * 2, 50, fieldWidth, _orders.Orders.Select(item => item.Color));
            _txtOrderNumber = AddOrderPageTextBox("订单号", 10 + (fieldWidth + fieldGap) * 3, 50, fieldWidth);
            if (order != null)
            {
                _txtOrderCustomer.Text = order.Customer;
                _txtOrderModel.Text = order.ProductModel;
                _txtOrderColor.Text = order.Color;
                _txtOrderNumber.Text = order.OrderNumber;
                _txtOrderNumber.ReadOnly = true;
                foreach (var template in order.Templates ?? new List<OrderTemplate>()) _orderTemplateDrafts.Add(CloneOrderTemplate(template));
            }

            var templateLabel = new Label { Text = "模板配置（点击卡片切换，每个模板独立保存设置）", Location = new Point(10, 105), Size = new Size(contentWidth, 20), Font = new Font(Font, FontStyle.Bold) };
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
            var loadFields = new Button { Text = "读取数据源", Location = new Point(templateActionX, 323), Size = new Size(actionWidth, 28), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            loadFields.Click += (s, e) => LoadOrderDataSourceRows();
            var removeTemplate = new Button { Text = "删除模板", Location = new Point(templateActionX - actionWidth - fieldGap, 288), Size = new Size(actionWidth, 28), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            removeTemplate.Click += (s, e) => RemoveSelectedOrderTemplateDraft();
            _orderContentPanel.Controls.Add(browseTemplate);
            _orderContentPanel.Controls.Add(loadFields);
            _orderContentPanel.Controls.Add(removeTemplate);
            MiuiTheme.StyleButton(browseTemplate);
            MiuiTheme.StyleButton(loadFields);
            MiuiTheme.StyleButton(removeTemplate);

            var printerLabelX = 10;
            var printerLabel = new Label { Text = "打印机：", Location = new Point(printerLabelX, 329), Size = new Size(65, 18) };
            var copiesX = templateActionX - 105;
            _cmbOrderPrinter = new ComboBox { Location = new Point(printerLabelX + 65, 325), Size = new Size(Math.Max(120, copiesX - printerLabelX - 75), 25), DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            foreach (var printer in cmbPrinter.Items) _cmbOrderPrinter.Items.Add(printer);
            if (cmbPrinter.SelectedItem != null && _cmbOrderPrinter.Items.Contains(cmbPrinter.SelectedItem)) _cmbOrderPrinter.SelectedItem = cmbPrinter.SelectedItem;
            else if (_cmbOrderPrinter.Items.Count > 0) _cmbOrderPrinter.SelectedIndex = 0;
            var copiesLabel = new Label { Text = "份数：", Location = new Point(copiesX, 329), Size = new Size(50, 18), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            _numOrderCopies = new NumericUpDown { Location = new Point(copiesX + 50, 325), Size = new Size(55, 25), Minimum = 1, Maximum = 99, Value = numCopies.Value, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            _orderContentPanel.Controls.Add(printerLabel);
            _orderContentPanel.Controls.Add(_cmbOrderPrinter);
            _orderContentPanel.Controls.Add(copiesLabel);
            _orderContentPanel.Controls.Add(_numOrderCopies);
            MiuiTheme.StyleLabel(printerLabel);
            MiuiTheme.StyleLabel(copiesLabel);

            _chkOrderInputValidation = new CheckBox { Text = "本地完整匹配", Location = new Point(10, 363), Size = new Size(120, 22), Enabled = false };
            _chkOrderDuplicateValidation = new CheckBox { Text = "重复校验", Location = new Point(140, 363), Size = new Size(90, 22) };
            _chkOrderLengthValidation = new CheckBox { Text = "长度校验", Location = new Point(240, 363), Size = new Size(90, 22) };
            var globalLengthLabel = new Label { Text = "全局长度：", Location = new Point(340, 366), Size = new Size(75, 18) };
            _numOrderGlobalLength = new NumericUpDown { Location = new Point(415, 361), Size = new Size(70, 25), Minimum = 0, Maximum = 512 };
            var chooseValidationData = new Button { Text = "选择校验数据", Location = new Point(500, 359), Size = new Size(100, 28) };
            chooseValidationData.Click += (s, e) => SelectOrderValidationData();
            _lblOrderLocalData = new Label { Text = "校验数据：未配置", Location = new Point(555, 366), Size = new Size(Math.Max(180, contentWidth - 545), 18), AutoEllipsis = true };
            _orderContentPanel.Controls.Add(_chkOrderInputValidation);
            _orderContentPanel.Controls.Add(_chkOrderDuplicateValidation);
            _orderContentPanel.Controls.Add(_chkOrderLengthValidation);
            _orderContentPanel.Controls.Add(globalLengthLabel);
            _orderContentPanel.Controls.Add(_numOrderGlobalLength);
            _orderContentPanel.Controls.Add(chooseValidationData);
            _orderContentPanel.Controls.Add(_lblOrderLocalData);
            MiuiTheme.StyleLabel(globalLengthLabel);
            MiuiTheme.StyleLabel(_lblOrderLocalData, true);

            var dataSourceTitle = new Label
            {
                Text = "数据源详细设置",
                Location = new Point(10, 398), Size = new Size(contentWidth, 20),
                Font = new Font(Font, FontStyle.Bold)
            };
            _orderContentPanel.Controls.Add(dataSourceTitle);
            MiuiTheme.StyleLabel(dataSourceTitle);

            _orderDataSourcesGrid = new DataGridView
            {
                Location = new Point(10, 422),
                Size = new Size(contentWidth, 285),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
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
            _orderDataSourcesGrid.ColumnHeadersDefaultCellStyle.Font = new Font(Font, FontStyle.Bold);
            _orderDataSourcesGrid.DefaultCellStyle.SelectionBackColor = MiuiTheme.PrimaryLight;
            _orderDataSourcesGrid.DefaultCellStyle.SelectionForeColor = MiuiTheme.TextPrimary;
            ConfigureOrderDataSourceGrid();
            _orderDataSourcesGrid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (_orderDataSourcesGrid.IsCurrentCellDirty) _orderDataSourcesGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _orderDataSourcesGrid.CellValueChanged += OrderDataSourcesGrid_CellValueChanged;
            _orderDataSourcesGrid.CellContentClick += OrderDataSourcesGrid_CellContentClick;
            _orderDataSourcesGrid.DataError += (s, e) => { e.ThrowException = false; };
            _orderContentPanel.Controls.Add(_orderDataSourcesGrid);

            var save = new Button { Text = order == null ? "保存订单" : "保存设置", Location = new Point(10 + contentWidth - 95, 722), Size = new Size(95, 30), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            save.Click += (s, e) => SaveOrderFromPage();
            _orderContentPanel.Controls.Add(save);
            MiuiTheme.StyleButton(save, true);

            _txtOrderTemplate.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _lblOrderLocalData.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            foreach (var control in new Control[] { _txtOrderCustomer, _txtOrderModel, _txtOrderColor, _txtOrderNumber })
                control.TextChanged += (s, e) => MarkOrderEditorDirty();
            _cmbOrderPrinter.SelectedIndexChanged += (s, e) => MarkOrderEditorDirty();
            _numOrderCopies.ValueChanged += (s, e) => MarkOrderEditorDirty();
            _chkOrderInputValidation.CheckedChanged += (s, e) => { UpdateOrderValidationControls(); MarkOrderEditorDirty(); };
            _chkOrderDuplicateValidation.Checked = _duplicateValidationEnabled;
            _chkOrderDuplicateValidation.CheckedChanged += (s, e) => MarkOrderEditorDirty();
            _chkOrderLengthValidation.CheckedChanged += (s, e) => { UpdateOrderValidationControls(); ApplyOrderGlobalLengthToGrid(true); MarkOrderEditorDirty(); };
            _numOrderGlobalLength.ValueChanged += (s, e) => { ApplyOrderGlobalLengthToGrid(true); MarkOrderEditorDirty(); };
            UpdateOrderValidationControls();

            RefreshOrderTemplateCards();
            if (_orderTemplateDrafts.Count > 0) SelectOrderTemplateDraft(_orderTemplateDrafts[0]);
            _loadingOrderEditor = false;
            _orderEditorDirty = false;
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
            return combo;
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
            SaveSelectedOrderTemplateDraft();
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
                RefreshOrderTemplateCards();
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
            RefreshOrderTemplateCards();
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
                Settings = CloneTemplateSettings(template.Settings)
            };
        }

        private static TemplateSettings CloneTemplateSettings(TemplateSettings settings)
        {
            settings ??= new TemplateSettings();
            return new TemplateSettings
            {
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
                    Size = new Size(Math.Max(320, _orderTemplateCards.ClientSize.Width - 20), 52),
                    Padding = new Padding(10, 16, 0, 0),
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
                    Size = new Size(210, 52),
                    TextAlign = ContentAlignment.MiddleLeft,
                    Tag = template,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = selected ? MiuiTheme.PrimaryLight : Color.White,
                    ForeColor = selected ? MiuiTheme.PrimaryDark : MiuiTheme.TextPrimary,
                    Margin = new Padding(4),
                    Padding = new Padding(8, 3, 8, 3),
                    Cursor = Cursors.Hand
                };
                card.FlatAppearance.BorderColor = selected ? Color.FromArgb(55, 115, 205) : Color.FromArgb(205, 210, 220);
                card.Click += (s, e) => SelectOrderTemplateDraft((OrderTemplate)((Button)s).Tag);
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
            var scope = FirstNonEmpty(_txtOrderNumber?.Text, _editingOrder?.Key, _activeOrder?.Key, "draft");
            targetTemplate.Settings.LocalDataStoragePath = SaveValidationDataSnapshot(scope, targetTemplate.Id, targetTemplate.SourcePath, imported.Values);
            targetTemplate.Settings.LocalData = new List<string>();
            if (ReferenceEquals(targetTemplate, _selectedOrderTemplateDraft))
            {
                _chkOrderInputValidation.Enabled = true;
                _chkOrderInputValidation.Checked = true;
                _lblOrderLocalData.Text = $"校验数据：{path}（{imported.Values.Count} 条，{imported.ColumnName}）";
            }
            MarkOrderEditorDirty();
        }

        private static string SaveValidationDataSnapshot(string orderScope, string templateId, string templatePath, HashSet<string> values)
        {
            AppPaths.Initialize();
            var hashInput = $"{orderScope}|{templateId}|{templatePath}";
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashInput))).Substring(0, 24);
            var targetPath = Path.Combine(AppPaths.ValidationDataDirectory, $"{hash}.txt");
            File.WriteAllLines(targetPath, values.OrderBy(value => value, NaturalStringComparer.Instance), Encoding.UTF8);
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

        private LocalDataImportResult ReadValidationDataFile(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".csv") return ReadCsvValidationData(path);
            if (ext == ".xlsx" || ext == ".xls") return ReadExcelValidationData(path);
            var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in File.ReadLines(path))
            { var value = line.Trim(); if (!string.IsNullOrEmpty(value)) values.Add(value); }
            return new LocalDataImportResult(values, "文本");
        }

        private LocalDataImportResult ReadCsvValidationData(string path)
        {
            using (var enumerator = File.ReadLines(path).GetEnumerator())
            {
                if (!enumerator.MoveNext()) { MessageBox.Show(this, "CSV 文件为空"); return null; }
                var firstRow = ParseCsvLine(enumerator.Current);
                var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (firstRow.Count <= 1)
                {
                    if (firstRow.Count == 1 && !string.IsNullOrWhiteSpace(firstRow[0])) values.Add(firstRow[0].Trim());
                    while (enumerator.MoveNext())
                    {
                        var columns = ParseCsvLine(enumerator.Current);
                        if (columns.Count > 0 && !string.IsNullOrWhiteSpace(columns[0])) values.Add(columns[0].Trim());
                    }
                    return new LocalDataImportResult(values, "单列");
                }
                var colIdx = PromptForColumnSelection(firstRow, Path.GetFileName(path));
                if (colIdx < 0) return null;
                while (enumerator.MoveNext())
                {
                    var columns = ParseCsvLine(enumerator.Current);
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
            {
                Values = values;
                ColumnName = columnName;
            }

            public HashSet<string> Values { get; }
            public string ColumnName { get; }
        }

        private void SelectOrderTemplateDraft(OrderTemplate template)
        {
            if (_loadingOrderTemplate) return;
            SaveSelectedOrderTemplateDraft();
            var wasLoadingEditor = _loadingOrderEditor;
            _loadingOrderEditor = true;
            _selectedOrderTemplateDraft = template;
            _txtOrderTemplate.Text = _selectedOrderTemplateDraft?.SourcePath ?? "";
            var settings = _selectedOrderTemplateDraft?.Settings;
            if (settings != null)
            {
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
            }
            LoadOrderSettingsIntoGrid(_selectedOrderTemplateDraft?.Settings);
            _loadingOrderEditor = wasLoadingEditor;
            RefreshOrderTemplateCards();
        }

        private void SaveSelectedOrderTemplateDraft()
        {
            if (_selectedOrderTemplateDraft == null || _orderDataSourcesGrid == null) return;
            _selectedOrderTemplateDraft.Settings = BuildOrderTemplateSettings(_selectedOrderTemplateDraft.SourcePath, BuildDataSourcesFromOrderGrid());
        }

        private TemplateSettings BuildOrderTemplateSettings(string templatePath, List<DataSourceItem> dataSources)
        {
            var settings = BuildTemplateSettings(templatePath, dataSources);
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
        }

        private void ConfigureOrderDataSourceGrid()
        {
            _orderDataSourcesGrid.Columns.Clear();
            _orderDataSourcesGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Enabled", HeaderText = "使用", Width = 48 });
            _orderDataSourcesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Field", HeaderText = "字段名", ReadOnly = true, MinimumWidth = 140, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            _orderDataSourcesGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "LockEnabled", HeaderText = "锁定", Visible = false });
            _orderDataSourcesGrid.Columns.Add(new DataGridViewButtonColumn { Name = "LockToggle", HeaderText = "锁定", Width = 52, FlatStyle = FlatStyle.Flat });
            _orderDataSourcesGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "AutoIncrement", HeaderText = "增降序", Width = 66 });
            _orderDataSourcesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "AutoStep", HeaderText = "步长", Width = 55 });
            _orderDataSourcesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "LockedValue", HeaderText = "锁定后输入值", Width = 130 });
            _orderDataSourcesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ExpectedLength", HeaderText = "长度", Width = 55 });
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
            if (columnName == "LockEnabled")
                UpdateOrderDataSourceRowState(_orderDataSourcesGrid.Rows[e.RowIndex]);
            if (columnName == "Enabled")
            {
                SaveSelectedOrderTemplateDraft();
                RefreshOrderTemplateCards();
            }
            if (columnName == "ExpectedLength" || columnName == "LockedValue" || columnName == "AutoIncrement" || columnName == "AutoStep" || columnName == "Enabled")
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
            row.Cells["LockToggle"].Value = lockEnabled ? "锁" : "开";
            row.Cells["LockedValue"].ReadOnly = !lockEnabled;
            row.Cells["LockedValue"].Style.BackColor = lockEnabled ? SystemColors.Window : SystemColors.Control;
            if (!lockEnabled)
            {
                var incrementIndex = _orderDataSourcesGrid.Columns["AutoIncrement"].Index;
                var stepIndex = _orderDataSourcesGrid.Columns["AutoStep"].Index;
                row.Tag = new OrderRowLockState
                {
                    AutoIncrement = ToBoolean(row.Cells["AutoIncrement"].Value),
                    AutoStep = row.Cells["AutoStep"].Value,
                    LockedValue = row.Cells["LockedValue"].Value?.ToString() ?? ""
                };
                row.Cells[incrementIndex] = new DataGridViewTextBoxCell { Value = "", Style = new DataGridViewCellStyle { BackColor = SystemColors.Control } };
                row.Cells[incrementIndex].ReadOnly = true;
                row.Cells[stepIndex] = new DataGridViewTextBoxCell { Value = "", Style = new DataGridViewCellStyle { BackColor = SystemColors.Control } };
                row.Cells[stepIndex].ReadOnly = true;
                row.Cells["LockedValue"].Value = "";
            }
            else
            {
                var savedState = row.Tag as OrderRowLockState;
                if (!(row.Cells["AutoIncrement"] is DataGridViewCheckBoxCell))
                    row.Cells[_orderDataSourcesGrid.Columns["AutoIncrement"].Index] = new DataGridViewCheckBoxCell { Value = savedState?.AutoIncrement ?? false };
                if (row.Cells["AutoStep"].ReadOnly)
                    row.Cells[_orderDataSourcesGrid.Columns["AutoStep"].Index] = new DataGridViewTextBoxCell { Value = savedState?.AutoStep ?? 1 };
                if (savedState != null) row.Cells["LockedValue"].Value = savedState.LockedValue;
                row.Tag = null;
            }
        }

        private sealed class OrderRowLockState
        {
            public bool AutoIncrement { get; set; }
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
            MarkOrderEditorDirty();
        }

        private void AddOrderDataSourceRow(DataSourceItem source)
        {
            var lockEnabled = source.IsLocked || source.LockAfterInput || source.AutoIncrement;
            var rowIndex = _orderDataSourcesGrid.Rows.Add(source.Enabled, source.Field, lockEnabled,
                lockEnabled ? "锁" : "开",
                source.AutoIncrement,
                source.AutoStep == 0 ? 1 : source.AutoStep, source.LockedValue, source.ExpectedLength);
            _orderDataSourcesGrid.Rows[rowIndex].Cells["ExpectedLength"].Tag = source.LengthEdited;
            UpdateOrderDataSourceRowState(_orderDataSourcesGrid.Rows[rowIndex]);
        }

        private void SaveOrderFromPage()
        {
            SaveSelectedOrderTemplateDraft();
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
            if (_editingOrder == null && _orders.Contains(input.Customer, input.ProductModel, input.Color, input.OrderNumber))
            { MessageBox.Show(this, "订单号已存在。", "添加订单", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
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
                    template.Settings.TemplateName = Path.GetFileName(template.SourcePath);
                    template.Settings.TemplatePath = template.SourcePath;
                    var localData = GetTemplateLocalData(template.Settings);
                    if (localData.Count > 0)
                    {
                        template.Settings.LocalDataStoragePath = SaveValidationDataSnapshot(savedOrder.Key, template.Id, template.SourcePath, localData);
                        template.Settings.LocalData = new List<string>();
                    }
                    savedOrder.Templates.Add(template);
                }
                _orders.Add(savedOrder);
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

        private void SyncPrintOrderSelection(PackagingOrder order)
        {
            if (_cmbPrintOrder == null || order == null || ReferenceEquals(_cmbPrintOrder.SelectedItem, order)) return;
            _loadingOrderFilters = true;
            try
            {
                var match = _cmbPrintOrder.Items.Cast<PackagingOrder>().FirstOrDefault(item => string.Equals(item.Key, order.Key, StringComparison.OrdinalIgnoreCase));
                if (match != null) _cmbPrintOrder.SelectedItem = match;
            }
            finally { _loadingOrderFilters = false; }
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
                var autoIncrement = lockEnabled && ToBoolean(row.Cells["AutoIncrement"].Value);
                var lockedValue = row.Cells["LockedValue"].Value?.ToString() ?? "";
                result.Add(new DataSourceItem
                {
                    Name = field,
                    Field = field,
                    Enabled = ToBoolean(row.Cells["Enabled"].Value),
                    AutoIncrement = autoIncrement,
                    AutoStep = step == 0 ? 1 : Math.Max(-99, Math.Min(99, step)),
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
                ApplyStartupTemplateSelection();
                LoadHistory();
                RefreshStats();
                _isInitializing = false;

                if (!string.IsNullOrEmpty(_selectedTemplatePath) && File.Exists(_selectedTemplatePath) && _btService.IsConnected)
                {
                    var item = cmbTemplate.SelectedItem as TemplateItem;
                    if (item == null || !RestoreTemplateSettings(item.Name, item.FullPath))
                        LoadTemplateDataSources(_selectedTemplatePath);
                }

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
            var configuredPrinter = _activeOrderTemplate?.Settings?.Printer;
            if (string.IsNullOrWhiteSpace(configuredPrinter)) configuredPrinter = cmbPrinter.SelectedItem?.ToString();
            if (string.IsNullOrWhiteSpace(configuredPrinter)) configuredPrinter = IniReadValue("General", "Printer", _configFile);
            var wasLoading = _isLoadingConfig;
            _isLoadingConfig = true;
            try
            {
                cmbPrinter.Items.Clear();
                foreach (var printer in printers) cmbPrinter.Items.Add(printer);
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
                _loadingOrderFilters = true;
                try
                {
                    _cmbPrintOrder.SelectedIndex = -1;
                    ClearOrderSelection();
                }
                finally { _loadingOrderFilters = false; }
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
            Task.Run(() =>
            {
                var names = _btService.GetTemplateDataSources(path);
                PostToUi(() =>
                {
                    if (!string.Equals(path, _selectedTemplatePath, StringComparison.OrdinalIgnoreCase)) return;
                    if (names.Count == 0) return;
                    var previousSources = _dataSources.ToList();
                    var dlg = new DataSourceSelectDialog(names, _dataSources, _hasSavedDataSourceOrder, _lengthValidationEnabled, _globalExpectedLength);
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        _dataSources = dlg.SelectedSources;
                        UpdateLengthRevisions(previousSources, _dataSources);
                        _hasSavedDataSourceOrder = true;
                        RebuildInputFields();
                        SaveConfig();
                        SaveCurrentTemplateSettings();
                        AddLog($"已加载 {names.Count} 个数据源，选择了 {_dataSources.Count} 个", "SUCCESS");
                    }
                });
            });
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
            var dlg = new DataSourceSelectDialog(fields, _dataSources, true, _lengthValidationEnabled, _globalExpectedLength);
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
                return f.ShowDialog(this) == DialogResult.OK ? txt.Text.Split('\n').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList() : null;
            }
        }

        private TemplateSettings BuildTemplateSettings(string templatePath, List<DataSourceItem> dataSources)
        {
            return new TemplateSettings
            {
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
            inputPanel.Controls.Clear();
            var enabled = _dataSources.Where(d => d.Enabled).ToList();
            _inputTextBoxes = new TextBox[enabled.Count];
            _rowPanels = new Panel[enabled.Count];
            _lockButtons = new Button[enabled.Count];
            int y = 4;
            for (int i = 0; i < enabled.Count; i++)
            {
                var rowPanel = new Panel
                {
                    Location = new Point(0, y),
                    Size = new Size(inputPanel.ClientSize.Width, 28),
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
                    Location = new Point(2, 3),
                    Size = new Size(22, 22),
                    Cursor = Cursors.Hand,
                    Tag = i,
                    Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
                    ForeColor = Color.FromArgb(160, 160, 160)
                };
                grip.MouseDown += Grip_MouseDown;

                var lbl = new Label
                {
                    Text = enabled[i].Name + "：",
                    Location = new Point(52, 3),
                    Size = new Size(75, 20),
                    TextAlign = ContentAlignment.MiddleRight
                };
                MiuiTheme.StyleLabel(lbl);

                var lockButton = new Button
                {
                    Location = new Point(rowPanel.Width - 28, 1),
                    Size = new Size(24, 24),
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
                    Location = new Point(130, 0),
                    Size = new Size(rowPanel.Width - 164, 25),
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
                y += 32;
            }

            int requiredHeight = Math.Max(40, y + 4);
            int maxHeight = 180;
            inputPanel.Height = Math.Min(requiredHeight, maxHeight);
            inputPanel.AutoScroll = true;
            inputPanel.AutoScrollMinSize = new Size(0, requiredHeight);

            btnPrint.Top = inputPanel.Bottom + 8;
            btnPrint.Width = inputPanel.Width;
            tabBottom.Top = btnPrint.Bottom + 8;
            tabBottom.Height = groupBoxLog.Top - tabBottom.Top - 8;
        }

        private void InputPanel_SizeChanged(object sender, EventArgs e)
        {
            int w = inputPanel.ClientSize.Width;
            for (int i = 0; i < _rowPanels.Length; i++)
            {
                if (_rowPanels[i] == null) continue;
                _rowPanels[i].Width = w;
                if (i < _inputTextBoxes.Length && _inputTextBoxes[i] != null)
                    _inputTextBoxes[i].Width = Math.Max(80, w - 164);
                if (i < _lockButtons.Length && _lockButtons[i] != null)
                    _lockButtons[i].Left = w - 28;
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

        private void SetInputsReadOnly(bool ro)
        {
            foreach (var tb in _inputTextBoxes)
                if (tb != null) { tb.ReadOnly = ro; tb.BackColor = ro ? SystemColors.Control : MiuiTheme.InputBackground; }
            foreach (var button in _lockButtons)
                if (button != null) button.Enabled = true;
        }

        private void RestoreInputReadOnlyStates(bool[] readOnlyStates)
        {
            for (int i = 0; i < readOnlyStates.Length && i < _inputTextBoxes.Length; i++)
            {
                var readOnly = readOnlyStates[i];
                _inputTextBoxes[i].ReadOnly = readOnly;
                _inputTextBoxes[i].BackColor = readOnly ? SystemColors.Control : MiuiTheme.InputBackground;
                if (i < _lockButtons.Length && _lockButtons[i] != null)
                    _lockButtons[i].Enabled = true;
            }
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
                var firstRow = ParseCsvLine(enumerator.Current);
                var data = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (firstRow.Count <= 1)
                {
                    if (firstRow.Count == 1 && !string.IsNullOrWhiteSpace(firstRow[0])) data.Add(firstRow[0].Trim());
                    while (enumerator.MoveNext())
                    {
                        var cols = ParseCsvLine(enumerator.Current);
                        if (cols.Count > 0 && !string.IsNullOrWhiteSpace(cols[0])) data.Add(cols[0].Trim());
                    }
                    ApplyLoadedLocalData(path, data, "单列", "CSV");
                    return;
                }
                var colIdx = PromptForColumnSelection(firstRow, Path.GetFileName(path));
                if (colIdx < 0) return;
                while (enumerator.MoveNext())
                {
                    var cols = ParseCsvLine(enumerator.Current);
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
            _localDataStoragePath = SaveValidationDataSnapshot(_activeOrder?.Key ?? "global", _activeOrderTemplate?.Id ?? "global", _selectedTemplatePath, data);
            _localDataColumnName = columnName;
            UpdateLocalDataValidationAvailability();
            _useLocalDataValidation = data.Count > 0;
            chkUseLocalData.Checked = _useLocalDataValidation;
            UpdateLocalDataLabel($"已加载: {data.Count} 条 [{columnName}] ({Path.GetFileName(path)})");
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
                return f.ShowDialog(this) == DialogResult.OK ? lst.SelectedIndex : -1;
            }
        }

        private List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(line)) return result;
            var current = new System.Text.StringBuilder();
            bool inQuotes = false;
            foreach (var c in line)
            {
                if (c == '"') inQuotes = !inQuotes;
                else if (c == ',' && !inQuotes) { result.Add(current.ToString()); current.Clear(); }
                else current.Append(c);
            }
            result.Add(current.ToString());
            return result;
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
            if (!_lengthValidationEnabled) return 0;
            if (source.ExpectedLength > 0)
                return source.ExpectedLength;
            return _globalExpectedLength;
        }

        private string GetDuplicateValidationMessage(DataSourceItem source, string value, HashSet<string> acceptedValues)
        {
            if (!_duplicateValidationEnabled) return null;
            if (acceptedValues.Contains(value))
                return $"输入数据重复：{value}\n请重新输入 {source.Name}。";
            if (_history.ContainsAnyValue(Path.GetFileName(_selectedTemplatePath), _selectedTemplatePath, value))
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

        private bool ValidateLocalData(Dictionary<string, string> fieldValues)
        {
            if (!_useLocalDataValidation || _localData.Count == 0) return true;
            var notInLocal = new List<string>();
            foreach (var kv in fieldValues)
            {
                if (string.IsNullOrEmpty(kv.Value)) continue;
                if (!_localData.Contains(kv.Value))
                    notInLocal.Add($"{kv.Key}={kv.Value}");
            }
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

            var fieldValues = new Dictionary<string, string>();
            for (int i = 0; i < enabled.Count; i++)
            {
                var val = _inputTextBoxes[i]?.Text?.Trim() ?? "";
                if (string.IsNullOrEmpty(val) && !enabled[i].IsLocked)
                { MessageBox.Show(this, $"\"{enabled[i].Name}\" 不能为空"); _inputTextBoxes[i]?.Focus(); return; }
                fieldValues[enabled[i].Field] = val;
            }

            if (!ValidateInputValues(enabled, fieldValues)) return;

            // Local data validation - only if enabled
            if (_useLocalDataValidation)
            {
                if (!ValidateLocalData(fieldValues))
                { AddLog("用户取消（本地数据校验失败）", "WARNING"); return; }
            }

            int copies = (int)numCopies.Value;
            var templatePath = _selectedTemplatePath;
            var templateName = Path.GetFileName(templatePath);
            var readOnlyStates = _inputTextBoxes.Select(input => input?.ReadOnly ?? false).ToArray();
            SetStatus("打印中..."); SetInputsReadOnly(true); SetPrintEnvironmentEnabled(false);
            AddLog($"打印: {string.Join(", ", fieldValues.Select(kv => $"{kv.Key}={kv.Value}"))}", "INFO");

            Task.Run(() =>
            {
                PrintResult result;
                try
                {
                    result = _btService.Print(templatePath, fieldValues, printer, copies);
                }
                catch (Exception ex)
                {
                    LoggerService.Error("打印失败", ex);
                    result = new PrintResult(false, ex.Message);
                }
                PostToUi(() =>
                {
                    var refreshHistoryAndStats = true;
                    try
                    {
                        if (result.Success)
                        {
                            SetStatus("打印完成");
                            AddLog("打印完成", "SUCCESS");
                            if (!_history.Add(templateName, templatePath, fieldValues, "PASS", printer, copies))
                            {
                                SetStatus("打印完成，历史保存失败");
                                AddLog("打印已完成，但历史记录保存失败；本次数据不会进入重复校验索引。", "ERROR");
                                RestoreInputReadOnlyStates(readOnlyStates);
                                refreshHistoryAndStats = false;
                                return;
                            }

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
                        else
                        {
                            SetStatus("打印失败");
                            AddLog($"打印失败: {result.ErrorMessage}", "ERROR");
                            if (!_history.Add(templateName, templatePath, fieldValues, "FAIL", printer, copies))
                                AddLog("失败打印历史记录保存失败。", "ERROR");
                            RestoreInputReadOnlyStates(readOnlyStates);
                        }
                    }
                    finally
                    {
                        SetPrintEnvironmentEnabled(true);
                        if (refreshHistoryAndStats)
                        {
                            LoadHistory(); RefreshStats();
                            if (!result.Success) SetStatus("打印失败");
                        }
                    }
                });
            });
        }

        private bool ValidateInputValues(List<DataSourceItem> enabled, Dictionary<string, string> fieldValues, bool checkDuplicates = true)
        {
            if (_lengthValidationEnabled)
            {
                for (int i = 0; i < enabled.Count; i++)
                {
                    fieldValues.TryGetValue(enabled[i].Field, out var value);
                    value ??= "";
                    var expectedLength = GetExpectedLength(enabled[i]);
                    if (expectedLength > 0 && value.Length != expectedLength)
                    {
                        MessageBox.Show(this, $"\"{enabled[i].Name}\" 必须为 {expectedLength} 位，当前为 {value.Length} 位。", "长度校验", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        if (!_inputTextBoxes[i].ReadOnly)
                        {
                            _inputTextBoxes[i].Text = "";
                            _inputTextBoxes[i].Focus();
                        }
                        return false;
                    }
                }
            }

            if (!_duplicateValidationEnabled || !checkDuplicates) return true;
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < enabled.Count; i++)
            {
                fieldValues.TryGetValue(enabled[i].Field, out var value);
                value ??= "";
                if (string.IsNullOrWhiteSpace(value)) continue;
                var isEditable = !enabled[i].IsLocked && !enabled[i].AutoIncrementLocked;
                var shouldCheckHistory = isEditable || enabled[i].AutoIncrement || enabled[i].AutoIncrementLocked;
                if (seen.ContainsKey(value) || shouldCheckHistory && _history.ContainsAnyValue(Path.GetFileName(_selectedTemplatePath), _selectedTemplatePath, value))
                {
                    MessageBox.Show(this, $"重复数据：{value}\n请重新输入 {enabled[i].Name}。", "数据校验", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    if (isEditable)
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

        #endregion

        #region History

        private void txtSearch_TextChanged(object sender, EventArgs e)
        {
            _historySearchTimer.Stop();
            _historySearchTimer.Start();
        }
        private void chkExactSearch_CheckedChanged(object sender, EventArgs e) => LoadHistory();
        private void btnClearSearch_Click(object sender, EventArgs e) { txtSearch.Text = ""; }
        private void btnClearHistory_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedTemplatePath))
            { MessageBox.Show(this, "请先选择模板"); return; }
            if (MessageBox.Show(this, "确定清空当前模板的全部记录？", "确认", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                if (!_history.Clear(Path.GetFileName(_selectedTemplatePath), _selectedTemplatePath))
                { MessageBox.Show(this, "清空历史记录失败，请检查文件权限。", "历史记录", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
                LoadHistory(); RefreshStats();
            }
        }
        private void btnExportHistory_Click(object sender, EventArgs e)
        {
            var records = GetCurrentHistoryRecords();
            if (records.Count == 0) { MessageBox.Show(this, "当前模板没有可导出的记录"); return; }
            using (var sfd = new SaveFileDialog { Filter = "CSV|*.csv", FileName = $"records_{DateTime.Now:yyyyMMdd_HHmmss}.csv" })
            {
                if (sfd.ShowDialog(this) == DialogResult.OK)
                { try { _history.Export(sfd.FileName, records); MessageBox.Show(this, "导出成功"); } catch (Exception ex) { MessageBox.Show(this, ex.Message); } }
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
                if (source.IsLocked && !source.AutoIncrement)
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

                var ok = new Button { Text = "补打印", Location = new Point(325, 345), Size = new Size(75, 28), DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "取消", Location = new Point(415, 345), Size = new Size(75, 28), DialogResult = DialogResult.Cancel };
                form.Controls.AddRange(new Control[] { lblDetails, txtDetails, lblPrinter, cmbReprintPrinter, ok, cancel });
                form.AcceptButton = ok;
                form.CancelButton = cancel;

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
            var enabled = _dataSources.Where(source => source.Enabled).ToList();
            if (enabled.Count > 0 && string.Equals(record.TemplatePath, _selectedTemplatePath, StringComparison.OrdinalIgnoreCase))
            {
                if (!ValidateInputValues(enabled, values, false)) return;
                if (_useLocalDataValidation && !ValidateLocalData(values)) return;
            }
            SetPrintEnvironmentEnabled(false);
            SetStatus("补打印中...");
            Task.Run(() =>
            {
                PrintResult result;
                try { result = _btService.Print(record.TemplatePath, values, printer, record.Copies); }
                catch (Exception ex) { result = new PrintResult(false, ex.Message); }
                PostToUi(() =>
                {
                    var historySaved = true;
                    try
                    {
                        historySaved = _history.Add(record.TemplateName, record.TemplatePath, values,
                            result.Success ? "REPRINT_PASS" : "REPRINT_FAIL", printer, record.Copies);
                        if (result.Success && historySaved) RestoreAutoIncrementInputsToPendingValues();
                        if (!historySaved)
                            AddLog(result.Success ? "补打印已完成，但历史记录保存失败。" : "补打印失败，且失败历史记录保存失败。", "ERROR");
                        else if (result.Success)
                            AddLog("历史记录补打印完成", "SUCCESS");
                        else
                            AddLog($"历史记录补打印失败: {result.ErrorMessage}", "ERROR");
                    }
                    finally
                    {
                        SetPrintEnvironmentEnabled(true);
                        LoadHistory();
                        RefreshStats();
                        SetStatus(result.Success ? (historySaved ? "补打印完成" : "补打印完成，历史保存失败") : (historySaved ? "补打印失败" : "补打印失败，历史保存失败"));
                    }
                });
            });
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

        private List<PrintRecord> GetCurrentHistoryRecords()
        {
            return _history.Search(Path.GetFileName(_selectedTemplatePath), _selectedTemplatePath, txtSearch?.Text ?? "", chkExactSearch.Checked);
        }

        private void LoadHistory()
        {
            dgvHistory.DataSource = null;
            var dt = new DataTable(); dt.Columns.Add("记录ID"); dt.Columns.Add("数据"); dt.Columns.Add("打印时间"); dt.Columns.Add("状态"); dt.Columns.Add("打印机"); dt.Columns.Add("份数");
            foreach (var r in GetCurrentHistoryRecords().AsEnumerable().Reverse())
            {
                var values = r.FieldValues != null && r.FieldValues.Count > 0
                    ? string.Join(" | ", r.FieldValues.Select(item => $"{item.Key}={item.Value}"))
                    : r.Imei;
                dt.Rows.Add(r.RecordId, values, r.PrintTime, r.Status, r.Printer, r.Copies);
            }
            dgvHistory.DataSource = dt;
            dgvHistory.Columns["记录ID"].Visible = false;

            // Apply color formatting to status column
            foreach (DataGridViewRow row in dgvHistory.Rows)
            {
                var statusCell = row.Cells["状态"];
                if (statusCell?.Value?.ToString().EndsWith("PASS", StringComparison.Ordinal) == true)
                {
                    statusCell.Style.ForeColor = Color.Green;
                    statusCell.Style.Font = new Font(dgvHistory.Font, FontStyle.Bold);
                }
                else if (statusCell?.Value?.ToString().EndsWith("FAIL", StringComparison.Ordinal) == true)
                {
                    statusCell.Style.ForeColor = Color.Red;
                    statusCell.Style.Font = new Font(dgvHistory.Font, FontStyle.Bold);
                }
            }
            dgvHistory.ClearSelection();
        }
        private void RefreshStats()
        {
            var records = _history.Search(Path.GetFileName(_selectedTemplatePath), _selectedTemplatePath, "", false);
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            var todayCount = records.Count(record => record.PrintTime.StartsWith(today));
            lblTodayCount.Text = todayCount.ToString();
            lblTotalCount.Text = records.Count.ToString();
            SetStatus($"就绪 | 今日: {todayCount} | 总计: {records.Count}");
        }

        private void DgvHistory_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            var row = dgvHistory.Rows[e.RowIndex];
            var imei = row.Cells["数据"].Value?.ToString() ?? "";
            var time = row.Cells["打印时间"].Value?.ToString() ?? "";
            var status = row.Cells["状态"].Value?.ToString() ?? "";

            var parts = imei.Split('|');
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"打印时间: {time}");
            sb.AppendLine($"状态: {status}");
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
            if (dgvHistory.SelectedRows.Count == 0)
            { MessageBox.Show(this, "请先选择一条历史记录"); return; }

            var row = dgvHistory.SelectedRows[0];
            var recordId = row.Cells["记录ID"].Value?.ToString() ?? "";
            var data = row.Cells["数据"].Value?.ToString() ?? "";
            if (MessageBox.Show(this, $"确定删除这条打印记录？\n\n{data}", "删除历史记录", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            var existedBeforeDelete = _history.GetById(recordId) != null;

            if (_history.Delete(recordId))
            {
                AddLog("已删除单条历史记录", "INFO");
                LoadHistory();
                RefreshStats();
            }
            else
            {
                MessageBox.Show(this, existedBeforeDelete ? "删除历史记录失败，请检查文件权限。" : "该历史记录已不存在，请刷新后重试。", "删除历史记录", MessageBoxButtons.OK, existedBeforeDelete ? MessageBoxIcon.Error : MessageBoxIcon.Information);
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
            ApplyTemplateSettings(settings);
            AddLog($"已恢复模板设置: {templateName}", "INFO");
            return true;
        }

        private void ApplyTemplateSettings(TemplateSettings settings)
        {
            _isLoadingConfig = true;
            try
            {
                _dataSources = (settings.DataSources ?? new List<DataSourceItem>()).Select(CloneDataSource).ToList();
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
                UpdateLocalDataLabel(_localData.Count > 0 ? $"已恢复: {_localData.Count} 条" : "");
                RebuildInputFields();
            }
            finally
            {
                _isLoadingConfig = false;
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
                LengthEdited = source.LengthEdited
            };
        }

        private void SaveConfig()
        {
            var dir = Path.GetDirectoryName(_configFile); if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            try
            {
                if (File.Exists(_configFile)) File.Copy(_configFile, _configFile + ".bak", true);
            }
            catch (Exception ex)
            {
                LoggerService.Warn($"备份配置失败: {ex.Message}");
            }
            IniWriteValue("General", "TemplatesFolder", _templatesFolder ?? "", _configFile);
            IniWriteValue("General", "Printer", cmbPrinter.SelectedItem?.ToString() ?? "", _configFile);
            IniWriteValue("General", "Copies", numCopies.Value.ToString(), _configFile);
            IniWriteValue("General", "InputValidation", _useLocalDataValidation.ToString(), _configFile);
            IniWriteValue("General", "DuplicateValidation", _duplicateValidationEnabled.ToString(), _configFile);
            IniWriteValue("General", "LengthValidation", _lengthValidationEnabled.ToString(), _configFile);
            IniWriteValue("General", "GlobalExpectedLength", _globalExpectedLength.ToString(), _configFile);
            IniWriteValue("General", "GlobalLengthRevision", _globalLengthRevision.ToString(), _configFile);
            IniWriteValue("General", "LengthRevisionCounter", _lengthRevisionCounter.ToString(), _configFile);
            IniWriteValue("General", "DSCount", _dataSources.Count.ToString(), _configFile);
            for (int i = 0; i < _dataSources.Count; i++)
            {
                IniWriteValue($"DS{i}", "Name", _dataSources[i].Name, _configFile);
                IniWriteValue($"DS{i}", "Field", _dataSources[i].Field, _configFile);
                IniWriteValue($"DS{i}", "Enabled", _dataSources[i].Enabled.ToString(), _configFile);
                IniWriteValue($"DS{i}", "AutoIncrement", _dataSources[i].AutoIncrement.ToString(), _configFile);
                IniWriteValue($"DS{i}", "AutoStep", _dataSources[i].AutoStep.ToString(), _configFile);
                IniWriteValue($"DS{i}", "IsLocked", _dataSources[i].IsLocked.ToString(), _configFile);
                IniWriteValue($"DS{i}", "LockAfterInput", _dataSources[i].LockAfterInput.ToString(), _configFile);
                IniWriteValue($"DS{i}", "LockedValue", _dataSources[i].LockedValue ?? "", _configFile);
                IniWriteValue($"DS{i}", "AutoIncrementLocked", _dataSources[i].AutoIncrementLocked.ToString(), _configFile);
                IniWriteValue($"DS{i}", "ExpectedLength", _dataSources[i].ExpectedLength.ToString(), _configFile);
                IniWriteValue($"DS{i}", "LengthRevision", _dataSources[i].LengthRevision.ToString(), _configFile);
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
            var dir = Path.GetDirectoryName(_configFile);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            IniWriteValue("General", "TemplatesFolder", _templatesFolder ?? "", _configFile);
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
                UpdateLocalDataLabel("");
                var copies = 1; int.TryParse(IniReadValue("General", "Copies", path), out copies); numCopies.Value = Math.Max(1, Math.Min(99, copies));
                bool.TryParse(IniReadValue("General", "InputValidation", path), out _useLocalDataValidation);
                if (!bool.TryParse(IniReadValue("General", "DuplicateValidation", path), out _duplicateValidationEnabled)) _duplicateValidationEnabled = true;
                chkDuplicateValidation.Checked = _duplicateValidationEnabled;
                bool.TryParse(IniReadValue("General", "LengthValidation", path), out _lengthValidationEnabled);
                int.TryParse(IniReadValue("General", "GlobalExpectedLength", path), out _globalExpectedLength);
                long.TryParse(IniReadValue("General", "GlobalLengthRevision", path), out _globalLengthRevision);
                long.TryParse(IniReadValue("General", "LengthRevisionCounter", path), out _lengthRevisionCounter);
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
                        LengthRevision = lengthRevision
                    });
                }
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
        { if (statusStrip.InvokeRequired) statusStrip.Invoke((Action)(() => lblStatus.Text = text)); else lblStatus.Text = text; }

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
            _lengthValidationEnabled = lengthValidationEnabled;
            _globalExpectedLength = globalExpectedLength;
            Text = "选择数据源 - 拖拽排序"; Size = new Size(980, 460);
            FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false;

            var lbl = new Label { Text = $"模板包含 {fields.Count} 个数据源，拖拽 ≡ 排序，勾选使用：", Location = new Point(10, 10), Size = new Size(940, 20) };

            chkSelectAll = new CheckBox { Text = "全选/全不选", Location = new Point(10, 32), Size = new Size(100, 20), Checked = true };
            chkSelectAll.CheckedChanged += (s, e) => { foreach (var r in _rows) r.CbEnabled.Checked = chkSelectAll.Checked; };

            var hdrGrip = new Label { Text = "排序", Location = new Point(15, 55), Size = new Size(22, 16), Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold) };
            var hdrName = new Label { Text = "字段名", Location = new Point(40, 55), Size = new Size(180, 16), Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold) };
            var hdrAuto = new Label { Text = "增序", Location = new Point(255, 55), Size = new Size(30, 16), Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold) };
            var hdrStep = new Label { Text = "步长", Location = new Point(300, 55), Size = new Size(60, 16), Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold) };
            var hdrLock = new Label { Text = "锁定方式", Location = new Point(380, 55), Size = new Size(80, 16), Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold) };
            var hdrLockedValue = new Label { Text = "锁定值（可空）", Location = new Point(505, 55), Size = new Size(120, 16), Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold) };
            var hdrLength = new Label { Text = "长度", Location = new Point(795, 55), Size = new Size(70, 16), Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold) };

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
                    existing?.AutoIncrementLocked ?? false, existing?.ExpectedLength ?? 0);
            }

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

            Controls.AddRange(new Control[] { lbl, chkSelectAll, hdrGrip, hdrName, hdrAuto, hdrStep, hdrLock, hdrLockedValue, hdrLength, _scrollPanel, infoLbl, btnSelectAll, btnSelectNone, ok, cancel });
            AcceptButton = ok; CancelButton = cancel;
        }

        private void CreateRow(string field, bool checkedVal, string displayName, bool autoInc, int autoStep,
            bool isLocked, bool lockAfterInput, string lockedValue, bool autoIncrementLocked, int expectedLength)
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
                Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
                ForeColor = Color.FromArgb(160, 160, 160)
            };
            row.Grip.MouseDown += Grip_MouseDown;

            row.CbEnabled = new CheckBox { Location = new Point(25, 2), Size = new Size(20, 20), Checked = checkedVal };

            row.LblField = new Label { Text = field, Location = new Point(50, 4), Size = new Size(190, 18), Cursor = Cursors.SizeAll };
            row.LblField.MouseDown += Row_MouseDown;

            row.CbAutoInc = new CheckBox { Location = new Point(255, 2), Size = new Size(20, 20), Checked = autoInc };

            row.NumStep = new NumericUpDown { Location = new Point(295, 0), Size = new Size(55, 25), Minimum = -99, Maximum = 99, Value = Math.Max(-99, Math.Min(99, autoStep)) };

            row.CmbLockMode = new ComboBox { Location = new Point(375, 0), Size = new Size(120, 25), DropDownStyle = ComboBoxStyle.DropDownList };
            row.CmbLockMode.Items.AddRange(new object[] { "不锁定", "固定锁定", "输入后锁定" });
            row.CmbLockMode.SelectedIndex = lockAfterInput ? 2 : isLocked ? 1 : 0;
            row.TxtLockedValue = new TextBox { Location = new Point(505, 0), Size = new Size(250, 25), Text = lockedValue ?? "" };
            row.NumExpectedLength = new NumericUpDown { Location = new Point(795, 0), Size = new Size(70, 25), Minimum = 0, Maximum = 512, Value = Math.Max(0, Math.Min(512, displayLength)) };
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

            row.RowPanel.Controls.AddRange(new Control[] { row.Grip, row.CbEnabled, row.LblField, row.CbAutoInc, row.NumStep, row.CmbLockMode, row.TxtLockedValue, row.NumExpectedLength });
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
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold)
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
    }
}
