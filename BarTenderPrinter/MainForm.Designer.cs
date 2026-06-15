namespace BarTenderPrinter
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.SuspendLayout();

            int pad = 10;
            int labelW = 90;
            int rowH = 30;

            // Title Panel
            this.titlePanel = new System.Windows.Forms.Panel();
            this.titleLabel = new System.Windows.Forms.Label();
            this.btnExportLog = new System.Windows.Forms.Button();
            this.btnSettings = new System.Windows.Forms.Button();
            this.titlePanel.SuspendLayout();
            this.titlePanel.Dock = System.Windows.Forms.DockStyle.Top;
            this.titlePanel.Height = 45;
            this.titlePanel.Padding = new System.Windows.Forms.Padding(pad, 8, pad, 8);
            this.titleLabel.Text = "BarTender 标签打印工具 v3.0.0";
            this.titleLabel.Font = new System.Drawing.Font("Microsoft YaHei", 14F, System.Drawing.FontStyle.Bold);
            this.titleLabel.AutoSize = true;
            this.titleLabel.Location = new System.Drawing.Point(pad, 8);
            this.btnExportLog.Text = "导出日志";
            this.btnExportLog.Dock = System.Windows.Forms.DockStyle.Right;
            this.btnExportLog.Width = 80;
            this.btnExportLog.Click += new System.EventHandler(this.btnExportLog_Click);
            this.btnSettings.Text = "设置";
            this.btnSettings.Dock = System.Windows.Forms.DockStyle.Right;
            this.btnSettings.Width = 60;
            this.btnSettings.Click += new System.EventHandler(this.btnSettings_Click);
            this.titlePanel.Controls.Add(this.titleLabel);
            this.titlePanel.Controls.Add(this.btnExportLog);
            this.titlePanel.Controls.Add(this.btnSettings);
            this.titlePanel.ResumeLayout(false);
            this.titlePanel.PerformLayout();

            // Tab Control
            this.tabMain = new System.Windows.Forms.TabControl();
            this.tabPrint = new System.Windows.Forms.TabPage();
            this.tabHistory = new System.Windows.Forms.TabPage();
            this.tabStats = new System.Windows.Forms.TabPage();
            this.tabMain.Dock = System.Windows.Forms.DockStyle.Fill;

            // Print Tab
            int y = pad;

            // Template Group
            this.grpTemplate = new System.Windows.Forms.GroupBox();
            this.grpTemplate.Text = "BarTender 模板";
            this.grpTemplate.Location = new System.Drawing.Point(pad, y);
            this.grpTemplate.Size = new System.Drawing.Size(880, 75);
            this.grpTemplate.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;

            this.lblTemplate = new System.Windows.Forms.Label();
            this.lblTemplate.Text = "模板文件：";
            this.lblTemplate.Location = new System.Drawing.Point(10, 25);
            this.lblTemplate.Size = new System.Drawing.Size(labelW, 20);
            this.txtTemplate = new System.Windows.Forms.TextBox();
            this.txtTemplate.Location = new System.Drawing.Point(100, 22);
            this.txtTemplate.Size = new System.Drawing.Size(680, 25);
            this.txtTemplate.ReadOnly = true;
            this.txtTemplate.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            this.btnBrowseTemplate = new System.Windows.Forms.Button();
            this.btnBrowseTemplate.Text = "浏览";
            this.btnBrowseTemplate.Location = new System.Drawing.Point(790, 20);
            this.btnBrowseTemplate.Size = new System.Drawing.Size(75, 28);
            this.btnBrowseTemplate.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            this.btnBrowseTemplate.Click += new System.EventHandler(this.btnBrowseTemplate_Click);

            this.lblDatasource = new System.Windows.Forms.Label();
            this.lblDatasource.Text = "数据源名称：";
            this.lblDatasource.Location = new System.Drawing.Point(10, 50);
            this.lblDatasource.Size = new System.Drawing.Size(labelW, 20);
            this.txtDatasource = new System.Windows.Forms.TextBox();
            this.txtDatasource.Location = new System.Drawing.Point(100, 47);
            this.txtDatasource.Size = new System.Drawing.Size(200, 25);

            this.grpTemplate.Controls.Add(this.lblTemplate);
            this.grpTemplate.Controls.Add(this.txtTemplate);
            this.grpTemplate.Controls.Add(this.btnBrowseTemplate);
            this.grpTemplate.Controls.Add(this.lblDatasource);
            this.grpTemplate.Controls.Add(this.txtDatasource);

            y += 85;

            // Excel Group
            this.grpExcel = new System.Windows.Forms.GroupBox();
            this.grpExcel.Text = "IMEI 数据源（Excel）";
            this.grpExcel.Location = new System.Drawing.Point(pad, y);
            this.grpExcel.Size = new System.Drawing.Size(880, 75);
            this.grpExcel.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;

            this.lblExcel = new System.Windows.Forms.Label();
            this.lblExcel.Text = "Excel 文件：";
            this.lblExcel.Location = new System.Drawing.Point(10, 25);
            this.lblExcel.Size = new System.Drawing.Size(labelW, 20);
            this.txtExcel = new System.Windows.Forms.TextBox();
            this.txtExcel.Location = new System.Drawing.Point(100, 22);
            this.txtExcel.Size = new System.Drawing.Size(680, 25);
            this.txtExcel.ReadOnly = true;
            this.txtExcel.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            this.btnBrowseExcel = new System.Windows.Forms.Button();
            this.btnBrowseExcel.Text = "选择文件";
            this.btnBrowseExcel.Location = new System.Drawing.Point(790, 20);
            this.btnBrowseExcel.Size = new System.Drawing.Size(75, 28);
            this.btnBrowseExcel.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            this.btnBrowseExcel.Click += new System.EventHandler(this.btnBrowseExcel_Click);

            this.lblExcelCol = new System.Windows.Forms.Label();
            this.lblExcelCol.Text = "IMEI 列名：";
            this.lblExcelCol.Location = new System.Drawing.Point(10, 50);
            this.lblExcelCol.Size = new System.Drawing.Size(labelW, 20);
            this.txtExcelCol = new System.Windows.Forms.TextBox();
            this.txtExcelCol.Location = new System.Drawing.Point(100, 47);
            this.txtExcelCol.Size = new System.Drawing.Size(150, 25);
            this.lblExcelCount = new System.Windows.Forms.Label();
            this.lblExcelCount.Text = "已加载：0 条";
            this.lblExcelCount.Location = new System.Drawing.Point(270, 50);
            this.lblExcelCount.Size = new System.Drawing.Size(120, 20);

            this.grpExcel.Controls.Add(this.lblExcel);
            this.grpExcel.Controls.Add(this.txtExcel);
            this.grpExcel.Controls.Add(this.btnBrowseExcel);
            this.grpExcel.Controls.Add(this.lblExcelCol);
            this.grpExcel.Controls.Add(this.txtExcelCol);
            this.grpExcel.Controls.Add(this.lblExcelCount);

            y += 85;

            // Printer Group
            this.grpPrinter = new System.Windows.Forms.GroupBox();
            this.grpPrinter.Text = "打印机";
            this.grpPrinter.Location = new System.Drawing.Point(pad, y);
            this.grpPrinter.Size = new System.Drawing.Size(880, 55);
            this.grpPrinter.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;

            this.lblPrinter = new System.Windows.Forms.Label();
            this.lblPrinter.Text = "选择打印机：";
            this.lblPrinter.Location = new System.Drawing.Point(10, 22);
            this.lblPrinter.Size = new System.Drawing.Size(labelW, 20);
            this.cmbPrinter = new System.Windows.Forms.ComboBox();
            this.cmbPrinter.Location = new System.Drawing.Point(100, 19);
            this.cmbPrinter.Size = new System.Drawing.Size(680, 25);
            this.cmbPrinter.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbPrinter.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            this.btnRefreshPrinter = new System.Windows.Forms.Button();
            this.btnRefreshPrinter.Text = "刷新";
            this.btnRefreshPrinter.Location = new System.Drawing.Point(790, 17);
            this.btnRefreshPrinter.Size = new System.Drawing.Size(75, 28);
            this.btnRefreshPrinter.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            this.btnRefreshPrinter.Click += new System.EventHandler(this.btnRefreshPrinter_Click);

            this.grpPrinter.Controls.Add(this.lblPrinter);
            this.grpPrinter.Controls.Add(this.cmbPrinter);
            this.grpPrinter.Controls.Add(this.btnRefreshPrinter);

            y += 65;

            // Options
            this.chkVerifyExcel = new System.Windows.Forms.CheckBox();
            this.chkVerifyExcel.Text = "打印前校验 Excel 数据";
            this.chkVerifyExcel.Location = new System.Drawing.Point(pad + 5, y);
            this.chkVerifyExcel.Size = new System.Drawing.Size(180, 22);
            this.chkVerifyExcel.Checked = true;

            this.lblCopies = new System.Windows.Forms.Label();
            this.lblCopies.Text = "打印份数：";
            this.lblCopies.Location = new System.Drawing.Point(210, y + 2);
            this.lblCopies.Size = new System.Drawing.Size(70, 20);
            this.numCopies = new System.Windows.Forms.NumericUpDown();
            this.numCopies.Location = new System.Drawing.Point(280, y);
            this.numCopies.Size = new System.Drawing.Size(60, 25);
            this.numCopies.Minimum = 1;
            this.numCopies.Maximum = 99;
            this.numCopies.Value = 1;

            y += 35;

            // Buttons
            this.btnPrint = new System.Windows.Forms.Button();
            this.btnPrint.Text = "输入 IMEI 并打印";
            this.btnPrint.Location = new System.Drawing.Point(pad, y);
            this.btnPrint.Size = new System.Drawing.Size(140, 32);
            this.btnPrint.Click += new System.EventHandler(this.btnPrint_Click);

            this.btnImportFile = new System.Windows.Forms.Button();
            this.btnImportFile.Text = "批量导入 IMEI";
            this.btnImportFile.Location = new System.Drawing.Point(160, y);
            this.btnImportFile.Size = new System.Drawing.Size(120, 32);
            this.btnImportFile.Click += new System.EventHandler(this.btnImportFile_Click);

            y += 45;

            // Status Group
            this.grpStatus = new System.Windows.Forms.GroupBox();
            this.grpStatus.Text = "打印状态";
            this.grpStatus.Location = new System.Drawing.Point(pad, y);
            this.grpStatus.Size = new System.Drawing.Size(880, 300);
            this.grpStatus.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;

            this.txtStatus = new System.Windows.Forms.TextBox();
            this.txtStatus.Location = new System.Drawing.Point(10, 20);
            this.txtStatus.Size = new System.Drawing.Size(860, 270);
            this.txtStatus.Multiline = true;
            this.txtStatus.ReadOnly = true;
            this.txtStatus.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtStatus.WordWrap = true;
            this.txtStatus.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;

            this.grpStatus.Controls.Add(this.txtStatus);

            this.tabPrint.Controls.Add(this.grpTemplate);
            this.tabPrint.Controls.Add(this.grpExcel);
            this.tabPrint.Controls.Add(this.grpPrinter);
            this.tabPrint.Controls.Add(this.chkVerifyExcel);
            this.tabPrint.Controls.Add(this.lblCopies);
            this.tabPrint.Controls.Add(this.numCopies);
            this.tabPrint.Controls.Add(this.btnPrint);
            this.tabPrint.Controls.Add(this.btnImportFile);
            this.tabPrint.Controls.Add(this.grpStatus);
            this.tabPrint.Padding = new System.Windows.Forms.Padding(3);
            this.tabPrint.Text = "打印";

            // History Tab
            this.dgvHistory = new System.Windows.Forms.DataGridView();
            this.dgvHistory.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dgvHistory.AllowUserToAddRows = false;
            this.dgvHistory.AllowUserToDeleteRows = false;
            this.dgvHistory.ReadOnly = true;
            this.dgvHistory.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.dgvHistory.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;

            this.historySearchPanel = new System.Windows.Forms.Panel();
            this.historySearchPanel.Dock = System.Windows.Forms.DockStyle.Top;
            this.historySearchPanel.Height = 35;
            this.historySearchPanel.Padding = new System.Windows.Forms.Padding(5);

            this.lblSearch = new System.Windows.Forms.Label();
            this.lblSearch.Text = "搜索：";
            this.lblSearch.Location = new System.Drawing.Point(5, 8);
            this.lblSearch.Size = new System.Drawing.Size(45, 20);
            this.txtSearch = new System.Windows.Forms.TextBox();
            this.txtSearch.Location = new System.Drawing.Point(50, 5);
            this.txtSearch.Size = new System.Drawing.Size(300, 25);
            this.txtSearch.TextChanged += new System.EventHandler(this.txtSearch_TextChanged);
            this.btnClearSearch = new System.Windows.Forms.Button();
            this.btnClearSearch.Text = "清空搜索";
            this.btnClearSearch.Location = new System.Drawing.Point(360, 4);
            this.btnClearSearch.Size = new System.Drawing.Size(75, 26);
            this.btnClearSearch.Click += new System.EventHandler(this.btnClearSearch_Click);

            this.historySearchPanel.Controls.Add(this.lblSearch);
            this.historySearchPanel.Controls.Add(this.txtSearch);
            this.historySearchPanel.Controls.Add(this.btnClearSearch);

            this.historyButtonPanel = new System.Windows.Forms.Panel();
            this.historyButtonPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.historyButtonPanel.Height = 40;
            this.historyButtonPanel.Padding = new System.Windows.Forms.Padding(5);

            this.btnClearHistory = new System.Windows.Forms.Button();
            this.btnClearHistory.Text = "清空记录";
            this.btnClearHistory.Location = new System.Drawing.Point(5, 5);
            this.btnClearHistory.Size = new System.Drawing.Size(80, 28);
            this.btnClearHistory.Click += new System.EventHandler(this.btnClearHistory_Click);
            this.btnExportHistory = new System.Windows.Forms.Button();
            this.btnExportHistory.Text = "导出记录";
            this.btnExportHistory.Location = new System.Drawing.Point(95, 5);
            this.btnExportHistory.Size = new System.Drawing.Size(80, 28);
            this.btnExportHistory.Click += new System.EventHandler(this.btnExportHistory_Click);

            this.historyButtonPanel.Controls.Add(this.btnClearHistory);
            this.historyButtonPanel.Controls.Add(this.btnExportHistory);

            this.tabHistory.Controls.Add(this.dgvHistory);
            this.tabHistory.Controls.Add(this.historySearchPanel);
            this.tabHistory.Controls.Add(this.historyButtonPanel);
            this.tabHistory.Text = "历史记录";

            // Stats Tab
            this.lblTodayTitle = new System.Windows.Forms.Label();
            this.lblTodayTitle.Text = "今日打印";
            this.lblTodayTitle.Font = new System.Drawing.Font("Microsoft YaHei", 10F);
            this.lblTodayTitle.Location = new System.Drawing.Point(50, 30);
            this.lblTodayTitle.Size = new System.Drawing.Size(100, 25);
            this.lblTodayCount = new System.Windows.Forms.Label();
            this.lblTodayCount.Text = "0";
            this.lblTodayCount.Font = new System.Drawing.Font("Microsoft YaHei", 28F, System.Drawing.FontStyle.Bold);
            this.lblTodayCount.Location = new System.Drawing.Point(50, 60);
            this.lblTodayCount.Size = new System.Drawing.Size(150, 50);
            this.lblTodayUnit = new System.Windows.Forms.Label();
            this.lblTodayUnit.Text = "个 IMEI";
            this.lblTodayUnit.Location = new System.Drawing.Point(50, 115);
            this.lblTodayUnit.Size = new System.Drawing.Size(100, 20);

            this.lblTotalTitle = new System.Windows.Forms.Label();
            this.lblTotalTitle.Text = "总打印";
            this.lblTotalTitle.Font = new System.Drawing.Font("Microsoft YaHei", 10F);
            this.lblTotalTitle.Location = new System.Drawing.Point(300, 30);
            this.lblTotalTitle.Size = new System.Drawing.Size(100, 25);
            this.lblTotalCount = new System.Windows.Forms.Label();
            this.lblTotalCount.Text = "0";
            this.lblTotalCount.Font = new System.Drawing.Font("Microsoft YaHei", 28F, System.Drawing.FontStyle.Bold);
            this.lblTotalCount.Location = new System.Drawing.Point(300, 60);
            this.lblTotalCount.Size = new System.Drawing.Size(150, 50);
            this.lblTotalUnit = new System.Windows.Forms.Label();
            this.lblTotalUnit.Text = "个 IMEI";
            this.lblTotalUnit.Location = new System.Drawing.Point(300, 115);
            this.lblTotalUnit.Size = new System.Drawing.Size(100, 20);

            this.tabStats.Controls.Add(this.lblTodayTitle);
            this.tabStats.Controls.Add(this.lblTodayCount);
            this.tabStats.Controls.Add(this.lblTodayUnit);
            this.tabStats.Controls.Add(this.lblTotalTitle);
            this.tabStats.Controls.Add(this.lblTotalCount);
            this.tabStats.Controls.Add(this.lblTotalUnit);
            this.tabStats.Text = "统计";

            this.tabMain.TabPages.Add(this.tabPrint);
            this.tabMain.TabPages.Add(this.tabHistory);
            this.tabMain.TabPages.Add(this.tabStats);

            // Status Bar
            this.statusStrip = new System.Windows.Forms.StatusStrip();
            this.lblStatus = new System.Windows.Forms.ToolStripStatusLabel();
            this.lblStatus.Text = "就绪";
            this.statusStrip.Items.Add(this.lblStatus);

            // Main Form
            this.Controls.Add(this.tabMain);
            this.Controls.Add(this.statusStrip);
            this.Controls.Add(this.titlePanel);
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(920, 680);
            this.Font = new System.Drawing.Font("Microsoft YaHei", 9F);
            this.MinimumSize = new System.Drawing.Size(800, 600);
            this.Text = "BarTender 标签打印工具 v3.0.0";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.MainForm_FormClosing);
            this.Load += new System.EventHandler(this.MainForm_Load);

            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        // Title
        private System.Windows.Forms.Panel titlePanel;
        private System.Windows.Forms.Label titleLabel;
        private System.Windows.Forms.Button btnExportLog;
        private System.Windows.Forms.Button btnSettings;

        // Tabs
        private System.Windows.Forms.TabControl tabMain;
        private System.Windows.Forms.TabPage tabPrint;
        private System.Windows.Forms.TabPage tabHistory;
        private System.Windows.Forms.TabPage tabStats;

        // Print Tab
        private System.Windows.Forms.GroupBox grpTemplate;
        private System.Windows.Forms.Label lblTemplate;
        private System.Windows.Forms.TextBox txtTemplate;
        private System.Windows.Forms.Button btnBrowseTemplate;
        private System.Windows.Forms.Label lblDatasource;
        private System.Windows.Forms.TextBox txtDatasource;

        private System.Windows.Forms.GroupBox grpExcel;
        private System.Windows.Forms.Label lblExcel;
        private System.Windows.Forms.TextBox txtExcel;
        private System.Windows.Forms.Button btnBrowseExcel;
        private System.Windows.Forms.Label lblExcelCol;
        private System.Windows.Forms.TextBox txtExcelCol;
        private System.Windows.Forms.Label lblExcelCount;

        private System.Windows.Forms.GroupBox grpPrinter;
        private System.Windows.Forms.Label lblPrinter;
        private System.Windows.Forms.ComboBox cmbPrinter;
        private System.Windows.Forms.Button btnRefreshPrinter;

        private System.Windows.Forms.CheckBox chkVerifyExcel;
        private System.Windows.Forms.Label lblCopies;
        private System.Windows.Forms.NumericUpDown numCopies;

        private System.Windows.Forms.Button btnPrint;
        private System.Windows.Forms.Button btnImportFile;

        private System.Windows.Forms.GroupBox grpStatus;
        private System.Windows.Forms.TextBox txtStatus;

        // History Tab
        private System.Windows.Forms.DataGridView dgvHistory;
        private System.Windows.Forms.Panel historySearchPanel;
        private System.Windows.Forms.Label lblSearch;
        private System.Windows.Forms.TextBox txtSearch;
        private System.Windows.Forms.Button btnClearSearch;
        private System.Windows.Forms.Panel historyButtonPanel;
        private System.Windows.Forms.Button btnClearHistory;
        private System.Windows.Forms.Button btnExportHistory;

        // Stats Tab
        private System.Windows.Forms.Label lblTodayTitle;
        private System.Windows.Forms.Label lblTodayCount;
        private System.Windows.Forms.Label lblTodayUnit;
        private System.Windows.Forms.Label lblTotalTitle;
        private System.Windows.Forms.Label lblTotalCount;
        private System.Windows.Forms.Label lblTotalUnit;

        // Status Bar
        private System.Windows.Forms.StatusStrip statusStrip;
        private System.Windows.Forms.ToolStripStatusLabel lblStatus;
    }
}
