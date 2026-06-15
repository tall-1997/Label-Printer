using System;
using System.Collections.Generic;
using System.Drawing;
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
        private readonly string _configFile;
        private readonly string _version = "v3.2.0";

        private Dictionary<int, string> _dsNames = new Dictionary<int, string>();
        private Dictionary<int, string> _dsFields = new Dictionary<int, string>();
        private int _previousCount = 0;
        private bool _suppressCountPrompt = false;
        private string _templatesFolder = "";
        private string _selectedTemplatePath = "";
        private string _previewTempFile = null;

        public MainForm()
        {
            InitializeComponent();
            _configFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".bartender-printer", "config.ini");
            Text = $"BarTender 标签打印工具 {_version}";
            titleLabel.Text = $"BarTender 标签打印工具 {_version}";
        }

        #region Load / Close

        private void MainForm_Load(object sender, EventArgs e)
        {
            if (File.Exists(_configFile))
                LoadConfig(_configFile);
            else
            {
                CreateInputControls((int)numDsCount.Value);
                _templatesFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "templates");
                try { if (!Directory.Exists(_templatesFolder)) Directory.CreateDirectory(_templatesFolder); } catch { }
                PopulateTemplateList(_templatesFolder);
            }
            _previousCount = (int)numDsCount.Value;
            _history.Load();
            RefreshStats();
            InitBarTender();
            AddLog("系统启动完成", "INFO");
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            CleanupPreview();
            _btService.Dispose();
        }

        #endregion

        #region BarTender

        private void InitBarTender()
        {
            if (_btService.Connect())
                SetStatus("BarTender 已连接");
            else
            {
                SetStatus("BarTender 连接失败");
                AddLog("BarTender 连接失败，请检查是否已安装", "ERROR");
            }
        }

        #endregion

        #region Data Source Count

        private void numDsCount_ValueChanged(object sender, EventArgs e)
        {
            if (_suppressCountPrompt) return;
            int newCount = (int)numDsCount.Value;
            if (newCount > _previousCount)
            {
                for (int i = _previousCount + 1; i <= newCount; i++)
                {
                    var cfg = PromptForDataSourceConfig(i);
                    if (cfg == null)
                    {
                        numDsCount.Value = _previousCount;
                        return;
                    }
                    if (string.IsNullOrWhiteSpace(cfg.Name)) cfg.Name = $"数据源 {i}";
                    if (string.IsNullOrWhiteSpace(cfg.Field)) cfg.Field = "DS" + i;
                    _dsNames[i] = cfg.Name;
                    _dsFields[i] = cfg.Field;
                }
                AddLog($"数据源数量从 {_previousCount} 增加到 {newCount}", "INFO");
            }
            else if (newCount < _previousCount)
            {
                for (int i = newCount + 1; i <= _previousCount; i++)
                {
                    if (_dsNames.ContainsKey(i)) _dsNames.Remove(i);
                    if (_dsFields.ContainsKey(i)) _dsFields.Remove(i);
                }
                AddLog($"数据源数量从 {_previousCount} 减少到 {newCount}", "INFO");
            }
            CreateInputControls(newCount);
            _previousCount = newCount;
        }

        private class DsConfigPrompt
        {
            public string Name { get; set; }
            public string Field { get; set; }
        }

        private DsConfigPrompt PromptForDataSourceConfig(int index)
        {
            using (var f = new Form())
            {
                f.Text = "配置数据源";
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterParent;
                f.ClientSize = new Size(420, 160);
                f.MinimizeBox = false;
                f.MaximizeBox = false;

                var lblName = new Label { Left = 10, Top = 10, Width = 390, Text = $"第 {index} 个数据源 显示名称：" };
                var tbName = new TextBox { Left = 10, Top = 30, Width = 390 };
                var lblField = new Label { Left = 10, Top = 60, Width = 390, Text = "映射到模板的字段名（例如 DS1 或 IMEI1）：" };
                var tbField = new TextBox { Left = 10, Top = 80, Width = 390 };
                var btnOk = new Button { Text = "确定", Left = 240, Width = 80, Top = 110, DialogResult = DialogResult.OK };
                var btnCancel = new Button { Text = "取消", Left = 330, Width = 80, Top = 110, DialogResult = DialogResult.Cancel };

                f.Controls.AddRange(new Control[] { lblName, tbName, lblField, tbField, btnOk, btnCancel });
                f.AcceptButton = btnOk;
                f.CancelButton = btnCancel;

                return f.ShowDialog(this) == DialogResult.OK
                    ? new DsConfigPrompt { Name = tbName.Text ?? "", Field = tbField.Text ?? "" }
                    : null;
            }
        }

        #endregion

        #region Dynamic Input Controls

        private void CreateInputControls(int count)
        {
            if (inputPanel == null) return;
            ClearOldControls();
            inputPanel.SuspendLayout();
            int top = 5;
            for (int i = 1; i <= count; i++)
            {
                var name = _dsNames.ContainsKey(i) ? _dsNames[i] : $"数据源 {i}";
                var lbl = new Label
                {
                    Text = name + "：",
                    AutoSize = false,
                    Size = new Size(100, 21),
                    Location = new Point(5, top + 2),
                    TextAlign = ContentAlignment.MiddleRight
                };
                var txt = new TextBox
                {
                    Name = $"txtDS_{i}",
                    Location = new Point(110, top),
                    Size = new Size(Math.Max(150, inputPanel.ClientSize.Width - 120), 25),
                    Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
                };
                txt.KeyDown -= DataSourceTextBox_KeyDown;
                txt.KeyDown += DataSourceTextBox_KeyDown;
                inputPanel.Controls.Add(lbl);
                inputPanel.Controls.Add(txt);
                top += 35;
            }
            inputPanel.ResumeLayout(true);
            btnPrint.Location = new Point(inputPanel.Left, inputPanel.Bottom + 8);
        }

        private void ClearOldControls()
        {
            foreach (Control ctrl in inputPanel.Controls)
            {
                if (ctrl is TextBox txt)
                    txt.KeyDown -= DataSourceTextBox_KeyDown;
            }
            inputPanel.Controls.Clear();
        }

        private void DataSourceTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            var tb = sender as TextBox;
            if (tb == null) return;
            var parts = tb.Name.Split('_');
            if (parts.Length < 2 || !int.TryParse(parts[1], out int idx)) return;
            var nextCtrl = inputPanel.Controls.Find($"txtDS_{idx + 1}", true).FirstOrDefault() as TextBox;
            if (nextCtrl != null)
            {
                nextCtrl.Focus();
                nextCtrl.SelectAll();
            }
            else
            {
                DoPrint();
            }
        }

        private void ClearInputsAndFocusFirst()
        {
            foreach (Control c in inputPanel.Controls)
            {
                if (c is TextBox tb && tb.Name.StartsWith("txtDS_"))
                    tb.Text = "";
            }
            var first = inputPanel.Controls.Find("txtDS_1", true).FirstOrDefault() as TextBox;
            first?.Focus();
        }

        private void SetInputsReadOnly(bool readOnly)
        {
            foreach (Control c in inputPanel.Controls)
            {
                if (c is TextBox tb)
                {
                    tb.ReadOnly = readOnly;
                    tb.BackColor = readOnly ? SystemColors.Control : SystemColors.Window;
                }
            }
        }

        #endregion

        #region Template Selection & Preview

        private void btnBrowseDir_Click(object sender, EventArgs e)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = "选择 .btw 模板文件所在目录";
                if (!string.IsNullOrEmpty(_templatesFolder) && Directory.Exists(_templatesFolder))
                    fbd.SelectedPath = _templatesFolder;
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    _templatesFolder = fbd.SelectedPath;
                    txtTemplateDir.Text = _templatesFolder;
                    PopulateTemplateList(_templatesFolder);
                }
            }
        }

        private void PopulateTemplateList(string folder)
        {
            cmbTemplate.Items.Clear();
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                lblSelectedTemplate.Text = "未找到 .btw 模板";
                _selectedTemplatePath = "";
                return;
            }
            try
            {
                var files = Directory.GetFiles(folder, "*.btw");
                foreach (var f in files)
                    cmbTemplate.Items.Add(new TemplateItem(Path.GetFileName(f), f));
                if (files.Length == 0)
                {
                    lblSelectedTemplate.Text = "未找到 .btw 模板";
                    _selectedTemplatePath = "";
                }
                else
                    cmbTemplate.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show("读取模板文件夹出错: " + ex.Message);
            }
        }

        private void cmbTemplate_SelectedIndexChanged(object sender, EventArgs e)
        {
            var item = cmbTemplate.SelectedItem as TemplateItem;
            if (item != null)
            {
                _selectedTemplatePath = item.FullPath;
                lblSelectedTemplate.Text = item.Name;
                LoadTemplatePreview(_selectedTemplatePath);
                AddLog($"已选择模板: {item.Name}", "INFO");
            }
            else
            {
                _selectedTemplatePath = "";
                lblSelectedTemplate.Text = "未选择模板文件";
                pictureBoxPreview.Image = null;
            }
        }

        private void LoadTemplatePreview(string templatePath)
        {
            CleanupPreview();
            pictureBoxPreview.Image = null;
            SetStatus("生成预览...");
            if (string.IsNullOrEmpty(templatePath) || !File.Exists(templatePath))
            {
                SetStatus("就绪");
                return;
            }
            Task.Run(() =>
            {
                var tempFile = _btService.ExportPreview(templatePath);
                this.BeginInvoke((Action)(() =>
                {
                    if (tempFile != null && File.Exists(tempFile))
                    {
                        try
                        {
                            _previewTempFile = tempFile;
                            using (var img = Image.FromFile(tempFile))
                            {
                                var bmp = new Bitmap(img);
                                var old = pictureBoxPreview.Image;
                                pictureBoxPreview.Image = bmp;
                                old?.Dispose();
                            }
                        }
                        catch (Exception ex)
                        {
                            LoggerService.Error("加载预览图片失败", ex);
                        }
                    }
                    SetStatus("就绪");
                }));
            });
        }

        private void CleanupPreview()
        {
            if (_previewTempFile != null && File.Exists(_previewTempFile))
            {
                try { File.Delete(_previewTempFile); } catch { }
                _previewTempFile = null;
            }
        }

        private class TemplateItem
        {
            public string Name { get; set; }
            public string FullPath { get; set; }
            public TemplateItem(string name, string fullPath) { Name = name; FullPath = fullPath; }
            public override string ToString() => Name;
        }

        #endregion

        #region Print

        private void btnPrint_Click(object sender, EventArgs e) => DoPrint();

        private void DoPrint()
        {
            if (string.IsNullOrEmpty(_selectedTemplatePath) || !File.Exists(_selectedTemplatePath))
            {
                MessageBox.Show("请先选择有效的模板文件 (.btw)");
                return;
            }
            if (!_btService.IsConnected)
            {
                AddLog("BarTender 未连接", "ERROR");
                return;
            }
            var printer = GetSelectedPrinter();
            if (string.IsNullOrEmpty(printer))
            {
                AddLog("请在设置中选择打印机", "ERROR");
                return;
            }

            int count = (int)numDsCount.Value;
            var fields = new DataSourceField[count];
            for (int i = 1; i <= count; i++)
            {
                var tb = inputPanel.Controls.Find($"txtDS_{i}", true).FirstOrDefault() as TextBox;
                var val = tb?.Text?.Trim() ?? "";
                if (string.IsNullOrEmpty(val))
                {
                    MessageBox.Show($"第 {i} 个数据源不能为空");
                    tb?.Focus();
                    return;
                }
                var fieldName = _dsFields.ContainsKey(i) ? _dsFields[i] : $"DS{i}";
                fields[i - 1] = new DataSourceField
                {
                    DisplayName = _dsNames.ContainsKey(i) ? _dsNames[i] : $"数据源 {i}",
                    FieldName = fieldName,
                    Value = val
                };
            }

            int copies = 1;
            try { copies = (int)GetCopiesValue(); } catch { }

            SetStatus("打印中...");
            AddLog("开始打印操作", "INFO");
            SetInputsReadOnly(true);
            btnPrint.Enabled = false;

            Task.Run(() =>
            {
                var result = _btService.Print(_selectedTemplatePath, fields, printer, copies);
                this.BeginInvoke((Action)(() =>
                {
                    if (result.Success)
                    {
                        SetStatus("打印完成");
                        AddLog($"打印完成: {string.Join(", ", fields.Select(f => $"{f.FieldName}={f.Value}"))}", "SUCCESS");
                        _history.Add(fields[0].Value, "PASS");
                        ClearInputsAndFocusFirst();
                    }
                    else
                    {
                        SetStatus("打印失败");
                        AddLog($"打印失败: {result.ErrorMessage}", "ERROR");
                        _history.Add(fields[0].Value, "FAIL");
                    }
                    SetInputsReadOnly(false);
                    btnPrint.Enabled = true;
                    RefreshStats();
                }));
            });
        }

        private string GetSelectedPrinter()
        {
            try
            {
                if (!File.Exists(_configFile)) return "";
                return IniReadValue("General", "Printer", _configFile);
            }
            catch { return ""; }
        }

        private decimal GetCopiesValue()
        {
            try
            {
                var val = IniReadValue("General", "Copies", _configFile);
                if (decimal.TryParse(val, out decimal c) && c > 0) return c;
            }
            catch { }
            return 1;
        }

        #endregion

        #region Config (INI)

        private void btnSaveConfig_Click(object sender, EventArgs e)
        {
            const string password = "20251129";
            var entered = PromptForPassword();
            if (entered == null) return;
            if (entered != password)
            {
                MessageBox.Show("密码错误，配置未保存");
                return;
            }
            SaveConfig(_configFile);
            AddLog($"配置已保存到: {_configFile}", "SUCCESS");
            MessageBox.Show("配置已保存");
        }

        private void btnLoadConfig_Click(object sender, EventArgs e)
        {
            if (File.Exists(_configFile))
            {
                LoadConfig(_configFile);
                AddLog($"配置已从 {_configFile} 加载", "SUCCESS");
                MessageBox.Show("配置已加载");
            }
            else
            {
                AddLog("未找到配置文件", "WARNING");
                MessageBox.Show("未找到配置文件");
            }
        }

        private void SaveConfig(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            IniWriteValue("General", "Count", numDsCount.Value.ToString(), path);
            IniWriteValue("General", "TemplatesFolder", _templatesFolder ?? "", path);
            IniWriteValue("General", "Printer", GetSelectedPrinter(), path);
            IniWriteValue("General", "Copies", GetCopiesValue().ToString(), path);
            for (int i = 1; i <= (int)numDsCount.Value; i++)
            {
                var tb = inputPanel.Controls.Find($"txtDS_{i}", true).FirstOrDefault() as TextBox;
                IniWriteValue($"DataSource{i}", "Value", tb?.Text ?? "", path);
                IniWriteValue($"DataSource{i}", "Name", _dsNames.ContainsKey(i) ? _dsNames[i] : "", path);
                IniWriteValue($"DataSource{i}", "Field", _dsFields.ContainsKey(i) ? _dsFields[i] : "", path);
            }
        }

        private void LoadConfig(string path)
        {
            var countStr = IniReadValue("General", "Count", path);
            if (!int.TryParse(countStr, out int count) || count < 0) count = 0;
            if (count > numDsCount.Maximum) count = (int)numDsCount.Maximum;

            _dsNames.Clear();
            _dsFields.Clear();
            for (int i = 1; i <= count; i++)
            {
                var name = IniReadValue($"DataSource{i}", "Name", path);
                if (!string.IsNullOrWhiteSpace(name)) _dsNames[i] = name;
                var field = IniReadValue($"DataSource{i}", "Field", path);
                if (!string.IsNullOrWhiteSpace(field)) _dsFields[i] = field;
            }

            _suppressCountPrompt = true;
            try { numDsCount.Value = count; } finally { _suppressCountPrompt = false; }

            _templatesFolder = IniReadValue("General", "TemplatesFolder", path);
            if (string.IsNullOrWhiteSpace(_templatesFolder))
                _templatesFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "templates");
            if (!string.IsNullOrEmpty(_templatesFolder) && Directory.Exists(_templatesFolder))
                PopulateTemplateList(_templatesFolder);

            CreateInputControls(count);
            for (int i = 1; i <= count; i++)
            {
                var val = IniReadValue($"DataSource{i}", "Value", path);
                var tb = inputPanel.Controls.Find($"txtDS_{i}", true).FirstOrDefault() as TextBox;
                if (tb != null) tb.Text = val;
            }
            _previousCount = count;
        }

        private string PromptForPassword()
        {
            using (var f = new Form())
            {
                f.Text = "请输入保存密码";
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterParent;
                f.ClientSize = new Size(320, 110);
                f.MinimizeBox = false;
                f.MaximizeBox = false;
                var lbl = new Label { Left = 10, Top = 10, Width = 300, Text = "请输入密码以保存配置：" };
                var tb = new TextBox { Left = 10, Top = 35, Width = 300, UseSystemPasswordChar = true };
                var btnOk = new Button { Text = "确定", Left = 150, Width = 75, Top = 70, DialogResult = DialogResult.OK };
                var btnCancel = new Button { Text = "取消", Left = 235, Width = 75, Top = 70, DialogResult = DialogResult.Cancel };
                f.Controls.AddRange(new Control[] { lbl, tb, btnOk, btnCancel });
                f.AcceptButton = btnOk;
                f.CancelButton = btnCancel;
                return f.ShowDialog(this) == DialogResult.OK ? (tb.Text ?? "") : null;
            }
        }

        #endregion

        #region Settings

        private void btnSettings_Click(object sender, EventArgs e)
        {
            using (var dialog = new SettingsDialog(_dsNames, _dsFields, (int)numDsCount.Value))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _dsNames = dialog.DsNames;
                    _dsFields = dialog.DsFields;
                    CreateInputControls((int)numDsCount.Value);
                }
            }
        }

        #endregion

        #region Log

        private void btnExportLog_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtLog.Text))
            {
                MessageBox.Show("日志为空，无需导出", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (var sfd = new SaveFileDialog())
            {
                sfd.Filter = "文本文件|*.txt|日志文件|*.log";
                sfd.FileName = $"bartender-printer_{DateTime.Now:yyyyMMdd_HHmmss}.log";
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        File.WriteAllText(sfd.FileName, txtLog.Text, Encoding.UTF8);
                        AddLog($"日志已导出到: {sfd.FileName}", "SUCCESS");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"导出失败: {ex.Message}");
                    }
                }
            }
        }

        private void btnClearLog_Click(object sender, EventArgs e)
        {
            txtLog.Clear();
            AddLog("日志已清空", "INFO");
        }

        private void AddLog(string message, string level = "INFO")
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
            if (txtLog.InvokeRequired)
                txtLog.Invoke((Action)(() => { txtLog.AppendText(line + Environment.NewLine); txtLog.ScrollToCaret(); }));
            else
            {
                txtLog.AppendText(line + Environment.NewLine);
                txtLog.ScrollToCaret();
            }
            if (level == "ERROR") LoggerService.Error(message);
            else LoggerService.Info(message);
        }

        #endregion

        #region Status

        private void SetStatus(string text)
        {
            if (lblStatusStrip.InvokeRequired)
                lblStatusStrip.Invoke((Action)(() => lblStatusStrip.Text = text));
            else
                lblStatusStrip.Text = text;
        }

        private void RefreshStats()
        {
            SetStatus($"就绪 | 今日: {_history.TodayCount()} | 总计: {_history.TotalCount()}");
        }

        #endregion

        #region INI Helpers

        [System.Runtime.InteropServices.DllImport("kernel32", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern long WritePrivateProfileString(string section, string key, string val, string filePath);

        [System.Runtime.InteropServices.DllImport("kernel32", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int GetPrivateProfileString(string section, string key, string def, StringBuilder retVal, int size, string filePath);

        private static void IniWriteValue(string section, string key, string value, string filePath)
        {
            WritePrivateProfileString(section, key, value, filePath);
        }

        private static string IniReadValue(string section, string key, string filePath)
        {
            var sb = new StringBuilder(2048);
            GetPrivateProfileString(section, key, "", sb, sb.Capacity, filePath);
            return sb.ToString();
        }

        #endregion
    }

    // Settings Dialog - edit data source names and fields
    public class SettingsDialog : Form
    {
        public Dictionary<int, string> DsNames { get; private set; }
        public Dictionary<int, string> DsFields { get; private set; }
        private TextBox[] txtNames;
        private TextBox[] txtFields;
        private ComboBox cmbPrinter;
        private NumericUpDown numCopies;
        private readonly int _count;
        private readonly string _configFile;

        public SettingsDialog(Dictionary<int, string> names, Dictionary<int, string> fields, int count)
        {
            _count = count;
            _configFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".bartender-printer", "config.ini");

            Text = "设置";
            Size = new Size(500, 450);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;

            var lblPrinter = new Label { Text = "打印机：", Location = new Point(10, 15), Size = new Size(60, 20) };
            cmbPrinter = new ComboBox { Location = new Point(75, 12), Size = new Size(320, 25), DropDownStyle = ComboBoxStyle.DropDownList };
            try { foreach (var p in System.Drawing.Printing.PrinterSettings.InstalledPrinters) cmbPrinter.Items.Add(p); } catch { }
            var savedPrinter = IniReadValue("General", "Printer", _configFile);
            if (!string.IsNullOrEmpty(savedPrinter) && cmbPrinter.Items.Contains(savedPrinter)) cmbPrinter.SelectedItem = savedPrinter;
            else if (cmbPrinter.Items.Count > 0) cmbPrinter.SelectedIndex = 0;

            var lblCopies = new Label { Text = "打印份数：", Location = new Point(10, 48), Size = new Size(60, 20) };
            numCopies = new NumericUpDown { Location = new Point(75, 45), Size = new Size(60, 25), Minimum = 1, Maximum = 99 };
            var savedCopies = IniReadValue("General", "Copies", _configFile);
            if (int.TryParse(savedCopies, out int c) && c > 0) numCopies.Value = Math.Min(c, 99);

            var lblDs = new Label { Text = "数据源配置：", Location = new Point(10, 80), Size = new Size(80, 20) };
            var panel = new Panel { Location = new Point(10, 105), Size = new Size(460, 250), AutoScroll = true, BorderStyle = BorderStyle.FixedSingle };

            txtNames = new TextBox[count];
            txtFields = new TextBox[count];
            for (int i = 1; i <= count; i++)
            {
                int y = (i - 1) * 30 + 5;
                var lblNum = new Label { Text = $"{i}.", Location = new Point(5, y + 3), Size = new Size(20, 20) };
                var lblName = new Label { Text = "显示名称：", Location = new Point(25, y + 3), Size = new Size(65, 20) };
                var txtName = new TextBox { Location = new Point(90, y), Size = new Size(140, 25), Text = names.ContainsKey(i) ? names[i] : $"数据源 {i}" };
                var lblField = new Label { Text = "映射字段：", Location = new Point(240, y + 3), Size = new Size(65, 20) };
                var txtField = new TextBox { Location = new Point(305, y), Size = new Size(140, 25), Text = fields.ContainsKey(i) ? fields[i] : $"DS{i}" };
                txtNames[i - 1] = txtName;
                txtFields[i - 1] = txtField;
                panel.Controls.AddRange(new Control[] { lblNum, lblName, txtName, lblField, txtField });
            }

            var btnOk = new Button { Text = "确定", Location = new Point(310, 370), Size = new Size(75, 28), DialogResult = DialogResult.OK };
            btnOk.Click += (s, e) => Save();
            var btnCancel = new Button { Text = "取消", Location = new Point(395, 370), Size = new Size(75, 28), DialogResult = DialogResult.Cancel };

            Controls.AddRange(new Control[] { lblPrinter, cmbPrinter, lblCopies, numCopies, lblDs, panel, btnOk, btnCancel });
        }

        private void Save()
        {
            DsNames = new Dictionary<int, string>();
            DsFields = new Dictionary<int, string>();
            for (int i = 0; i < _count; i++)
            {
                DsNames[i + 1] = txtNames[i]?.Text?.Trim() ?? $"数据源 {i + 1}";
                DsFields[i + 1] = txtFields[i]?.Text?.Trim() ?? $"DS{i + 1}";
            }
            IniWriteValue("General", "Printer", cmbPrinter.SelectedItem?.ToString() ?? "", _configFile);
            IniWriteValue("General", "Copies", numCopies.Value.ToString(), _configFile);
        }

        [System.Runtime.InteropServices.DllImport("kernel32", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern long WritePrivateProfileString(string section, string key, string val, string filePath);

        [System.Runtime.InteropServices.DllImport("kernel32", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int GetPrivateProfileString(string section, string key, string def, StringBuilder retVal, int size, string filePath);

        private static void IniWriteValue(string s, string k, string v, string p) => WritePrivateProfileString(s, k, v, p);
        private static string IniReadValue(string s, string k, string p) { var sb = new StringBuilder(2048); GetPrivateProfileString(s, k, "", sb, sb.Capacity, p); return sb.ToString(); }
    }
}
