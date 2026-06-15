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

            // === Title Bar ===
            this.titlePanel = new System.Windows.Forms.Panel();
            this.titleLabel = new System.Windows.Forms.Label();
            this.btnExportLog = new System.Windows.Forms.Button();
            this.btnSettings = new System.Windows.Forms.Button();
            this.titlePanel.SuspendLayout();
            this.titlePanel.Dock = System.Windows.Forms.DockStyle.Top;
            this.titlePanel.Height = 42;
            this.titlePanel.Padding = new System.Windows.Forms.Padding(10, 6, 10, 6);
            this.titleLabel.Text = "BarTender 标签打印工具 v3.1.0";
            this.titleLabel.Font = new System.Drawing.Font("Microsoft YaHei", 13F, System.Drawing.FontStyle.Bold);
            this.titleLabel.AutoSize = true;
            this.titleLabel.Location = new System.Drawing.Point(10, 8);
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

            // === Split Container: Left=Template, Right=Input ===
            this.splitContainer = new System.Windows.Forms.SplitContainer();
            this.splitContainer.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer.Orientation = System.Windows.Forms.Orientation.Vertical;
            this.splitContainer.SplitterDistance = 420;
            this.splitContainer.FixedPanel = System.Windows.Forms.FixedPanel.Panel1;

            // === Left: Template Selection + Preview ===
            this.leftPanel = new System.Windows.Forms.Panel();
            this.leftPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.leftPanel.Padding = new System.Windows.Forms.Padding(10);

            this.lblTemplateDir = new System.Windows.Forms.Label();
            this.lblTemplateDir.Text = "模板目录：";
            this.lblTemplateDir.Location = new System.Drawing.Point(0, 5);
            this.lblTemplateDir.Size = new System.Drawing.Size(75, 20);
            this.txtTemplateDir = new System.Windows.Forms.TextBox();
            this.txtTemplateDir.Location = new System.Drawing.Point(75, 2);
            this.txtTemplateDir.Size = new System.Drawing.Size(250, 25);
            this.txtTemplateDir.ReadOnly = true;
            this.btnBrowseDir = new System.Windows.Forms.Button();
            this.btnBrowseDir.Text = "浏览";
            this.btnBrowseDir.Location = new System.Drawing.Point(330, 1);
            this.btnBrowseDir.Size = new System.Drawing.Size(60, 26);
            this.btnBrowseDir.Click += new System.EventHandler(this.btnBrowseDir_Click);

            this.lblTemplate = new System.Windows.Forms.Label();
            this.lblTemplate.Text = "选择模板：";
            this.lblTemplate.Location = new System.Drawing.Point(0, 35);
            this.lblTemplate.Size = new System.Drawing.Size(75, 20);
            this.cmbTemplate = new System.Windows.Forms.ComboBox();
            this.cmbTemplate.Location = new System.Drawing.Point(75, 32);
            this.cmbTemplate.Size = new System.Drawing.Size(315, 25);
            this.cmbTemplate.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbTemplate.SelectedIndexChanged += new System.EventHandler(this.cmbTemplate_SelectedIndexChanged);

            this.previewBox = new System.Windows.Forms.PictureBox();
            this.previewBox.Location = new System.Drawing.Point(0, 65);
            this.previewBox.Size = new System.Drawing.Size(400, 300);
            this.previewBox.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.previewBox.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.previewBox.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            this.previewBox.BackColor = System.Drawing.Color.White;

            this.leftPanel.Controls.Add(this.lblTemplateDir);
            this.leftPanel.Controls.Add(this.txtTemplateDir);
            this.leftPanel.Controls.Add(this.btnBrowseDir);
            this.leftPanel.Controls.Add(this.lblTemplate);
            this.leftPanel.Controls.Add(this.cmbTemplate);
            this.leftPanel.Controls.Add(this.previewBox);

            // === Right: Data Source Input + Status ===
            this.rightPanel = new System.Windows.Forms.Panel();
            this.rightPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.rightPanel.Padding = new System.Windows.Forms.Padding(10);

            this.inputPanel = new System.Windows.Forms.Panel();
            this.inputPanel.Location = new System.Drawing.Point(0, 0);
            this.inputPanel.Size = new System.Drawing.Size(450, 200);
            this.inputPanel.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            this.inputPanel.AutoScroll = true;

            this.btnPrint = new System.Windows.Forms.Button();
            this.btnPrint.Text = "打印所选模板";
            this.btnPrint.Font = new System.Drawing.Font("Microsoft YaHei", 10F, System.Drawing.FontStyle.Bold);
            this.btnPrint.Size = new System.Drawing.Size(150, 35);
            this.btnPrint.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            this.btnPrint.Click += new System.EventHandler(this.btnPrint_Click);

            this.grpStatus = new System.Windows.Forms.GroupBox();
            this.grpStatus.Text = "打印状态";
            this.grpStatus.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.grpStatus.Height = 250;
            this.grpStatus.Padding = new System.Windows.Forms.Padding(8);
            this.txtStatus = new System.Windows.Forms.TextBox();
            this.txtStatus.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtStatus.Multiline = true;
            this.txtStatus.ReadOnly = true;
            this.txtStatus.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtStatus.WordWrap = true;
            this.grpStatus.Controls.Add(this.txtStatus);

            this.rightPanel.Controls.Add(this.inputPanel);
            this.rightPanel.Controls.Add(this.btnPrint);
            this.rightPanel.Controls.Add(this.grpStatus);

            this.splitContainer.Panel1.Controls.Add(this.leftPanel);
            this.splitContainer.Panel2.Controls.Add(this.rightPanel);

            // === Status Bar ===
            this.statusStrip = new System.Windows.Forms.StatusStrip();
            this.lblStatus = new System.Windows.Forms.ToolStripStatusLabel();
            this.lblStatus.Text = "就绪";
            this.statusStrip.Items.Add(this.lblStatus);

            // === Main Form ===
            this.Controls.Add(this.splitContainer);
            this.Controls.Add(this.statusStrip);
            this.Controls.Add(this.titlePanel);
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(920, 680);
            this.Font = new System.Drawing.Font("Microsoft YaHei", 9F);
            this.MinimumSize = new System.Drawing.Size(850, 600);
            this.Text = "BarTender 标签打印工具 v3.1.0";
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

        // Split
        private System.Windows.Forms.SplitContainer splitContainer;

        // Left: Template
        private System.Windows.Forms.Panel leftPanel;
        private System.Windows.Forms.Label lblTemplateDir;
        private System.Windows.Forms.TextBox txtTemplateDir;
        private System.Windows.Forms.Button btnBrowseDir;
        private System.Windows.Forms.Label lblTemplate;
        private System.Windows.Forms.ComboBox cmbTemplate;
        private System.Windows.Forms.PictureBox previewBox;

        // Right: Input + Status
        private System.Windows.Forms.Panel rightPanel;
        private System.Windows.Forms.Panel inputPanel;
        private System.Windows.Forms.Button btnPrint;
        private System.Windows.Forms.GroupBox grpStatus;
        private System.Windows.Forms.TextBox txtStatus;

        // Status Bar
        private System.Windows.Forms.StatusStrip statusStrip;
        private System.Windows.Forms.ToolStripStatusLabel lblStatus;
    }
}
