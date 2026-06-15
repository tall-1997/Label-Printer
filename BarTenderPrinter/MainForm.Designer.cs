namespace BarTenderPrinter
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            // Title
            this.titlePanel = new System.Windows.Forms.Panel();
            this.titleLabel = new System.Windows.Forms.Label();
            this.btnExportLog = new System.Windows.Forms.Button();
            this.btnSettings = new System.Windows.Forms.Button();
            this.titlePanel.SuspendLayout();
            this.titlePanel.Dock = System.Windows.Forms.DockStyle.Top;
            this.titlePanel.Height = 40;
            this.titlePanel.Padding = new System.Windows.Forms.Padding(10, 6, 10, 6);
            this.titleLabel.Text = "BarTender 标签打印工具 v4.0.0";
            this.titleLabel.Font = new System.Drawing.Font("Microsoft YaHei UI", 12F, System.Drawing.FontStyle.Bold);
            this.titleLabel.AutoSize = true;
            this.titleLabel.Location = new System.Drawing.Point(10, 8);
            this.btnExportLog.Text = "导出日志";
            this.btnExportLog.Dock = System.Windows.Forms.DockStyle.Right;
            this.btnExportLog.Width = 75;
            this.btnExportLog.Click += new System.EventHandler(this.btnExportLog_Click);
            this.btnSettings.Text = "设置";
            this.btnSettings.Dock = System.Windows.Forms.DockStyle.Right;
            this.btnSettings.Width = 55;
            this.btnSettings.Click += new System.EventHandler(this.btnSettings_Click);
            this.titlePanel.Controls.Add(this.titleLabel);
            this.titlePanel.Controls.Add(this.btnExportLog);
            this.titlePanel.Controls.Add(this.btnSettings);
            this.titlePanel.ResumeLayout(false);
            this.titlePanel.PerformLayout();
            MiuiTheme.StyleButton(this.btnExportLog);
            MiuiTheme.StyleButton(this.btnSettings);

            // Config Row
            this.btnSaveConfig = new System.Windows.Forms.Button();
            this.btnSaveConfig.Text = "保存配置";
            this.btnSaveConfig.Location = new System.Drawing.Point(10, 45);
            this.btnSaveConfig.Size = new System.Drawing.Size(75, 25);
            this.btnSaveConfig.Click += new System.EventHandler(this.btnSaveConfig_Click);
            this.btnLoadConfig = new System.Windows.Forms.Button();
            this.btnLoadConfig.Text = "加载配置";
            this.btnLoadConfig.Location = new System.Drawing.Point(92, 45);
            this.btnLoadConfig.Size = new System.Drawing.Size(75, 25);
            this.btnLoadConfig.Click += new System.EventHandler(this.btnLoadConfig_Click);
            this.btnEditDataSources = new System.Windows.Forms.Button();
            this.btnEditDataSources.Text = "编辑数据源";
            this.btnEditDataSources.Location = new System.Drawing.Point(175, 45);
            this.btnEditDataSources.Size = new System.Drawing.Size(85, 25);
            this.btnEditDataSources.Click += new System.EventHandler(this.btnEditDataSources_Click);
            MiuiTheme.StyleButton(this.btnSaveConfig);
            MiuiTheme.StyleButton(this.btnLoadConfig);
            MiuiTheme.StyleButton(this.btnEditDataSources);

            // Template Row
            this.lblTemplateDir = new System.Windows.Forms.Label();
            this.lblTemplateDir.Text = "模板目录：";
            this.lblTemplateDir.Location = new System.Drawing.Point(10, 80);
            this.lblTemplateDir.Size = new System.Drawing.Size(65, 18);
            this.txtTemplateDir = new System.Windows.Forms.TextBox();
            this.txtTemplateDir.Location = new System.Drawing.Point(78, 77);
            this.txtTemplateDir.Size = new System.Drawing.Size(380, 25);
            this.txtTemplateDir.ReadOnly = true;
            this.txtTemplateDir.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            this.btnBrowseDir = new System.Windows.Forms.Button();
            this.btnBrowseDir.Text = "浏览";
            this.btnBrowseDir.Location = new System.Drawing.Point(465, 76);
            this.btnBrowseDir.Size = new System.Drawing.Size(55, 25);
            this.btnBrowseDir.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            this.btnBrowseDir.Click += new System.EventHandler(this.btnBrowseDir_Click);
            MiuiTheme.StyleTextBox(this.txtTemplateDir);
            MiuiTheme.StyleButton(this.btnBrowseDir);
            MiuiTheme.StyleLabel(this.lblTemplateDir);

            // Template Combo + Print Button
            this.cmbTemplate = new System.Windows.Forms.ComboBox();
            this.cmbTemplate.Location = new System.Drawing.Point(10, 108);
            this.cmbTemplate.Size = new System.Drawing.Size(390, 25);
            this.cmbTemplate.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbTemplate.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            this.cmbTemplate.SelectedIndexChanged += new System.EventHandler(this.cmbTemplate_SelectedIndexChanged);
            this.btnPrint = new System.Windows.Forms.Button();
            this.btnPrint.Text = "打印所选模板";
            this.btnPrint.Location = new System.Drawing.Point(410, 107);
            this.btnPrint.Size = new System.Drawing.Size(110, 28);
            this.btnPrint.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Bold);
            this.btnPrint.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            this.btnPrint.Click += new System.EventHandler(this.btnPrint_Click);
            MiuiTheme.StyleButton(this.btnPrint, true);

            // Template Name + Preview
            this.lblSelectedTemplate = new System.Windows.Forms.Label();
            this.lblSelectedTemplate.Text = "未选择模板文件";
            this.lblSelectedTemplate.Location = new System.Drawing.Point(10, 142);
            this.lblSelectedTemplate.Size = new System.Drawing.Size(300, 18);
            MiuiTheme.StyleLabel(this.lblSelectedTemplate, true);
            this.pictureBoxPreview = new System.Windows.Forms.PictureBox();
            this.pictureBoxPreview.Location = new System.Drawing.Point(340, 140);
            this.pictureBoxPreview.Size = new System.Drawing.Size(180, 55);
            this.pictureBoxPreview.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.pictureBoxPreview.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.pictureBoxPreview.BackColor = System.Drawing.Color.White;
            this.pictureBoxPreview.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;

            // Input Panel
            this.inputPanel = new System.Windows.Forms.Panel();
            this.inputPanel.Location = new System.Drawing.Point(10, 200);
            this.inputPanel.Size = new System.Drawing.Size(510, 120);
            this.inputPanel.AutoScroll = true;
            this.inputPanel.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.inputPanel.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            MiuiTheme.StyleCard(this.inputPanel);

            // Tab Control: History + Stats
            this.tabBottom = new System.Windows.Forms.TabControl();
            this.tabHistory = new System.Windows.Forms.TabPage();
            this.tabStats = new System.Windows.Forms.TabPage();
            this.tabBottom.Location = new System.Drawing.Point(10, 340);
            this.tabBottom.Size = new System.Drawing.Size(510, 120);
            this.tabBottom.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;

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
            this.historySearchPanel.Height = 30;
            this.lblSearch = new System.Windows.Forms.Label { Text = "搜索：", Location = new System.Drawing.Point(2, 6), Size = new System.Drawing.Size(45, 18) };
            this.txtSearch = new System.Windows.Forms.TextBox { Location = new System.Drawing.Point(48, 3), Size = new System.Drawing.Size(200, 25) };
            this.txtSearch.TextChanged += new System.EventHandler(this.txtSearch_TextChanged);
            this.btnClearSearch = new System.Windows.Forms.Button { Text = "清空", Location = new System.Drawing.Point(253, 2), Size = new System.Drawing.Size(50, 25) };
            this.btnClearSearch.Click += new System.EventHandler(this.btnClearSearch_Click);
            this.btnClearHistory = new System.Windows.Forms.Button { Text = "清空记录", Location = new System.Drawing.Point(310, 2), Size = new System.Drawing.Size(65, 25) };
            this.btnClearHistory.Click += new System.EventHandler(this.btnClearHistory_Click);
            this.btnExportHistory = new System.Windows.Forms.Button { Text = "导出", Location = new System.Drawing.Point(380, 2), Size = new System.Drawing.Size(50, 25) };
            this.btnExportHistory.Click += new System.EventHandler(this.btnExportHistory_Click);
            this.historySearchPanel.Controls.AddRange(new System.Windows.Forms.Control[] { this.lblSearch, this.txtSearch, this.btnClearSearch, this.btnClearHistory, this.btnExportHistory });
            this.tabHistory.Controls.Add(this.dgvHistory);
            this.tabHistory.Controls.Add(this.historySearchPanel);
            this.tabHistory.Text = "历史记录";

            // Stats Tab
            this.lblTodayTitle = new System.Windows.Forms.Label { Text = "今日打印", Location = new System.Drawing.Point(30, 10), Size = new System.Drawing.Size(80, 20) };
            this.lblTodayCount = new System.Windows.Forms.Label { Text = "0", Location = new System.Drawing.Point(30, 30), Size = new System.Drawing.Size(100, 40), Font = new System.Drawing.Font("Microsoft YaHei UI", 22F, System.Drawing.FontStyle.Bold) };
            this.lblTotalTitle = new System.Windows.Forms.Label { Text = "总打印", Location = new System.Drawing.Point(200, 10), Size = new System.Drawing.Size(80, 20) };
            this.lblTotalCount = new System.Windows.Forms.Label { Text = "0", Location = new System.Drawing.Point(200, 30), Size = new System.Drawing.Size(100, 40), Font = new System.Drawing.Font("Microsoft YaHei UI", 22F, System.Drawing.FontStyle.Bold) };
            this.tabStats.Controls.AddRange(new System.Windows.Forms.Control[] { this.lblTodayTitle, this.lblTodayCount, this.lblTotalTitle, this.lblTotalCount });
            this.tabStats.Text = "统计";
            this.tabBottom.TabPages.Add(this.tabHistory);
            this.tabBottom.TabPages.Add(this.tabStats);

            // Log Group
            this.groupBoxLog = new System.Windows.Forms.GroupBox();
            this.groupBoxLog.Text = "日志";
            this.groupBoxLog.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.groupBoxLog.Height = 100;
            this.groupBoxLog.Padding = new System.Windows.Forms.Padding(8);
            this.txtLog = new System.Windows.Forms.TextBox();
            this.txtLog.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtLog.Multiline = true;
            this.txtLog.ReadOnly = true;
            this.txtLog.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.btnClearLog = new System.Windows.Forms.Button();
            this.btnClearLog.Text = "清空日志";
            this.btnClearLog.Dock = System.Windows.Forms.DockStyle.Right;
            this.btnClearLog.Width = 70;
            this.btnClearLog.Click += new System.EventHandler(this.btnClearLog_Click);
            this.groupBoxLog.Controls.Add(this.txtLog);
            this.groupBoxLog.Controls.Add(this.btnClearLog);
            MiuiTheme.StyleGroupBox(this.groupBoxLog);

            // Status Strip
            this.statusStrip = new System.Windows.Forms.StatusStrip();
            this.lblStatusStrip = new System.Windows.Forms.ToolStripStatusLabel();
            this.lblStatusStrip.Text = "就绪";
            this.statusStrip.Items.Add(this.lblStatusStrip);
            MiuiTheme.StyleStatusStrip(this.statusStrip);

            // Main Form
            this.Controls.Add(this.tabBottom);
            this.Controls.Add(this.inputPanel);
            this.Controls.Add(this.pictureBoxPreview);
            this.Controls.Add(this.lblSelectedTemplate);
            this.Controls.Add(this.btnPrint);
            this.Controls.Add(this.cmbTemplate);
            this.Controls.Add(this.btnBrowseDir);
            this.Controls.Add(this.txtTemplateDir);
            this.Controls.Add(this.lblTemplateDir);
            this.Controls.Add(this.btnEditDataSources);
            this.Controls.Add(this.btnLoadConfig);
            this.Controls.Add(this.btnSaveConfig);
            this.Controls.Add(this.groupBoxLog);
            this.Controls.Add(this.statusStrip);
            this.Controls.Add(this.titlePanel);
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(540, 570);
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.MinimumSize = new System.Drawing.Size(500, 500);
            this.Text = "BarTender 标签打印工具 v4.0.0";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.MainForm_FormClosing);
            this.Load += new System.EventHandler(this.MainForm_Load);
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        // Title
        private System.Windows.Forms.Panel titlePanel;
        private System.Windows.Forms.Label titleLabel;
        private System.Windows.Forms.Button btnExportLog;
        private System.Windows.Forms.Button btnSettings;

        // Config
        private System.Windows.Forms.Button btnSaveConfig;
        private System.Windows.Forms.Button btnLoadConfig;
        private System.Windows.Forms.Button btnEditDataSources;

        // Template
        private System.Windows.Forms.Label lblTemplateDir;
        private System.Windows.Forms.TextBox txtTemplateDir;
        private System.Windows.Forms.Button btnBrowseDir;
        private System.Windows.Forms.ComboBox cmbTemplate;
        private System.Windows.Forms.Label lblSelectedTemplate;
        private System.Windows.Forms.PictureBox pictureBoxPreview;
        private System.Windows.Forms.Button btnPrint;

        // Input
        private System.Windows.Forms.Panel inputPanel;

        // Tabs
        private System.Windows.Forms.TabControl tabBottom;
        private System.Windows.Forms.TabPage tabHistory;
        private System.Windows.Forms.TabPage tabStats;

        // History
        private System.Windows.Forms.DataGridView dgvHistory;
        private System.Windows.Forms.Panel historySearchPanel;
        private System.Windows.Forms.Label lblSearch;
        private System.Windows.Forms.TextBox txtSearch;
        private System.Windows.Forms.Button btnClearSearch;
        private System.Windows.Forms.Button btnClearHistory;
        private System.Windows.Forms.Button btnExportHistory;

        // Stats
        private System.Windows.Forms.Label lblTodayTitle;
        private System.Windows.Forms.Label lblTodayCount;
        private System.Windows.Forms.Label lblTotalTitle;
        private System.Windows.Forms.Label lblTotalCount;

        // Log
        private System.Windows.Forms.GroupBox groupBoxLog;
        private System.Windows.Forms.TextBox txtLog;
        private System.Windows.Forms.Button btnClearLog;

        // Status
        private System.Windows.Forms.StatusStrip statusStrip;
        private System.Windows.Forms.ToolStripStatusLabel lblStatusStrip;
    }
}
