using System;
using System.Collections.Generic;
using System.Data;
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
        private readonly string _version = "v4.0.0";

        private List<DataSourceItem> _dataSources = new List<DataSourceItem>();
        private Panel[] _inputPanels = new Panel[0];
        private TextBox[] _inputTextBoxes = new TextBox[0];
        private string _templatesFolder = "";
        private string _selectedTemplatePath = "";
        private string _previewTempFile = null;
        private List<string> _excelData = new List<string>();

        private class DataSourceItem
        {
            public string Name { get; set; }
            public string Field { get; set; }
            public bool Enabled { get; set; }
        }

        public MainForm()
        {
            InitializeComponent();
            _configFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".bartender-printer", "config.ini");
            Text = $"BarTender 标签打印工具 {_version}";
            MiuiTheme.ApplyTheme(this);
        }

        #region Load / Close

        private void MainForm_Load(object sender, EventArgs e)
        {
            if (File.Exists(_configFile))
                LoadConfig(_configFile);
            else
            {
                _dataSources = new List<DataSourceItem>
                {
                    new DataSourceItem { Name = "IMEI", Field = "IMEI1", Enabled = true }
                };
                _templatesFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "templates");
                try { if (!Directory.Exists(_templatesFolder)) Directory.CreateDirectory(_templatesFolder); } catch { }
            }
            PopulateTemplateList(_templatesFolder);
            RebuildInputFields();
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

        #region Template Selection & Preview

        private void btnBrowseDir_Click(object sender, EventArgs e)
        {
            using (var fbd = new FolderBrowserDialog())
            {
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
                else cmbTemplate.SelectedIndex = 0;
            }
            catch (Exception ex) { MessageBox.Show("读取模板文件夹出错: " + ex.Message); }
        }

        private void cmbTemplate_SelectedIndexChanged(object sender, EventArgs e)
        {
            var item = cmbTemplate.SelectedItem as TemplateItem;
            if (item != null)
            {
                _selectedTemplatePath = item.FullPath;
                lblSelectedTemplate.Text = item.Name;
                LoadTemplatePreview(_selectedTemplatePath);
                LoadTemplateDataSources(_selectedTemplatePath);
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
            if (string.IsNullOrEmpty(templatePath) || !File.Exists(templatePath)) { SetStatus("就绪"); return; }
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
                        catch (Exception ex) { LoggerService.Error("加载预览图片失败", ex); }
                    }
                    SetStatus("就绪");
                }));
            });
        }

        private void LoadTemplateDataSources(string templatePath)
        {
            if (!_btService.IsConnected) return;
            AddLog("正在读取模板数据源...", "INFO");
            Task.Run(() =>
            {
                var names = _btService.GetTemplateDataSources(templatePath);
                this.BeginInvoke((Action)(() =>
                {
                    if (names.Count > 0)
                    {
                        var dlg = new DataSourceSelectDialog(names, _dataSources);
                        if (dlg.ShowDialog(this) == DialogResult.OK)
                        {
                            _dataSources = dlg.SelectedSources;
                            RebuildInputFields();
                            AddLog($"已加载 {names.Count} 个数据源，选择了 {_dataSources.Count} 个", "SUCCESS");
                        }
                        else
                            AddLog($"模板包含 {names.Count} 个数据源: {string.Join(", ", names)}", "INFO");
                    }
                    else
                        AddLog("未能读取模板数据源", "WARNING");
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

        #region Data Source Selection

        private void btnEditDataSources_Click(object sender, EventArgs e)
        {
            var names = _dataSources.Select(d => d.Field).ToList();
            if (names.Count == 0)
            {
                if (!string.IsNullOrEmpty(_selectedTemplatePath) && _btService.IsConnected)
                    names = _btService.GetTemplateDataSources(_selectedTemplatePath);
                if (names.Count == 0)
                    names = new List<string> { "IMEI1", "DS1", "DS2" };
            }
            var dlg = new DataSourceSelectDialog(names, _dataSources);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _dataSources = dlg.SelectedSources;
                RebuildInputFields();
            }
        }

        #endregion

        #region Dynamic Input Fields

        private void RebuildInputFields()
        {
            inputPanel.Controls.Clear();
            var enabled = _dataSources.Where(d => d.Enabled).ToList();
            _inputPanels = new Panel[enabled.Count];
            _inputTextBoxes = new TextBox[enabled.Count];

            int y = 5;
            for (int i = 0; i < enabled.Count; i++)
            {
                var ds = enabled[i];
                var row = new Panel { Location = new Point(5, y), Size = new Size(inputPanel.Width - 10, 28), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
                var lbl = new Label { Text = ds.Name + "：", Location = new Point(0, 4), Size = new Size(90, 20), TextAlign = ContentAlignment.MiddleRight };
                MiuiTheme.StyleLabel(lbl);
                var txt = new TextBox { Location = new Point(95, 1), Size = new Size(row.Width - 100, 25), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
                MiuiTheme.StyleTextBox(txt);
                txt.Tag = i;
                txt.KeyDown += InputTextBox_KeyDown;
                row.Controls.Add(lbl);
                row.Controls.Add(txt);
                inputPanel.Controls.Add(row);
                _inputPanels[i] = row;
                _inputTextBoxes[i] = txt;
                y += 32;
            }
            btnPrint.Location = new Point(10, inputPanel.Bottom + 8);
        }

        private void InputTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            var tb = sender as TextBox;
            if (tb == null) return;
            int idx = (int)tb.Tag;
            if (idx < _inputTextBoxes.Length - 1)
            {
                _inputTextBoxes[idx + 1].Focus();
                _inputTextBoxes[idx + 1].SelectAll();
            }
            else DoPrint();
        }

        private void ClearInputsAndFocusFirst()
        {
            foreach (var tb in _inputTextBoxes)
                if (tb != null) tb.Text = "";
            if (_inputTextBoxes.Length > 0) { _inputTextBoxes[0].Focus(); _inputTextBoxes[0].SelectAll(); }
        }

        private void SetInputsReadOnly(bool readOnly)
        {
            foreach (var tb in _inputTextBoxes)
            {
                if (tb != null)
                {
                    tb.ReadOnly = readOnly;
                    tb.BackColor = readOnly ? SystemColors.Control : MiuiTheme.InputBackground;
                }
            }
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
            if (!_btService.IsConnected) { AddLog("BarTender 未连接", "ERROR"); return; }
            var printer = GetSelectedPrinter();
            if (string.IsNullOrEmpty(printer)) { AddLog("请在设置中选择打印机", "ERROR"); return; }

            var enabled = _dataSources.Where(d => d.Enabled).ToList();
            if (enabled.Count == 0) { AddLog("请配置数据源", "ERROR"); return; }

            var fieldValues = new Dictionary<string, string>();
            for (int i = 0; i < enabled.Count; i++)
            {
                var val = _inputTextBoxes[i]?.Text?.Trim() ?? "";
                if (string.IsNullOrEmpty(val))
                {
                    MessageBox.Show($"\"{enabled[i].Name}\" 不能为空");
                    _inputTextBoxes[i]?.Focus();
                    return;
                }
                fieldValues[enabled[i].Field] = val;
            }

            // Duplicate check (first field)
            var firstVal = fieldValues.Values.FirstOrDefault();
            if (!string.IsNullOrEmpty(firstVal) && _history.IsPrinted(firstVal))
            {
                if (MessageBox.Show($"\"{firstVal}\" 已打印过，是否继续？", "数据重复", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;
            }

            int copies = 1;
            try { copies = (int)GetCopiesValue(); } catch { }

            SetStatus("打印中...");
            AddLog($"开始打印: {string.Join(", ", fieldValues.Select(kv => $"{kv.Key}={kv.Value}"))}", "INFO");
            SetInputsReadOnly(true);
            btnPrint.Enabled = false;

            Task.Run(() =>
            {
                var result = _btService.Print(_selectedTemplatePath, fieldValues, printer, copies);
                this.BeginInvoke((Action)(() =>
                {
                    if (result.Success)
                    {
                        SetStatus("打印完成");
                        AddLog("打印完成", "SUCCESS");
                        _history.Add(firstVal, "PASS");
                        ClearInputsAndFocusFirst();
                    }
                    else
                    {
                        SetStatus("打印失败");
                        AddLog($"打印失败: {result.ErrorMessage}", "ERROR");
                        _history.Add(firstVal, "FAIL");
                    }
                    SetInputsReadOnly(false);
                    btnPrint.Enabled = true;
                    RefreshStats();
                }));
            });
        }

        private string GetSelectedPrinter()
        {
            try { return IniReadValue("General", "Printer", _configFile); } catch { return ""; }
        }
        private decimal GetCopiesValue()
        {
            try { var v = IniReadValue("General", "Copies", _configFile); if (decimal.TryParse(v, out decimal c) && c > 0) return c; } catch { }
            return 1;
        }

        #endregion

        #region History

        private void txtSearch_TextChanged(object sender, EventArgs e) => LoadHistory();
        private void btnClearSearch_Click(object sender, EventArgs e) { txtSearch.Text = ""; }
        private void btnClearHistory_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("确定要清空所有记录吗？", "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            { _history.Clear(); LoadHistory(); RefreshStats(); }
        }
        private void btnExportHistory_Click(object sender, EventArgs e)
        {
            if (_history.Records.Count == 0) { MessageBox.Show("没有可导出的历史记录"); return; }
            using (var sfd = new SaveFileDialog { Filter = "CSV|*.csv", FileName = $"print_records_{DateTime.Now:yyyyMMdd_HHmmss}.csv" })
            {
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    try { _history.Export(sfd.FileName, txtSearch?.Text?.Trim() ?? ""); MessageBox.Show($"已导出到:\n{sfd.FileName}"); }
                    catch (Exception ex) { MessageBox.Show($"导出失败: {ex.Message}"); }
                }
            }
        }

        private void LoadHistory()
        {
            dgvHistory.DataSource = null;
            var dt = new DataTable();
            dt.Columns.Add("IMEI", typeof(string));
            dt.Columns.Add("打印时间", typeof(string));
            dt.Columns.Add("状态", typeof(string));
            var keyword = txtSearch?.Text?.Trim().ToLower() ?? "";
            foreach (var r in _history.Records.AsEnumerable().Reverse())
            {
                if (!string.IsNullOrEmpty(keyword) && !r.Imei.ToLower().Contains(keyword) && !r.PrintTime.ToLower().Contains(keyword) && !r.Status.ToLower().Contains(keyword)) continue;
                dt.Rows.Add(r.Imei, r.PrintTime, r.Status);
            }
            dgvHistory.DataSource = dt;
        }

        private void RefreshStats()
        {
            lblTodayCount.Text = _history.TodayCount().ToString();
            lblTotalCount.Text = _history.TotalCount().ToString();
            SetStatus($"就绪 | 今日: {_history.TodayCount()} | 总计: {_history.TotalCount()}");
        }

        #endregion

        #region Config (INI)

        private void btnSaveConfig_Click(object sender, EventArgs e)
        {
            SaveConfig(_configFile);
            AddLog($"配置已保存", "SUCCESS");
            MessageBox.Show("配置已保存");
        }

        private void btnLoadConfig_Click(object sender, EventArgs e)
        {
            if (File.Exists(_configFile))
            {
                LoadConfig(_configFile);
                PopulateTemplateList(_templatesFolder);
                RebuildInputFields();
                AddLog("配置已加载", "SUCCESS");
                MessageBox.Show("配置已加载");
            }
            else { MessageBox.Show("未找到配置文件"); }
        }

        private void SaveConfig(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            IniWriteValue("General", "TemplatesFolder", _templatesFolder ?? "", path);
            IniWriteValue("General", "Printer", GetSelectedPrinter(), path);
            IniWriteValue("General", "Copies", GetCopiesValue().ToString(), path);
            IniWriteValue("General", "DataSourceCount", _dataSources.Count.ToString(), path);
            for (int i = 0; i < _dataSources.Count; i++)
            {
                IniWriteValue($"DS{i}", "Name", _dataSources[i].Name, path);
                IniWriteValue($"DS{i}", "Field", _dataSources[i].Field, path);
                IniWriteValue($"DS{i}", "Enabled", _dataSources[i].Enabled.ToString(), path);
            }
        }

        private void LoadConfig(string path)
        {
            _templatesFolder = IniReadValue("General", "TemplatesFolder", path);
            if (string.IsNullOrWhiteSpace(_templatesFolder))
                _templatesFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "templates");
            txtTemplateDir.Text = _templatesFolder;

            var countStr = IniReadValue("General", "DataSourceCount", path);
            int count = 0;
            if (!int.TryParse(countStr, out count) || count < 0) count = 0;
            _dataSources = new List<DataSourceItem>();
            for (int i = 0; i < count; i++)
            {
                _dataSources.Add(new DataSourceItem
                {
                    Name = IniReadValue($"DS{i}", "Name", path),
                    Field = IniReadValue($"DS{i}", "Field", path),
                    bool.TryParse(IniReadValue($"DS{i}", "Enabled", path), out bool en) ? en : true
                });
            }
            if (_dataSources.Count == 0)
                _dataSources.Add(new DataSourceItem { Name = "IMEI", Field = "IMEI1", Enabled = true });
        }

        #endregion

        #region Settings

        private void btnSettings_Click(object sender, EventArgs e)
        {
            using (var dialog = new SettingsDialog())
            {
                var printer = GetSelectedPrinter();
                var copies = GetCopiesValue();
                dialog.LoadSettings(printer, (int)copies);
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    IniWriteValue("General", "Printer", dialog.Printer, _configFile);
                    IniWriteValue("General", "Copies", dialog.Copies.ToString(), _configFile);
                    AddLog("设置已保存", "SUCCESS");
                }
            }
        }

        #endregion

        #region Log

        private void btnExportLog_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtLog.Text)) { MessageBox.Show("日志为空"); return; }
            using (var sfd = new SaveFileDialog { Filter = "文本|*.txt|日志|*.log", FileName = $"log_{DateTime.Now:yyyyMMdd_HHmmss}.log" })
            {
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    try { File.WriteAllText(sfd.FileName, txtLog.Text, Encoding.UTF8); AddLog($"日志已导出", "SUCCESS"); }
                    catch (Exception ex) { MessageBox.Show($"导出失败: {ex.Message}"); }
                }
            }
        }
        private void btnClearLog_Click(object sender, EventArgs e) { txtLog.Clear(); AddLog("日志已清空", "INFO"); }

        private void AddLog(string message, string level = "INFO")
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] [{level}] {message}";
            if (txtLog.InvokeRequired) txtLog.Invoke((Action)(() => { txtLog.AppendText(line + Environment.NewLine); txtLog.ScrollToCaret(); }));
            else { txtLog.AppendText(line + Environment.NewLine); txtLog.ScrollToCaret(); }
            if (level == "ERROR") LoggerService.Error(message); else LoggerService.Info(message);
        }

        #endregion

        #region Status

        private void SetStatus(string text)
        {
            if (statusStrip.InvokeRequired) statusStrip.Invoke((Action)(() => lblStatusStrip.Text = text));
            else lblStatusStrip.Text = text;
        }

        #endregion

        #region INI

        [System.Runtime.InteropServices.DllImport("kernel32", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern long WritePrivateProfileString(string section, string key, string val, string filePath);
        [System.Runtime.InteropServices.DllImport("kernel32", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int GetPrivateProfileString(string section, string key, string def, StringBuilder retVal, int size, string filePath);
        private static void IniWriteValue(string s, string k, string v, string p) => WritePrivateProfileString(s, k, v, p);
        private static string IniReadValue(string s, string k, string p) { var sb = new StringBuilder(2048); GetPrivateProfileString(s, k, "", sb, sb.Capacity, p); return sb.ToString(); }

        #endregion
    }

    // Data Source Selection Dialog
    public class DataSourceSelectDialog : Form
    {
        public List<DataSourceItem> SelectedSources { get; private set; }
        private CheckBox[] _checkBoxes;
        private TextBox[] _nameBoxes;
        private readonly List<string> _availableFields;
        private readonly List<DataSourceItem> _currentSources;

        public DataSourceSelectDialog(List<string> availableFields, List<DataSourceItem> currentSources)
        {
            _availableFields = availableFields;
            _currentSources = currentSources;
            Text = "选择数据源";
            Size = new Size(450, 400);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;

            var lbl = new Label { Text = "勾选需要的数据源并设置显示名称：", Location = new Point(10, 10), Size = new Size(400, 20) };
            var panel = new Panel { Location = new Point(10, 35), Size = new Size(415, 280), AutoScroll = true, BorderStyle = BorderStyle.FixedSingle };

            _checkBoxes = new CheckBox[availableFields.Count];
            _nameBoxes = new TextBox[availableFields.Count];

            for (int i = 0; i < availableFields.Count; i++)
            {
                int y = i * 32 + 5;
                var existing = currentSources.FirstOrDefault(d => d.Field == availableFields[i]);
                var cb = new CheckBox { Text = availableFields[i], Location = new Point(5, y + 3), Size = new Size(120, 20), Checked = existing?.Enabled ?? false };
                var txt = new TextBox { Location = new Point(130, y), Size = new Size(270, 25), Text = existing?.Name ?? availableFields[i] };
                _checkBoxes[i] = cb;
                _nameBoxes[i] = txt;
                panel.Controls.Add(cb);
                panel.Controls.Add(txt);
            }

            var btnOk = new Button { Text = "确定", Location = new Point(260, 325), Size = new Size(75, 28), DialogResult = DialogResult.OK };
            btnOk.Click += (s, e) => Save();
            var btnCancel = new Button { Text = "取消", Location = new Point(350, 325), Size = new Size(75, 28), DialogResult = DialogResult.Cancel };
            Controls.AddRange(new Control[] { lbl, panel, btnOk, btnCancel });
            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }

        private void Save()
        {
            SelectedSources = new List<DataSourceItem>();
            for (int i = 0; i < _availableFields.Count; i++)
            {
                if (_checkBoxes[i].Checked)
                {
                    SelectedSources.Add(new DataSourceItem
                    {
                        Name = _nameBoxes[i].Text?.Trim() ?? _availableFields[i],
                        Field = _availableFields[i],
                        Enabled = true
                    });
                }
            }
        }
    }

    // Settings Dialog
    public class SettingsDialog : Form
    {
        public string Printer { get; private set; }
        public int Copies { get; private set; }
        private ComboBox cmbPrinter;
        private NumericUpDown numCopies;

        public SettingsDialog()
        {
            Text = "设置";
            Size = new Size(400, 180);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;

            var lblPrinter = new Label { Text = "打印机：", Location = new Point(10, 15), Size = new Size(60, 20) };
            cmbPrinter = new ComboBox { Location = new Point(75, 12), Size = new Size(290, 25), DropDownStyle = ComboBoxStyle.DropDownList };
            try { foreach (var p in System.Drawing.Printing.PrinterSettings.InstalledPrinters) cmbPrinter.Items.Add(p); } catch { }

            var lblCopies = new Label { Text = "打印份数：", Location = new Point(10, 50), Size = new Size(60, 20) };
            numCopies = new NumericUpDown { Location = new Point(75, 47), Size = new Size(60, 25), Minimum = 1, Maximum = 99 };

            var btnOk = new Button { Text = "确定", Location = new Point(210, 90), Size = new Size(75, 28), DialogResult = DialogResult.OK };
            btnOk.Click += (s, e) => { Printer = cmbPrinter.SelectedItem?.ToString() ?? ""; Copies = (int)numCopies.Value; };
            var btnCancel = new Button { Text = "取消", Location = new Point(295, 90), Size = new Size(75, 28), DialogResult = DialogResult.Cancel };

            Controls.AddRange(new Control[] { lblPrinter, cmbPrinter, lblCopies, numCopies, btnOk, btnCancel });
        }

        public void LoadSettings(string printer, int copies)
        {
            if (!string.IsNullOrEmpty(printer) && cmbPrinter.Items.Contains(printer)) cmbPrinter.SelectedItem = printer;
            else if (cmbPrinter.Items.Count > 0) cmbPrinter.SelectedIndex = 0;
            numCopies.Value = Math.Max(1, Math.Min(99, copies));
        }
    }

    public class DataSourceItem
    {
        public string Name { get; set; }
        public string Field { get; set; }
        public bool Enabled { get; set; }
    }
}
