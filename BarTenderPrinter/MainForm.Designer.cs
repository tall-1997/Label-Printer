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
            this.titlePanel.Padding = new System.Windows.Forms.Padding(8, 5, 8, 5);
            this.titleLabel.Text = "BarTender 标签打印工具 v3.2.0";
            this.titleLabel.Font = new System.Drawing.Font("Microsoft YaHei", 12F, System.Drawing.FontStyle.Bold);
            this.titleLabel.AutoSize = true;
            this.titleLabel.Location = new System.Drawing.Point(8, 8);
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

            // Row 1: Data Source Count + Save/Load Config
            this.lblDsCount = new System.Windows.Forms.Label();
            this.lblDsCount.Text = "数据源数量：";
            this.lblDsCount.Location = new System.Drawing.Point(10, 48);
            this.lblDsCount.Size = new System.Drawing.Size(80, 18);
            this.numDsCount = new System.Windows.Forms.NumericUpDown();
            this.numDsCount.Location = new System.Drawing.Point(92, 45);
            this.numDsCount.Size = new System.Drawing.Size(55, 25);
            this.numDsCount.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            this.numDsCount.ValueChanged += new System.EventHandler(this.numDsCount_ValueChanged);
            this.btnSaveConfig = new System.Windows.Forms.Button();
            this.btnSaveConfig.Text = "保存配置";
            this.btnSaveConfig.Location = new System.Drawing.Point(165, 43);
            this.btnSaveConfig.Size = new System.Drawing.Size(75, 25);
            this.btnSaveConfig.Click += new System.EventHandler(this.btnSaveConfig_Click);
            this.btnLoadConfig = new System.Windows.Forms.Button();
            this.btnLoadConfig.Text = "加载配置";
            this.btnLoadConfig.Location = new System.Drawing.Point(248, 43);
            this.btnLoadConfig.Size = new System.Drawing.Size(75, 25);
            this.btnLoadConfig.Click += new System.EventHandler(this.btnLoadConfig_Click);

            // Row 2: Template Directory
            this.lblTemplateDir = new System.Windows.Forms.Label();
            this.lblTemplateDir.Text = "模板目录：";
            this.lblTemplateDir.Location = new System.Drawing.Point(10, 78);
            this.lblTemplateDir.Size = new System.Drawing.Size(70, 18);
            this.txtTemplateDir = new System.Windows.Forms.TextBox();
            this.txtTemplateDir.Location = new System.Drawing.Point(82, 75);
            this.txtTemplateDir.Size = new System.Drawing.Size(400, 25);
            this.txtTemplateDir.ReadOnly = true;
            this.txtTemplateDir.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            this.btnBrowseDir = new System.Windows.Forms.Button();
            this.btnBrowseDir.Text = "浏览";
            this.btnBrowseDir.Location = new System.Drawing.Point(490, 74);
            this.btnBrowseDir.Size = new System.Drawing.Size(55, 25);
            this.btnBrowseDir.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            this.btnBrowseDir.Click += new System.EventHandler(this.btnBrowseDir_Click);

            // Row 3: Template ComboBox + Print Button
            this.cmbTemplate = new System.Windows.Forms.ComboBox();
            this.cmbTemplate.Location = new System.Drawing.Point(10, 105);
            this.cmbTemplate.Size = new System.Drawing.Size(470, 25);
            this.cmbTemplate.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbTemplate.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            this.cmbTemplate.SelectedIndexChanged += new System.EventHandler(this.cmbTemplate_SelectedIndexChanged);
            this.btnPrint = new System.Windows.Forms.Button();
            this.btnPrint.Text = "打印所选模板";
            this.btnPrint.Location = new System.Drawing.Point(490, 104);
            this.btnPrint.Size = new System.Drawing.Size(95, 25);
            this.btnPrint.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            this.btnPrint.Click += new System.EventHandler(this.btnPrint_Click);

            // Row 4: Template Name + Preview
            this.lblSelectedTemplate = new System.Windows.Forms.Label();
            this.lblSelectedTemplate.Text = "未选择模板文件";
            this.lblSelectedTemplate.Location = new System.Drawing.Point(10, 138);
            this.lblSelectedTemplate.Size = new System.Drawing.Size(300, 18);
            this.pictureBoxPreview = new System.Windows.Forms.PictureBox();
            this.pictureBoxPreview.Location = new System.Drawing.Point(370, 135);
            this.pictureBoxPreview.Size = new System.Drawing.Size(215, 65);
            this.pictureBoxPreview.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.pictureBoxPreview.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.pictureBoxPreview.BackColor = System.Drawing.Color.White;
            this.pictureBoxPreview.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;

            // Row 5: Status
            this.lblStatusText = new System.Windows.Forms.Label();
            this.lblStatusText.Text = "就绪";
            this.lblStatusText.Location = new System.Drawing.Point(10, 205);
            this.lblStatusText.Size = new System.Drawing.Size(300, 18);

            // Row 6: Input Panel
            this.inputPanel = new System.Windows.Forms.Panel();
            this.inputPanel.Location = new System.Drawing.Point(10, 228);
            this.inputPanel.Size = new System.Drawing.Size(575, 150);
            this.inputPanel.AutoScroll = true;
            this.inputPanel.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.inputPanel.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;

            // Row 7: Print Button (repositioned dynamically)
            // btnPrint is already created above, will be repositioned in CreateInputControls

            // Bottom: Log Group
            this.groupBoxLog = new System.Windows.Forms.GroupBox();
            this.groupBoxLog.Text = "日志";
            this.groupBoxLog.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.groupBoxLog.Height = 120;
            this.groupBoxLog.Padding = new System.Windows.Forms.Padding(8);
            this.txtLog = new System.Windows.Forms.TextBox();
            this.txtLog.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtLog.Multiline = true;
            this.txtLog.ReadOnly = true;
            this.txtLog.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.btnClearLog = new System.Windows.Forms.Button();
            this.btnClearLog.Text = "清空日志";
            this.btnClearLog.Dock = System.Windows.Forms.DockStyle.Right;
            this.btnClearLog.Width = 75;
            this.btnClearLog.Click += new System.EventHandler(this.btnClearLog_Click);
            this.groupBoxLog.Controls.Add(this.txtLog);
            this.groupBoxLog.Controls.Add(this.btnClearLog);

            // Status Strip
            this.statusStrip = new System.Windows.Forms.StatusStrip();
            this.lblStatusStrip = new System.Windows.Forms.ToolStripStatusLabel();
            this.lblStatusStrip.Text = "就绪";
            this.statusStrip.Items.Add(this.lblStatusStrip);

            // Main Form
            this.Controls.Add(this.inputPanel);
            this.Controls.Add(this.lblStatusText);
            this.Controls.Add(this.pictureBoxPreview);
            this.Controls.Add(this.lblSelectedTemplate);
            this.Controls.Add(this.btnPrint);
            this.Controls.Add(this.cmbTemplate);
            this.Controls.Add(this.btnBrowseDir);
            this.Controls.Add(this.txtTemplateDir);
            this.Controls.Add(this.lblTemplateDir);
            this.Controls.Add(this.btnLoadConfig);
            this.Controls.Add(this.btnSaveConfig);
            this.Controls.Add(this.numDsCount);
            this.Controls.Add(this.lblDsCount);
            this.Controls.Add(this.groupBoxLog);
            this.Controls.Add(this.statusStrip);
            this.Controls.Add(this.titlePanel);
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(600, 500);
            this.Font = new System.Drawing.Font("Microsoft YaHei", 9F);
            this.MinimumSize = new System.Drawing.Size(550, 450);
            this.Text = "BarTender 标签打印工具 v3.2.0";
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
        private System.Windows.Forms.Label lblDsCount;
        private System.Windows.Forms.NumericUpDown numDsCount;
        private System.Windows.Forms.Button btnSaveConfig;
        private System.Windows.Forms.Button btnLoadConfig;

        // Template
        private System.Windows.Forms.Label lblTemplateDir;
        private System.Windows.Forms.TextBox txtTemplateDir;
        private System.Windows.Forms.Button btnBrowseDir;
        private System.Windows.Forms.ComboBox cmbTemplate;
        private System.Windows.Forms.Label lblSelectedTemplate;
        private System.Windows.Forms.PictureBox pictureBoxPreview;
        private System.Windows.Forms.Button btnPrint;

        // Status & Input
        private System.Windows.Forms.Label lblStatusText;
        private System.Windows.Forms.Panel inputPanel;

        // Log
        private System.Windows.Forms.GroupBox groupBoxLog;
        private System.Windows.Forms.TextBox txtLog;
        private System.Windows.Forms.Button btnClearLog;

        // Status Strip
        private System.Windows.Forms.StatusStrip statusStrip;
        private System.Windows.Forms.ToolStripStatusLabel lblStatusStrip;
    }
}
