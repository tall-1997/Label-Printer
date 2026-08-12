using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
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
        private readonly string _version = "v5.7.29";

        private List<DataSourceItem> _dataSources = new List<DataSourceItem>();
        private TextBox[] _inputTextBoxes = new TextBox[0];
        private Panel[] _rowPanels = new Panel[0];
        private Button[] _lockButtons = new Button[0];
        private string _templatesFolder = "";
        private string _selectedTemplatePath = "";
        private HashSet<string> _localData = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string _localDataPath = "";
        private bool _useLocalDataValidation = false;
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
        private Button _btnPrintPage;
        private Button _btnOrderPage;
        private Panel _orderPagePanel;
        private Panel _orderContentPanel;
        private ComboBox _txtOrderCustomer;
        private ComboBox _txtOrderModel;
        private ComboBox _txtOrderColor;
        private TextBox _txtOrderNumber;
        private TextBox _txtOrderTemplate;
        private DataGridView _orderDataSourcesGrid;
        private bool _loadingOrderFilters;

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
            FormClosing += (s, e) => { SaveCurrentTemplateSettings(); _historySearchTimer.Dispose(); _btService.Dispose(); };
            inputPanel.SizeChanged += InputPanel_SizeChanged;
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
            const int navWidth = 150;
            ClientSize = new Size(ClientSize.Width + navWidth, ClientSize.Height);
            MinimumSize = new Size(MinimumSize.Width + navWidth, MinimumSize.Height);
            foreach (Control control in Controls)
            {
                if (control == titlePanel || control == groupBoxLog || control == statusStrip) continue;
                control.Left += navWidth;
            }

            _navPanel = new Panel
            {
                Location = new Point(0, titlePanel.Bottom),
                Size = new Size(navWidth, groupBoxLog.Top - titlePanel.Bottom),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left,
                BackColor = Color.FromArgb(245, 246, 250)
            };
            _btnPrintPage = new Button { Text = "打印页面", Location = new Point(12, 18), Size = new Size(120, 34) };
            _btnOrderPage = new Button { Text = "订单管理", Location = new Point(12, 60), Size = new Size(120, 34) };
            _btnPrintPage.Click += (s, e) => ShowPrintPage();
            _btnOrderPage.Click += (s, e) => ShowOrderManagementPage();
            _navPanel.Controls.AddRange(new Control[] { _btnPrintPage, _btnOrderPage });
            Controls.Add(_navPanel);
            _navPanel.BringToFront();
            MiuiTheme.StyleButton(_btnPrintPage, true);
            MiuiTheme.StyleButton(_btnOrderPage);

            _orderPagePanel = new Panel
            {
                Location = new Point(navWidth + 10, titlePanel.Bottom + 8),
                Size = new Size(ClientSize.Width - navWidth - 20, groupBoxLog.Top - titlePanel.Bottom - 16),
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
            Controls.Add(_orderPagePanel);
            MiuiTheme.StyleGroupBox(_orderPanel);
            MiuiTheme.StyleButton(_btnAddOrder, true);
            btnEditDataSources.Visible = false;
        }

        private void ShowPrintPage()
        {
            _orderPagePanel.Visible = false;
            MiuiTheme.StyleButton(_btnPrintPage, true);
            MiuiTheme.StyleButton(_btnOrderPage);
        }

        private void ShowOrderManagementPage()
        {
            _orderPagePanel.Visible = true;
            _orderPagePanel.BringToFront();
            MiuiTheme.StyleButton(_btnOrderPage, true);
            MiuiTheme.StyleButton(_btnPrintPage);
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

        private void RefreshOrderFilters(OrderFilterLevel level = OrderFilterLevel.All)
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
            if (HasCompleteOrderSelection()) ApplySelectedOrder();
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
            var order = _orders.Find(_cmbOrderCustomer?.SelectedItem?.ToString(), _cmbOrderModel?.SelectedItem?.ToString(), _cmbOrderColor?.SelectedItem?.ToString(), _cmbOrderNumber?.SelectedItem?.ToString());
            if (order == null) return;
            if (string.IsNullOrEmpty(order.TemplatePath) || !File.Exists(order.TemplatePath))
            { MessageBox.Show(this, "订单归档模板文件不存在，请重新添加订单。", "订单模板", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            ApplyOrder(order);
        }

        private void ApplyOrder(PackagingOrder order)
        {
            if (!string.IsNullOrEmpty(_selectedTemplatePath)) SaveCurrentTemplateSettings();
            _isLoadingConfig = true;
            try
            {
                _selectedTemplatePath = order.TemplatePath;
                _templatesFolder = Path.GetDirectoryName(order.TemplatePath) ?? "";
                txtTemplateDir.Text = _templatesFolder;
                PopulateTemplateList(GetTemplateFiles(_templatesFolder));
                var match = cmbTemplate.Items.Cast<TemplateItem>().FirstOrDefault(item => string.Equals(item.FullPath, order.TemplatePath, StringComparison.OrdinalIgnoreCase));
                if (match != null) cmbTemplate.SelectedItem = match;
                lblSelectedTemplate.Text = Path.GetFileName(order.TemplatePath);
            }
            finally
            {
                _isLoadingConfig = false;
            }
            ApplyTemplateSettings(order.Settings ?? new TemplateSettings());
            LoadHistory();
            RefreshStats();
            AddLog($"已选择订单: {order.DisplayName}", "INFO");
        }

        private void ShowAddOrderPage()
        {
            _orderContentPanel.Controls.Clear();
            var title = new Label { Text = "添加订单", Location = new Point(10, 10), Size = new Size(500, 28), Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold) };
            _orderContentPanel.Controls.Add(title);

            _txtOrderCustomer = AddOrderPageComboBox("客户", 10, 50, _orders.Orders.Select(order => order.Customer));
            _txtOrderModel = AddOrderPageComboBox("机型", 260, 50, _orders.Orders.Select(order => order.ProductModel));
            _txtOrderColor = AddOrderPageComboBox("颜色", 510, 50, _orders.Orders.Select(order => order.Color));
            _txtOrderNumber = AddOrderPageTextBox("订单号", 760, 50);
            _txtOrderTemplate = AddOrderPageTextBox("模板", 10, 105, 700);
            _txtOrderTemplate.ReadOnly = true;
            _txtOrderTemplate.Text = File.Exists(_selectedTemplatePath) ? _selectedTemplatePath : "";
            var browseTemplate = new Button { Text = "选择模板", Location = new Point(725, 125), Size = new Size(90, 28) };
            browseTemplate.Click += (s, e) => BrowseOrderTemplate();
            var loadFields = new Button { Text = "读取数据源", Location = new Point(825, 125), Size = new Size(95, 28) };
            loadFields.Click += (s, e) => LoadOrderDataSourceRows();
            _orderContentPanel.Controls.Add(browseTemplate);
            _orderContentPanel.Controls.Add(loadFields);
            MiuiTheme.StyleButton(browseTemplate);
            MiuiTheme.StyleButton(loadFields);

            _orderDataSourcesGrid = new DataGridView
            {
                Location = new Point(10, 170),
                Size = new Size(930, 230),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            ConfigureOrderDataSourceGrid();
            _orderContentPanel.Controls.Add(_orderDataSourcesGrid);

            var save = new Button { Text = "保存订单", Location = new Point(825, 415), Size = new Size(95, 30) };
            save.Click += (s, e) => SaveOrderFromPage();
            _orderContentPanel.Controls.Add(save);
            MiuiTheme.StyleButton(save, true);
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

        private ComboBox AddOrderPageComboBox(string labelText, int x, int y, IEnumerable<string> values)
        {
            var label = new Label { Text = labelText + "：", Location = new Point(x, y), Size = new Size(200, 18) };
            var combo = new ComboBox { Location = new Point(x, y + 22), Size = new Size(200, 25), DropDownStyle = ComboBoxStyle.DropDown };
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
                    _txtOrderTemplate.Text = ofd.FileName;
                    LoadOrderDataSourceRows();
                }
        }

        private void ConfigureOrderDataSourceGrid()
        {
            _orderDataSourcesGrid.Columns.Clear();
            _orderDataSourcesGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Enabled", HeaderText = "使用", Width = 50 });
            _orderDataSourcesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Field", HeaderText = "字段名", ReadOnly = true });
            _orderDataSourcesGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "AutoIncrement", HeaderText = "增降序", Width = 70 });
            _orderDataSourcesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "AutoStep", HeaderText = "步长", Width = 60 });
            _orderDataSourcesGrid.Columns.Add(new DataGridViewComboBoxColumn { Name = "LockMode", HeaderText = "锁定方式", DataSource = new[] { "不锁定", "固定锁定", "输入后锁定" } });
            _orderDataSourcesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "LockedValue", HeaderText = "锁定值" });
            _orderDataSourcesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ExpectedLength", HeaderText = "长度", Width = 60 });
        }

        private void LoadOrderDataSourceRows()
        {
            if (string.IsNullOrWhiteSpace(_txtOrderTemplate.Text) || !File.Exists(_txtOrderTemplate.Text))
            { MessageBox.Show(this, "请先选择有效模板文件。", "添加订单", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            var fields = _btService.IsConnected ? _btService.GetTemplateDataSources(_txtOrderTemplate.Text) : new List<string>();
            if (fields.Count == 0)
            {
                var manual = PromptForManualDataSources();
                if (manual == null || manual.Count == 0) return;
                fields = manual;
            }
            _orderDataSourcesGrid.Rows.Clear();
            foreach (var field in fields.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(field => field, NaturalStringComparer.Instance))
                _orderDataSourcesGrid.Rows.Add(true, field, false, 1, "不锁定", "", _lengthValidationEnabled ? _globalExpectedLength : 0);
        }

        private void SaveOrderFromPage()
        {
            var input = new OrderInput
            {
                Customer = _txtOrderCustomer.Text.Trim(),
                ProductModel = _txtOrderModel.Text.Trim(),
                Color = _txtOrderColor.Text.Trim(),
                OrderNumber = _txtOrderNumber.Text.Trim(),
                TemplatePath = _txtOrderTemplate.Text.Trim()
            };
            if (new[] { input.Customer, input.ProductModel, input.Color, input.OrderNumber, input.TemplatePath }.Any(string.IsNullOrWhiteSpace))
            { MessageBox.Show(this, "客户、机型、颜色、订单号和模板都不能为空。", "添加订单", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (!File.Exists(input.TemplatePath))
            { MessageBox.Show(this, "模板文件不存在。", "添加订单", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (_orders.Contains(input.Customer, input.ProductModel, input.Color, input.OrderNumber))
            { MessageBox.Show(this, "订单号已存在。", "添加订单", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            var dataSources = BuildDataSourcesFromOrderGrid();
            if (dataSources.Count == 0)
            { MessageBox.Show(this, "请至少选择一个数据源。", "添加订单", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            var archivedTemplate = _orders.CopyTemplateForOrder(input.TemplatePath, input.Customer, input.ProductModel, input.Color, input.OrderNumber);
            var order = new PackagingOrder
            {
                Customer = input.Customer,
                ProductModel = input.ProductModel,
                Color = input.Color,
                OrderNumber = input.OrderNumber,
                TemplatePath = archivedTemplate,
                Settings = BuildTemplateSettings(archivedTemplate, dataSources)
            };
            _orders.Add(order);
            RefreshOrderFilters();
            SelectOrder(order);
            ApplySelectedOrder();
            AddLog($"已添加订单: {order.DisplayName}", "SUCCESS");
        }

        private List<DataSourceItem> BuildDataSourcesFromOrderGrid()
        {
            var result = new List<DataSourceItem>();
            foreach (DataGridViewRow row in _orderDataSourcesGrid.Rows)
            {
                if (row.IsNewRow || Convert.ToBoolean(row.Cells["Enabled"].Value) != true) continue;
                var field = row.Cells["Field"].Value?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(field)) continue;
                int.TryParse(row.Cells["AutoStep"].Value?.ToString(), out var step);
                int.TryParse(row.Cells["ExpectedLength"].Value?.ToString(), out var expectedLength);
                var autoIncrement = Convert.ToBoolean(row.Cells["AutoIncrement"].Value);
                var lockMode = row.Cells["LockMode"].Value?.ToString() ?? "不锁定";
                result.Add(new DataSourceItem
                {
                    Name = field,
                    Field = field,
                    Enabled = true,
                    AutoIncrement = autoIncrement,
                    AutoStep = step == 0 ? 1 : Math.Max(-99, Math.Min(99, step)),
                    IsLocked = lockMode == "固定锁定",
                    LockAfterInput = lockMode == "输入后锁定",
                    LockedValue = row.Cells["LockedValue"].Value?.ToString() ?? "",
                    ExpectedLength = Math.Max(0, Math.Min(512, expectedLength)),
                    LengthEdited = expectedLength > 0
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
            Task.Run(() =>
            {
                _btService.RunDiagnostics(_selectedTemplatePath);
                BeginInvoke((Action)(() =>
                {
                    AddLog("诊断完成，请查看日志文件获取详细信息", "INFO");
                    AddLog($"日志文件: {LoggerService.GetLogFile()}", "INFO");
                }));
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
            cmbPrinter.Items.Clear();
            foreach (var p in printers)
                cmbPrinter.Items.Add(p);
            var saved = IniReadValue("General", "Printer", _configFile);
            if (!string.IsNullOrEmpty(saved) && cmbPrinter.Items.Contains(saved))
                cmbPrinter.SelectedItem = saved;
            else if (cmbPrinter.Items.Count > 0)
                cmbPrinter.SelectedIndex = 0;
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
            if (!_isInitializing && !string.IsNullOrEmpty(_selectedTemplatePath)) SaveCurrentTemplateSettings();
            _selectedTemplatePath = item.FullPath;
            lblSelectedTemplate.Text = item.Name;

            if (_isInitializing || _isLoadingConfig) return;

            var restored = RestoreTemplateSettings(item.Name, item.FullPath);
            if (!restored) ResetTemplateState();
            LoadHistory();
            RefreshStats();
            if (!restored) LoadTemplateDataSources(_selectedTemplatePath);
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
                BeginInvoke((Action)(() =>
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
                }));
            });
        }

        private void ResetTemplateState()
        {
            _dataSources = new List<DataSourceItem>();
            _hasSavedDataSourceOrder = false;
            _useLocalDataValidation = false;
            _lengthValidationEnabled = false;
            _globalExpectedLength = 0;
            _globalLengthRevision = 0;
            _lengthRevisionCounter = 0;
            _localDataPath = "";
            _localData.Clear();
            _isLoadingConfig = true;
            try
            {
                chkUseLocalData.Checked = false;
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
            public override string ToString() => Name;
        }

        #endregion

        #region Data Source

        private void btnEditDataSources_Click(object sender, EventArgs e)
        {
            var fields = _dataSources.Select(d => d.Field).ToList();
            if (fields.Count == 0 && !string.IsNullOrEmpty(_selectedTemplatePath) && _btService.IsConnected)
                fields = _btService.GetTemplateDataSources(_selectedTemplatePath);
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
                LengthValidation = _lengthValidationEnabled,
                GlobalExpectedLength = _globalExpectedLength,
                GlobalLengthRevision = _globalLengthRevision,
                LengthRevisionCounter = _lengthRevisionCounter,
                LocalDataPath = _localDataPath,
                LocalData = _localData.ToList(),
                DataSources = dataSources.Select(CloneDataSource).ToList()
            };
        }

        private class OrderInput
        {
            public string Customer;
            public string ProductModel;
            public string Color;
            public string OrderNumber;
            public string TemplatePath;
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
            var editable = enabled.Where(d => !d.IsLocked && !d.AutoIncrementLocked).ToList();
            var acceptedValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in enabled.Where(d => d.IsLocked || d.AutoIncrementLocked))
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
                if (_useLocalDataValidation && configuredValues.Contains(value))
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
                    Cursor = Cursors.Hand,
                    BackColor = Color.Transparent,
                    AccessibleName = IsInputLocked(enabled[i]) ? "解除锁定" : "锁定"
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
            var button = (Button)sender;
            var enabled = _dataSources.Where(d => d.Enabled).ToList();
            var index = (int)button.Tag;
            if (index < 0 || index >= enabled.Count || index >= _inputTextBoxes.Length) return;

            var source = enabled[index];
            if (IsInputLocked(source))
            {
                source.IsLocked = false;
                source.AutoIncrementLocked = false;
                source.LockAfterInput = false;
                source.LockedValue = "";
            }
            else
            {
                var value = _inputTextBoxes[index].Text.Trim();
                if (string.IsNullOrEmpty(value))
                { MessageBox.Show(this, $"请先输入 {source.Name} 后再锁定。", "锁定数据源", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                source.LockedValue = value;
                source.LockAfterInput = false;
                if (source.AutoIncrement)
                {
                    source.IsLocked = false;
                    source.AutoIncrementLocked = true;
                }
                else
                {
                    source.IsLocked = true;
                    source.AutoIncrementLocked = false;
                }
            }
            var readOnly = IsInputLocked(source);
            _inputTextBoxes[index].ReadOnly = readOnly;
            _inputTextBoxes[index].BackColor = readOnly ? SystemColors.Control : MiuiTheme.InputBackground;
            button.AccessibleName = readOnly ? "解除锁定" : "锁定";
            button.Invalidate();
            SaveConfig();
            SaveCurrentTemplateSettings();
            AddLog($"数据源 {source.Name} 已{(readOnly ? "锁定" : "解除锁定")}", "INFO");
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
                if (button != null) button.Enabled = !ro;
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
            using (var ofd = new OpenFileDialog { Filter = "CSV|*.csv|Excel|*.xlsx;*.xls|文本|*.txt|所有|*.*" })
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
            var lines = File.ReadAllLines(path);
            if (lines.Length < 2) { MessageBox.Show(this, "CSV 文件为空"); return; }
            var headers = ParseCsvLine(lines[0]);
            int colIdx = 0;
            if (headers.Count > 1)
            {
                colIdx = PromptForColumnSelection(headers, Path.GetFileName(path));
                if (colIdx < 0) return;
            }
            _localData.Clear();
            foreach (var line in lines.Skip(1))
            {
                var cols = ParseCsvLine(line);
                if (colIdx < cols.Count && !string.IsNullOrWhiteSpace(cols[colIdx]))
                    _localData.Add(cols[colIdx].Trim());
            }
            _localDataPath = path; _useLocalDataValidation = true; chkUseLocalData.Checked = true;
            UpdateLocalDataLabel($"已加载: {_localData.Count} 条 [{headers[colIdx]}] ({Path.GetFileName(path)})");
            AddLog($"加载 CSV: {_localData.Count} 条, 列: {headers[colIdx]}", "SUCCESS");
            SaveCurrentConfigurationState();
        }

        private void LoadExcelData(string path)
        {
            SetStatus("正在加载 Excel...");
            AddLog("正在加载 Excel 数据...", "INFO");

            Task.Run(() =>
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
                    try
                    {
                        excel = Activator.CreateInstance(excelType);
                        excel.Visible = false;
                        excel.DisplayAlerts = false;
                        wb = excel.Workbooks.Open(path, ReadOnly: true);
                        dynamic ws = wb.ActiveSheet;
                        dynamic usedRange = ws.UsedRange;
                        int rows = usedRange.Rows.Count;
                        int cols = usedRange.Columns.Count;

                        if (rows < 2 || cols < 1)
                        {
                            BeginInvoke((Action)(() => MessageBox.Show(this, "Excel 文件为空")));
                            wb.Close(false); excel.Quit();
                            return;
                        }

                        dynamic allData = usedRange.Value2;
                        var headers = new List<string>();
                        for (int c = 1; c <= cols; c++)
                            headers.Add(allData[1, c]?.ToString()?.Trim() ?? $"列{c}");

                        int colIdx = 0;
                        if (headers.Count > 1)
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
                        }

                        var data = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        for (int r = 2; r <= rows; r++)
                        {
                            var val = allData[r, colIdx + 1]?.ToString()?.Trim();
                            if (!string.IsNullOrEmpty(val)) data.Add(val);
                        }

                        wb.Close(false); excel.Quit();

                        BeginInvoke((Action)(() =>
                        {
                            _localData = data;
                            _localDataPath = path;
                            _useLocalDataValidation = true;
                            chkUseLocalData.Checked = true;
                            UpdateLocalDataLabel($"已加载: {data.Count} 条 [{headers[colIdx]}] ({Path.GetFileName(path)})");
                            AddLog($"加载 Excel: {data.Count} 条, 列: {headers[colIdx]}", "SUCCESS");
                            SetStatus("就绪");
                            SaveCurrentConfigurationState();
                        }));
                    }
                    finally
                    {
                        try { wb?.Close(false); } catch { }
                        try { excel?.Quit(); } catch { }
                        try { if (excel != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(excel); } catch { }
                    }
                }
                catch (Exception ex)
                {
                    BeginInvoke((Action)(() => { MessageBox.Show(this, $"读取 Excel 失败: {ex.Message}"); SetStatus("就绪"); }));
                }
            });
        }

        private void LoadTextData(string path)
        {
            _localData.Clear();
            foreach (var line in File.ReadAllLines(path))
            { var val = line.Trim(); if (!string.IsNullOrEmpty(val)) _localData.Add(val); }
            _localDataPath = path; _useLocalDataValidation = true; chkUseLocalData.Checked = true;
            UpdateLocalDataLabel($"已加载: {_localData.Count} 条 ({Path.GetFileName(path)})");
            AddLog($"加载本地数据: {_localData.Count} 条", "SUCCESS");
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
            _useLocalDataValidation = chkUseLocalData.Checked;
            if (!_isInitializing) SaveCurrentConfigurationState();
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
            if (!_useLocalDataValidation) return null;
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
                var msg = $"以下数据不在本地数据文件中：\n{string.Join("\n", notInLocal)}\n\n是否继续打印？";
                return MessageBox.Show(this, msg, "数据校验", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
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
                BeginInvoke((Action)(() =>
                {
                    if (result.Success)
                    {
                        SetStatus("打印完成");
                        AddLog("打印完成", "SUCCESS");
                        _history.Add(templateName, templatePath, fieldValues, "PASS", printer, copies);

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
                        _history.Add(templateName, templatePath, fieldValues, "FAIL", printer, copies);
                        RestoreInputReadOnlyStates(readOnlyStates);
                    }
                    SetPrintEnvironmentEnabled(true);
                    LoadHistory(); RefreshStats();
                }));
            });
        }

        private bool ValidateInputValues(List<DataSourceItem> enabled, Dictionary<string, string> fieldValues)
        {
            if (_lengthValidationEnabled)
            {
                for (int i = 0; i < enabled.Count; i++)
                {
                    var value = fieldValues[enabled[i].Field];
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

            if (!_useLocalDataValidation) return true;
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < enabled.Count; i++)
            {
                var value = fieldValues[enabled[i].Field];
                var isEditable = !enabled[i].IsLocked && !enabled[i].AutoIncrementLocked;
                if (seen.ContainsKey(value) || (isEditable && _history.ContainsAnyValue(Path.GetFileName(_selectedTemplatePath), _selectedTemplatePath, value)))
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
            { _history.Clear(Path.GetFileName(_selectedTemplatePath), _selectedTemplatePath); LoadHistory(); RefreshStats(); }
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
            SetPrintEnvironmentEnabled(false);
            SetStatus("补打印中...");
            var values = new Dictionary<string, string>(record.FieldValues, StringComparer.OrdinalIgnoreCase);
            Task.Run(() =>
            {
                PrintResult result;
                try { result = _btService.Print(record.TemplatePath, values, printer, record.Copies); }
                catch (Exception ex) { result = new PrintResult(false, ex.Message); }
                BeginInvoke((Action)(() =>
                {
                    _history.Add(record.TemplateName, record.TemplatePath, values,
                        result.Success ? "REPRINT_PASS" : "REPRINT_FAIL", printer, record.Copies);
                    RestoreAutoIncrementInputsToPendingValues();
                    AddLog(result.Success ? "历史记录补打印完成" : $"历史记录补打印失败: {result.ErrorMessage}", result.Success ? "SUCCESS" : "ERROR");
                    SetPrintEnvironmentEnabled(true);
                    LoadHistory();
                    RefreshStats();
                }));
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

            if (_history.Delete(recordId))
            {
                AddLog("已删除单条历史记录", "INFO");
                LoadHistory();
                RefreshStats();
            }
            else
            {
                MessageBox.Show(this, "该历史记录已不存在，请刷新后重试。", "删除历史记录", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                _templateSettings.Save(new TemplateSettings
                {
                    TemplateName = Path.GetFileName(_selectedTemplatePath),
                    TemplatePath = _selectedTemplatePath,
                    Printer = cmbPrinter.SelectedItem?.ToString() ?? "",
                    Copies = (int)numCopies.Value,
                    InputValidation = _useLocalDataValidation,
                    LengthValidation = _lengthValidationEnabled,
                    GlobalExpectedLength = _globalExpectedLength,
                    GlobalLengthRevision = _globalLengthRevision,
                    LengthRevisionCounter = _lengthRevisionCounter,
                    LocalDataPath = _localDataPath,
                    LocalData = _localData.ToList(),
                    DataSources = _dataSources.Select(CloneDataSource).ToList()
                });
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
                _lengthValidationEnabled = settings.LengthValidation;
                _globalExpectedLength = settings.GlobalExpectedLength;
                _globalLengthRevision = settings.GlobalLengthRevision;
                _lengthRevisionCounter = settings.LengthRevisionCounter;
                _localDataPath = settings.LocalDataPath ?? "";
                _localData = new HashSet<string>(settings.LocalData ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                chkUseLocalData.Checked = _useLocalDataValidation;
                chkLengthValidation.Checked = _lengthValidationEnabled;
                btnGlobalLength.Enabled = _lengthValidationEnabled;
                numCopies.Value = Math.Max(1, Math.Min(99, settings.Copies));
                if (!string.IsNullOrEmpty(settings.Printer) && cmbPrinter.Items.Contains(settings.Printer))
                    cmbPrinter.SelectedItem = settings.Printer;
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
                LengthRevision = source.LengthRevision
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
                var copies = 1; int.TryParse(IniReadValue("General", "Copies", path), out copies); numCopies.Value = Math.Max(1, Math.Min(99, copies));
                bool.TryParse(IniReadValue("General", "InputValidation", path), out _useLocalDataValidation);
                chkUseLocalData.Checked = _useLocalDataValidation;
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
                            IsLocked = r.CmbLockMode.SelectedIndex == 1 || (r.CmbLockMode.SelectedIndex == 2 && r.WasInputLocked),
                            LockAfterInput = r.CmbLockMode.SelectedIndex == 2,
                            LockedValue = r.CmbLockMode.SelectedIndex == 1 || (r.CmbLockMode.SelectedIndex == 2 && r.WasInputLocked) || (r.CbAutoInc.Checked && r.AutoIncrementLocked)
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
            row.TxtLockedValue.Enabled = row.CmbLockMode.SelectedIndex == 1;
            row.CmbLockMode.SelectedIndexChanged += (s, e) =>
            {
                row.TxtLockedValue.Enabled = row.CmbLockMode.SelectedIndex == 1;
                if (row.CmbLockMode.SelectedIndex == 0)
                {
                    row.WasInputLocked = false;
                    row.TxtLockedValue.Text = "";
                }
                else if (row.CmbLockMode.SelectedIndex == 1)
                {
                    row.WasInputLocked = false;
                }
                else if (!lockAfterInput)
                {
                    row.WasInputLocked = false;
                    row.TxtLockedValue.Text = "";
                }
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
