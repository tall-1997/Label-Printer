namespace BarTenderPrinter
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;
        protected override void Dispose(bool disposing) { if (disposing && (components != null)) components.Dispose(); base.Dispose(disposing); }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            // === Title ===
            this.titlePanel = new System.Windows.Forms.Panel();
            this.titleLabel = new System.Windows.Forms.Label();
            this.btnExportLog = new System.Windows.Forms.Button();
            this.titlePanel.SuspendLayout();
            this.titlePanel.Dock = System.Windows.Forms.DockStyle.Top;
            this.titlePanel.Height = 38;
            this.titlePanel.Padding = new System.Windows.Forms.Padding(10, 5, 10, 5);
            this.titleLabel.Text = "BarTender 标签打印工具 v5.3.0";
            this.titleLabel.Font = new System.Drawing.Font("Microsoft YaHei UI", 12F, System.Drawing.FontStyle.Bold);
            this.titleLabel.AutoSize = true;
            this.titleLabel.Location = new System.Drawing.Point(10, 7);
            this.btnExportLog.Text = "导出日志";
            this.btnExportLog.Dock = System.Windows.Forms.DockStyle.Right;
            this.btnExportLog.Width = 75;
            this.btnExportLog.Click += new System.EventHandler(this.btnExportLog_Click);
            this.titlePanel.Controls.Add(this.titleLabel);
            this.titlePanel.Controls.Add(this.btnExportLog);
            this.titlePanel.ResumeLayout(false);
            this.titlePanel.PerformLayout();
            MiuiTheme.StyleButton(this.btnExportLog);

            // === Config Buttons Row ===
            this.btnSaveConfig = new System.Windows.Forms.Button { Text = "保存配置", Location = new System.Drawing.Point(10, 42), Size = new System.Drawing.Size(70, 24) };
            this.btnSaveConfig.Click += new System.EventHandler(this.btnSaveConfig_Click);
            this.btnLoadConfig = new System.Windows.Forms.Button { Text = "加载配置", Location = new System.Drawing.Point(86, 42), Size = new System.Drawing.Size(70, 24) };
            this.btnLoadConfig.Click += new System.EventHandler(this.btnLoadConfig_Click);
            this.btnEditDataSources = new System.Windows.Forms.Button { Text = "编辑数据源", Location = new System.Drawing.Point(162, 42), Size = new System.Drawing.Size(80, 24) };
            this.btnEditDataSources.Click += new System.EventHandler(this.btnEditDataSources_Click);
            this.btnLoadLocalData = new System.Windows.Forms.Button { Text = "加载校验数据", Location = new System.Drawing.Point(248, 42), Size = new System.Drawing.Size(90, 24) };
            this.btnLoadLocalData.Click += new System.EventHandler(this.btnLoadLocalData_Click);
            this.chkUseLocalData = new System.Windows.Forms.CheckBox { Text = "启用校验", Location = new System.Drawing.Point(345, 44), Size = new System.Drawing.Size(80, 20), Checked = false };
            this.chkUseLocalData.CheckedChanged += new System.EventHandler(this.chkUseLocalData_CheckedChanged);
            this.lblLocalData = new System.Windows.Forms.Label { Text = "", Location = new System.Drawing.Point(430, 45), Size = new System.Drawing.Size(200, 18) };
            this.lblLocalData.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            MiuiTheme.StyleButton(this.btnSaveConfig);
            MiuiTheme.StyleButton(this.btnLoadConfig);
            MiuiTheme.StyleButton(this.btnEditDataSources);
            MiuiTheme.StyleButton(this.btnLoadLocalData);
            MiuiTheme.StyleLabel(this.lblLocalData, true);

            // === Template Row ===
            this.lblTemplateDir = new System.Windows.Forms.Label { Text = "模板目录：", Location = new System.Drawing.Point(10, 75), Size = new System.Drawing.Size(65, 18) };
            this.txtTemplateDir = new System.Windows.Forms.TextBox { Location = new System.Drawing.Point(78, 72), Size = new System.Drawing.Size(320, 25), ReadOnly = true };
            this.txtTemplateDir.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            this.btnBrowseDir = new System.Windows.Forms.Button { Text = "浏览", Location = new System.Drawing.Point(405, 71), Size = new System.Drawing.Size(50, 25) };
            this.btnBrowseDir.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            this.btnBrowseDir.Click += new System.EventHandler(this.btnBrowseDir_Click);
            MiuiTheme.StyleLabel(this.lblTemplateDir); MiuiTheme.StyleTextBox(this.txtTemplateDir); MiuiTheme.StyleButton(this.btnBrowseDir);

            // === Template Combo + Preview ===
            this.cmbTemplate = new System.Windows.Forms.ComboBox { Location = new System.Drawing.Point(10, 102), Size = new System.Drawing.Size(300, 25), DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList };
            this.cmbTemplate.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left;
            this.cmbTemplate.SelectedIndexChanged += new System.EventHandler(this.cmbTemplate_SelectedIndexChanged);
            this.lblSelectedTemplate = new System.Windows.Forms.Label { Text = "未选择模板", Location = new System.Drawing.Point(10, 132), Size = new System.Drawing.Size(250, 18) };
            MiuiTheme.StyleLabel(this.lblSelectedTemplate, true);
            this.pictureBoxPreview = new System.Windows.Forms.PictureBox { Location = new System.Drawing.Point(320, 98), Size = new System.Drawing.Size(145, 55), SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom, BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle, BackColor = System.Drawing.Color.White };
            this.pictureBoxPreview.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;

            // === Printer + Copies Row ===
            this.lblPrinter = new System.Windows.Forms.Label { Text = "打印机：", Location = new System.Drawing.Point(10, 160), Size = new System.Drawing.Size(55, 18) };
            this.cmbPrinter = new System.Windows.Forms.ComboBox { Location = new System.Drawing.Point(68, 157), Size = new System.Drawing.Size(280, 25), DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList };
            this.cmbPrinter.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            this.btnRefreshPrinter = new System.Windows.Forms.Button { Text = "刷新", Location = new System.Drawing.Point(355, 156), Size = new System.Drawing.Size(45, 25) };
            this.btnRefreshPrinter.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            this.btnRefreshPrinter.Click += new System.EventHandler(this.btnRefreshPrinter_Click);
            this.lblCopies = new System.Windows.Forms.Label { Text = "份数：", Location = new System.Drawing.Point(410, 160), Size = new System.Drawing.Size(35, 18) };
            this.lblCopies.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            this.numCopies = new System.Windows.Forms.NumericUpDown { Location = new System.Drawing.Point(448, 157), Size = new System.Drawing.Size(50, 25), Minimum = 1, Maximum = 99, Value = 1 };
            this.numCopies.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            MiuiTheme.StyleLabel(this.lblPrinter); MiuiTheme.StyleLabel(this.lblCopies); MiuiTheme.StyleButton(this.btnRefreshPrinter);

            // === Input Panel ===
            this.inputPanel = new System.Windows.Forms.Panel { Location = new System.Drawing.Point(10, 190), Size = new System.Drawing.Size(490, 40), AutoScroll = true, AutoScrollMinSize = new System.Drawing.Size(0, 40), BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle };
            this.inputPanel.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            MiuiTheme.StyleCard(this.inputPanel);

            // === Print Button ===
            this.btnPrint = new System.Windows.Forms.Button { Text = "打印", Location = new System.Drawing.Point(10, 238), Size = new System.Drawing.Size(490, 32), Font = new System.Drawing.Font("Microsoft YaHei UI", 10F, System.Drawing.FontStyle.Bold) };
            this.btnPrint.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            this.btnPrint.Click += new System.EventHandler(this.btnPrint_Click);
            MiuiTheme.StyleButton(this.btnPrint, true);

            // === Bottom Tabs ===
            this.tabBottom = new System.Windows.Forms.TabControl { Location = new System.Drawing.Point(10, 278), Size = new System.Drawing.Size(490, 160) };
            this.tabBottom.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            this.tabHistory = new System.Windows.Forms.TabPage { Text = "历史记录" };
            this.tabStats = new System.Windows.Forms.TabPage { Text = "统计" };

            // History
            this.dgvHistory = new System.Windows.Forms.DataGridView { Dock = System.Windows.Forms.DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, ReadOnly = true, SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill };
            this.historyPanel = new System.Windows.Forms.Panel { Dock = System.Windows.Forms.DockStyle.Top, Height = 28 };
            this.lblSearch = new System.Windows.Forms.Label { Text = "搜索：", Location = new System.Drawing.Point(2, 5), Size = new System.Drawing.Size(40, 18) };
            this.txtSearch = new System.Windows.Forms.TextBox { Location = new System.Drawing.Point(42, 2), Size = new System.Drawing.Size(180, 25) };
            this.txtSearch.TextChanged += new System.EventHandler(this.txtSearch_TextChanged);
            this.btnClearSearch = new System.Windows.Forms.Button { Text = "清空", Location = new System.Drawing.Point(226, 1), Size = new System.Drawing.Size(45, 24) };
            this.btnClearSearch.Click += new System.EventHandler(this.btnClearSearch_Click);
            this.btnClearHistory = new System.Windows.Forms.Button { Text = "清空记录", Location = new System.Drawing.Point(278, 1), Size = new System.Drawing.Size(60, 24) };
            this.btnClearHistory.Click += new System.EventHandler(this.btnClearHistory_Click);
            this.btnExportHistory = new System.Windows.Forms.Button { Text = "导出", Location = new System.Drawing.Point(344, 1), Size = new System.Drawing.Size(45, 24) };
            this.btnExportHistory.Click += new System.EventHandler(this.btnExportHistory_Click);
            this.historyPanel.Controls.AddRange(new System.Windows.Forms.Control[] { this.lblSearch, this.txtSearch, this.btnClearSearch, this.btnClearHistory, this.btnExportHistory });
            this.tabHistory.Controls.Add(this.dgvHistory); this.tabHistory.Controls.Add(this.historyPanel);

            // Stats
            this.lblTodayTitle = new System.Windows.Forms.Label { Text = "今日打印", Location = new System.Drawing.Point(20, 10), Size = new System.Drawing.Size(70, 18) };
            this.lblTodayCount = new System.Windows.Forms.Label { Text = "0", Location = new System.Drawing.Point(20, 30), Size = new System.Drawing.Size(80, 40), Font = new System.Drawing.Font("Microsoft YaHei UI", 20F, System.Drawing.FontStyle.Bold) };
            this.lblTotalTitle = new System.Windows.Forms.Label { Text = "总打印", Location = new System.Drawing.Point(160, 10), Size = new System.Drawing.Size(60, 18) };
            this.lblTotalCount = new System.Windows.Forms.Label { Text = "0", Location = new System.Drawing.Point(160, 30), Size = new System.Drawing.Size(80, 40), Font = new System.Drawing.Font("Microsoft YaHei UI", 20F, System.Drawing.FontStyle.Bold) };
            this.tabStats.Controls.AddRange(new System.Windows.Forms.Control[] { this.lblTodayTitle, this.lblTodayCount, this.lblTotalTitle, this.lblTotalCount });
            this.tabBottom.TabPages.Add(this.tabHistory); this.tabBottom.TabPages.Add(this.tabStats);

            // === Log ===
            this.groupBoxLog = new System.Windows.Forms.GroupBox { Text = "日志", Dock = System.Windows.Forms.DockStyle.Bottom, Height = 90, Padding = new System.Windows.Forms.Padding(6) };
            this.txtLog = new System.Windows.Forms.TextBox { Dock = System.Windows.Forms.DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = System.Windows.Forms.ScrollBars.Vertical };
            this.btnClearLog = new System.Windows.Forms.Button { Text = "清空", Dock = System.Windows.Forms.DockStyle.Right, Width = 50 };
            this.btnClearLog.Click += new System.EventHandler(this.btnClearLog_Click);
            this.groupBoxLog.Controls.Add(this.txtLog); this.groupBoxLog.Controls.Add(this.btnClearLog);
            MiuiTheme.StyleGroupBox(this.groupBoxLog);

            // === Status Strip ===
            this.statusStrip = new System.Windows.Forms.StatusStrip();
            this.lblStatus = new System.Windows.Forms.ToolStripStatusLabel { Text = "就绪" };
            this.statusStrip.Items.Add(this.lblStatus);
            MiuiTheme.StyleStatusStrip(this.statusStrip);

            // === Main Form ===
            this.Controls.Add(this.tabBottom);
            this.Controls.Add(this.btnPrint);
            this.Controls.Add(this.inputPanel);
            this.Controls.Add(this.numCopies);
            this.Controls.Add(this.lblCopies);
            this.Controls.Add(this.btnRefreshPrinter);
            this.Controls.Add(this.cmbPrinter);
            this.Controls.Add(this.lblPrinter);
            this.Controls.Add(this.pictureBoxPreview);
            this.Controls.Add(this.lblSelectedTemplate);
            this.Controls.Add(this.cmbTemplate);
            this.Controls.Add(this.btnBrowseDir);
            this.Controls.Add(this.txtTemplateDir);
            this.Controls.Add(this.lblTemplateDir);
            this.Controls.Add(this.lblLocalData);
            this.Controls.Add(this.chkUseLocalData);
            this.Controls.Add(this.btnLoadLocalData);
            this.Controls.Add(this.btnEditDataSources);
            this.Controls.Add(this.btnLoadConfig);
            this.Controls.Add(this.btnSaveConfig);
            this.Controls.Add(this.groupBoxLog);
            this.Controls.Add(this.statusStrip);
            this.Controls.Add(this.titlePanel);
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(520, 580);
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.MinimumSize = new System.Drawing.Size(480, 520);
            this.Text = "BarTender 标签打印工具 v5.3.0";
            this.ResumeLayout(false); this.PerformLayout();
        }

        private System.Windows.Forms.Panel titlePanel;
        private System.Windows.Forms.Label titleLabel;
        private System.Windows.Forms.Button btnExportLog;
        private System.Windows.Forms.Button btnSaveConfig, btnLoadConfig, btnEditDataSources, btnLoadLocalData;
        private System.Windows.Forms.CheckBox chkUseLocalData;
        private System.Windows.Forms.Label lblLocalData;
        private System.Windows.Forms.Label lblTemplateDir;
        private System.Windows.Forms.TextBox txtTemplateDir;
        private System.Windows.Forms.Button btnBrowseDir;
        private System.Windows.Forms.ComboBox cmbTemplate;
        private System.Windows.Forms.Label lblSelectedTemplate;
        private System.Windows.Forms.PictureBox pictureBoxPreview;
        private System.Windows.Forms.Label lblPrinter;
        private System.Windows.Forms.ComboBox cmbPrinter;
        private System.Windows.Forms.Button btnRefreshPrinter;
        private System.Windows.Forms.Label lblCopies;
        private System.Windows.Forms.NumericUpDown numCopies;
        private System.Windows.Forms.Panel inputPanel;
        private System.Windows.Forms.Button btnPrint;
        private System.Windows.Forms.TabControl tabBottom;
        private System.Windows.Forms.TabPage tabHistory, tabStats;
        private System.Windows.Forms.DataGridView dgvHistory;
        private System.Windows.Forms.Panel historyPanel;
        private System.Windows.Forms.Label lblSearch;
        private System.Windows.Forms.TextBox txtSearch;
        private System.Windows.Forms.Button btnClearSearch, btnClearHistory, btnExportHistory;
        private System.Windows.Forms.Label lblTodayTitle, lblTodayCount, lblTotalTitle, lblTotalCount;
        private System.Windows.Forms.GroupBox groupBoxLog;
        private System.Windows.Forms.TextBox txtLog;
        private System.Windows.Forms.Button btnClearLog;
        private System.Windows.Forms.StatusStrip statusStrip;
        private System.Windows.Forms.ToolStripStatusLabel lblStatus;
    }
}
