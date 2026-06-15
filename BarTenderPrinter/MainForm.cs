using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BarTenderPrinter
{
    public partial class MainForm : Form
    {
        private readonly BarTenderService _btService = new BarTenderService();
        private readonly HistoryManager _history = new HistoryManager();
        private readonly string _configFile;
        private readonly string _version = "v3.1.0";

        private DataSourceConfig[] _dsConfigs = new DataSourceConfig[0];
        private TextBox[] _dsInputs = new TextBox[0];
        private Label[] _dsLabels = new Label[0];
        private string _selectedTemplate = "";
        private string _templateDir = "";
        private string _previewTempFile = null;

        public MainForm()
        {
            InitializeComponent();
            _configFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".bartender-printer", "config.json");
            Text = $"BarTender 标签打印工具 {_version}";
            titleLabel.Text = $"BarTender 标签打印工具 {_version}";
        }

        #region Load / Close

        private void MainForm_Load(object sender, EventArgs e)
        {
            LoadConfig();
            _history.Load();
            RefreshStats();
            InitBarTender();
            LoadPrinters();
            RebuildInputFields();
            if (!string.IsNullOrEmpty(_templateDir) && Directory.Exists(_templateDir))
                LoadTemplateList();
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            SaveConfig();
            CleanupPreview();
            _btService.Dispose();
        }

        #endregion

        #region BarTender Init

        private void InitBarTender()
        {
            LoggerService.Info("正在初始化 BarTender...");
            if (_btService.Connect())
                lblStatus.Text = "BarTender 已连接";
            else
            {
                lblStatus.Text = "BarTender 连接失败";
                AppendStatus("BarTender 连接失败，请检查是否已安装", true);
            }
        }

        #endregion

        #region Template Selection & Preview

        private void btnBrowseDir_Click(object sender, EventArgs e)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = "选择 .btw 模板文件所在目录";
                if (!string.IsNullOrEmpty(_templateDir) && Directory.Exists(_templateDir))
                    fbd.SelectedPath = _templateDir;
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    _templateDir = fbd.SelectedPath;
                    txtTemplateDir.Text = _templateDir;
                    SaveConfig();
                    LoadTemplateList();
                }
            }
        }

        private void LoadTemplateList()
        {
            cmbTemplate.Items.Clear();
            var templates = _btService.GetAvailableTemplates(_templateDir);
            foreach (var t in templates)
                cmbTemplate.Items.Add(Path.GetFileName(t));
            if (cmbTemplate.Items.Count > 0)
            {
                var savedIdx = -1;
                for (int i = 0; i < cmbTemplate.Items.Count; i++)
                {
                    if (cmbTemplate.Items[i].ToString() == _selectedTemplate)
                    { savedIdx = i; break; }
                }
                cmbTemplate.SelectedIndex = savedIdx >= 0 ? savedIdx : 0;
            }
        }

        private void cmbTemplate_SelectedIndexChanged(object sender, EventArgs e)
        {
            _selectedTemplate = cmbTemplate.SelectedItem?.ToString() ?? "";
            SaveConfig();
            UpdatePreview();
        }

        private string GetFullTemplatePath()
        {
            if (string.IsNullOrEmpty(_templateDir) || string.IsNullOrEmpty(_selectedTemplate))
                return "";
            return Path.Combine(_templateDir, _selectedTemplate);
        }

        private void UpdatePreview()
        {
            CleanupPreview();
            previewBox.Image = null;
            previewBox.Image = CreatePlaceholderImage("正在生成预览...");

            var templatePath = GetFullTemplatePath();
            if (string.IsNullOrEmpty(templatePath) || !File.Exists(templatePath))
            {
                previewBox.Image = CreatePlaceholderImage("未找到模板文件");
                return;
            }

            Task.Run(() =>
            {
                var result = _btService.ExportPreview(templatePath, 400, 300);
                this.BeginInvoke((Action)(() =>
                {
                    if (result != null && File.Exists(result))
                    {
                        try
                        {
                            _previewTempFile = result;
                            using (var fs = new FileStream(result, FileMode.Open, FileAccess.Read))
                            {
                                previewBox.Image = Image.FromStream(fs);
                            }
                        }
                        catch (Exception ex)
                        {
                            LoggerService.Error("加载预览图片失败", ex);
                            previewBox.Image = CreatePlaceholderImage("预览加载失败");
                        }
                    }
                    else
                    {
                        previewBox.Image = CreatePlaceholderImage("预览生成失败");
                    }
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

        private static Bitmap CreatePlaceholderImage(string text)
        {
            var bmp = new Bitmap(400, 300);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.WhiteSmoke);
                using (var font = new Font("Microsoft YaHei", 11F))
                using (var brush = new SolidBrush(Color.Gray))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    g.DrawString(text, font, brush, new RectangleF(0, 0, 400, 300), sf);
                }
            }
            return bmp;
        }

        #endregion

        #region Dynamic Data Source Inputs

        private void RebuildInputFields()
        {
            inputPanel.Controls.Clear();
            if (_dsConfigs.Length == 0)
            {
                _dsInputs = new TextBox[0];
                _dsLabels = new Label[0];
                var lbl = new Label
                {
                    Text = "请在“设置”中配置数据源",
                    ForeColor = Color.Gray,
                    Location = new Point(5, 10),
                    AutoSize = true
                };
                inputPanel.Controls.Add(lbl);
                btnPrint.Location = new Point(inputPanel.Left, inputPanel.Bottom + 10);
                return;
            }

            _dsInputs = new TextBox[_dsConfigs.Length];
            _dsLabels = new Label[_dsConfigs.Length];

            int y = 5;
            for (int i = 0; i < _dsConfigs.Length; i++)
            {
                var cfg = _dsConfigs[i];
                var lbl = new Label
                {
                    Text = cfg.DisplayName + "：",
                    Location = new Point(5, y + 3),
                    Size = new Size(100, 20),
                    TextAlign = ContentAlignment.MiddleRight
                };
                var txt = new TextBox
                {
                    Location = new Point(110, y),
                    Size = new Size(inputPanel.Width - 120, 25),
                    Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                    Tag = i
                };
                var idx = i;
                txt.KeyDown += (s, e) => OnInputKeyDown(e, idx);

                _dsLabels[i] = lbl;
                _dsInputs[i] = txt;
                inputPanel.Controls.Add(lbl);
                inputPanel.Controls.Add(txt);
                y += 32;
            }

            btnPrint.Location = new Point(inputPanel.Left, inputPanel.Bottom + 10);
        }

        private void OnInputKeyDown(KeyEventArgs e, int index)
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;

            if (index < _dsInputs.Length - 1)
            {
                _dsInputs[index + 1].Focus();
                _dsInputs[index + 1].SelectAll();
            }
            else
            {
                DoPrint();
            }
        }

        private void ClearAllInputs()
        {
            foreach (var txt in _dsInputs)
                txt.Text = "";
            if (_dsInputs.Length > 0)
            {
                _dsInputs[0].Focus();
                _dsInputs[0].SelectAll();
            }
        }

        #endregion

        #region Print

        private void btnPrint_Click(object sender, EventArgs e)
        {
            DoPrint();
        }

        private void DoPrint()
        {
            var templatePath = GetFullTemplatePath();
            if (string.IsNullOrEmpty(templatePath) || !File.Exists(templatePath))
            {
                AppendStatus("错误：请选择有效的 BarTender 模板", true);
                return;
            }
            var printer = GetSelectedPrinter();
            if (string.IsNullOrEmpty(printer))
            {
                AppendStatus("错误：请选择打印机", true);
                return;
            }
            if (!_btService.IsConnected)
            {
                AppendStatus("错误：BarTender 未连接", true);
                return;
            }
            if (_dsConfigs.Length == 0)
            {
                AppendStatus("错误：请在设置中配置数据源", true);
                return;
            }

            // Collect values
            var fields = new DataSourceField[_dsConfigs.Length];
            for (int i = 0; i < _dsConfigs.Length; i++)
            {
                var val = _dsInputs[i]?.Text?.Trim() ?? "";
                if (string.IsNullOrEmpty(val))
                {
                    AppendStatus($"错误：{_dsConfigs[i].DisplayName} 不能为空", true);
                    _dsInputs[i]?.Focus();
                    return;
                }
                fields[i] = new DataSourceField
                {
                    DisplayName = _dsConfigs[i].DisplayName,
                    FieldName = _dsConfigs[i].FieldName,
                    Value = val
                };
            }

            // Check duplicates
            var firstImei = fields[0].Value;
            if (_history.IsPrinted(firstImei))
            {
                var msg = $"IMEI {firstImei} 已打印过，是否继续？";
                if (MessageBox.Show(msg, "数据重复", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;
            }

            int copies = 1;
            try { copies = (int)GetCopiesValue(); } catch { }

            ClearStatus();
            AppendStatus($"开始打印: {string.Join(", ", fields.Select(f => $"{f.DisplayName}={f.Value}"))}");
            lblStatus.Text = "打印中...";

            Task.Run(() =>
            {
                var result = _btService.Print(templatePath, fields, printer, copies);
                var status = result.Success ? "PASS" : "FAIL";
                _history.Add(firstImei, status);
                this.BeginInvoke((Action)(() =>
                {
                    if (result.Success)
                    {
                        AppendStatus($"PASS {firstImei}");
                        ClearAllInputs();
                    }
                    else
                    {
                        AppendStatus($"FAIL {firstImei} - {result.ErrorMessage}", true);
                    }
                    lblStatus.Text = result.Success ? "打印成功" : "打印失败";
                    RefreshStats();
                }));
            });
        }

        #endregion

        #region Config

        private void LoadConfig()
        {
            try
            {
                if (!File.Exists(_configFile)) return;
                var json = File.ReadAllText(_configFile);
                var config = ParseJson(json);

                _templateDir = config.ContainsKey("TemplateDir") ? config["TemplateDir"] : "";
                _selectedTemplate = config.ContainsKey("SelectedTemplate") ? config["SelectedTemplate"] : "";
                txtTemplateDir.Text = _templateDir;

                // Load data source configs
                int dsCount = 0;
                if (config.ContainsKey("DataSourceCount"))
                    int.TryParse(config["DataSourceCount"], out dsCount);
                if (dsCount <= 0) dsCount = 1;

                _dsConfigs = new DataSourceConfig[dsCount];
                for (int i = 0; i < dsCount; i++)
                {
                    var dn = config.ContainsKey($"DS{i}_DisplayName") ? config[$"DS{i}_DisplayName"] : $"数据源{i + 1}";
                    var fn = config.ContainsKey($"DS{i}_FieldName") ? config[$"DS{i}_FieldName"] : $"Field{i + 1}";
                    _dsConfigs[i] = new DataSourceConfig { DisplayName = dn, FieldName = fn };
                }
            }
            catch (Exception ex)
            {
                LoggerService.Error("加载配置失败", ex);
                if (_dsConfigs.Length == 0)
                    _dsConfigs = new[] { new DataSourceConfig { DisplayName = "IMEI", FieldName = "IMEI1" } };
            }
        }

        private void SaveConfig()
        {
            try
            {
                var dir = Path.GetDirectoryName(_configFile);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var config = new Dictionary<string, string>
                {
                    ["TemplateDir"] = _templateDir ?? "",
                    ["SelectedTemplate"] = _selectedTemplate ?? "",
                    ["DataSourceCount"] = _dsConfigs.Length.ToString()
                };
                for (int i = 0; i < _dsConfigs.Length; i++)
                {
                    config[$"DS{i}_DisplayName"] = _dsConfigs[i].DisplayName ?? "";
                    config[$"DS{i}_FieldName"] = _dsConfigs[i].FieldName ?? "";
                }
                File.WriteAllText(_configFile, SerializeJson(config));
            }
            catch (Exception ex)
            {
                LoggerService.Error("保存配置失败", ex);
            }
        }

        public void ApplyDataSourceConfig(DataSourceConfig[] configs)
        {
            _dsConfigs = configs;
            SaveConfig();
            RebuildInputFields();
        }

        #endregion

        #region Settings

        private void btnSettings_Click(object sender, EventArgs e)
        {
            using (var dialog = new SettingsDialog(_dsConfigs))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    ApplyDataSourceConfig(dialog.Configs);
                }
            }
        }

        #endregion

        #region Log

        private void btnExportLog_Click(object sender, EventArgs e)
        {
            var logFile = LoggerService.GetLogFile();
            if (!File.Exists(logFile))
            {
                MessageBox.Show("日志文件不存在", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (var sfd = new SaveFileDialog())
            {
                sfd.Filter = "日志文件|*.log|文本文件|*.txt";
                sfd.FileName = $"bartender-printer_{DateTime.Now:yyyyMMdd_HHmmss}.log";
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        LoggerService.ExportLog(sfd.FileName);
                        MessageBox.Show($"日志已导出到:\n{sfd.FileName}", "导出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"导出失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        #endregion

        #region Status

        private void AppendStatus(string msg, bool isError = false)
        {
            var prefix = isError ? "[ERROR] " : "";
            txtStatus.AppendText($"{DateTime.Now:HH:mm:ss} {prefix}{msg}{Environment.NewLine}");
            LoggerService.Info(msg);
        }

        private void ClearStatus() => txtStatus.Clear();

        private void RefreshStats()
        {
            lblStatus.Text = $"就绪 | 今日: {_history.TodayCount()} | 总计: {_history.TotalCount()}";
        }

        #endregion

        #region Helpers

        private string GetSelectedPrinter()
        {
            // Printer is loaded in settings, get from config
            try
            {
                if (!File.Exists(_configFile)) return "";
                var json = File.ReadAllText(_configFile);
                var config = ParseJson(json);
                return config.ContainsKey("Printer") ? config["Printer"] : "";
            }
            catch { return ""; }
        }

        private decimal GetCopiesValue()
        {
            try
            {
                if (!File.Exists(_configFile)) return 1;
                var json = File.ReadAllText(_configFile);
                var config = ParseJson(json);
                if (config.ContainsKey("Copies"))
                    return decimal.Parse(config["Copies"]);
            }
            catch { }
            return 1;
        }

        private void LoadPrinters()
        {
            // Printers are managed in settings dialog
        }

        #endregion

        #region JSON Helpers

        private static Dictionary<string, string> ParseJson(string json)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrWhiteSpace(json)) return result;
            json = json.Trim();
            if (json.StartsWith("{")) json = json.Substring(1);
            if (json.EndsWith("}")) json = json.Substring(0, json.Length - 1);
            var pairs = SplitJsonPairs(json);
            foreach (var pair in pairs)
            {
                var colonIdx = pair.IndexOf(':');
                if (colonIdx < 0) continue;
                var key = pair.Substring(0, colonIdx).Trim().Trim('"');
                var value = pair.Substring(colonIdx + 1).Trim().Trim('"');
                result[key] = value;
            }
            return result;
        }

        private static List<string> SplitJsonPairs(string json)
        {
            var result = new List<string>();
            int depth = 0, start = 0;
            bool inString = false;
            for (int i = 0; i < json.Length; i++)
            {
                var c = json[i];
                if (c == '"') inString = !inString;
                if (inString) continue;
                if (c == '{' || c == '[') depth++;
                if (c == '}' || c == ']') depth--;
                if (c == ',' && depth == 0)
                {
                    result.Add(json.Substring(start, i - start));
                    start = i + 1;
                }
            }
            result.Add(json.Substring(start));
            return result;
        }

        private static string SerializeJson(Dictionary<string, string> dict)
        {
            var pairs = dict.Select(kv => $"\"{kv.Key}\": \"{EscapeJson(kv.Value)}\"");
            return "{\n  " + string.Join(",\n  ", pairs) + "\n}";
        }

        private static string EscapeJson(string s)
        {
            return s?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r") ?? "";
        }

        #endregion
    }

    // Settings Dialog
    public class SettingsDialog : Form
    {
        public DataSourceConfig[] Configs { get; private set; }
        private NumericUpDown numDsCount;
        private Panel dsPanel;
        private TextBox[] txtDisplayNames;
        private TextBox[] txtFieldNames;
        private ComboBox cmbPrinter;
        private NumericUpDown numCopies;
        private readonly string _configFile;

        public SettingsDialog(DataSourceConfig[] currentConfigs)
        {
            _configFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".bartender-printer", "config.json");

            Text = "设置";
            Size = new System.Drawing.Size(500, 520);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;

            // Printer
            var lblPrinter = new Label { Text = "打印机：", Location = new System.Drawing.Point(10, 15), Size = new System.Drawing.Size(60, 20) };
            cmbPrinter = new ComboBox { Location = new System.Drawing.Point(75, 12), Size = new System.Drawing.Size(320, 25), DropDownStyle = ComboBoxStyle.DropDownList };
            try
            {
                foreach (var p in System.Drawing.Printing.PrinterSettings.InstalledPrinters)
                    cmbPrinter.Items.Add(p);
            }
            catch { }
            var savedPrinter = GetConfigValue("Printer");
            if (!string.IsNullOrEmpty(savedPrinter) && cmbPrinter.Items.Contains(savedPrinter))
                cmbPrinter.SelectedItem = savedPrinter;
            else if (cmbPrinter.Items.Count > 0)
                cmbPrinter.SelectedIndex = 0;

            // Copies
            var lblCopies = new Label { Text = "打印份数：", Location = new System.Drawing.Point(10, 48), Size = new System.Drawing.Size(60, 20) };
            numCopies = new NumericUpDown { Location = new System.Drawing.Point(75, 45), Size = new System.Drawing.Size(60, 25), Minimum = 1, Maximum = 99 };
            var savedCopies = GetConfigValue("Copies");
            if (int.TryParse(savedCopies, out int c) && c > 0) numCopies.Value = Math.Min(c, 99);

            // Data Source Count
            var lblDsCount = new Label { Text = "数据源数量：", Location = new System.Drawing.Point(10, 85), Size = new System.Drawing.Size(80, 20) };
            numDsCount = new NumericUpDown { Location = new System.Drawing.Point(95, 82), Size = new System.Drawing.Size(60, 25), Minimum = 1, Maximum = 20 };
            numDsCount.Value = Math.Max(1, currentConfigs.Length);
            numDsCount.ValueChanged += (s, e) => RebuildDsFields((int)numDsCount.Value);

            // Data Source Panel
            dsPanel = new Panel { Location = new System.Drawing.Point(10, 115), Size = new System.Drawing.Size(460, 300), AutoScroll = true, BorderStyle = BorderStyle.FixedSingle };

            // Buttons
            var btnOk = new Button { Text = "确定", Location = new System.Drawing.Point(310, 430), Size = new System.Drawing.Size(75, 28), DialogResult = DialogResult.OK };
            btnOk.Click += (s, e) => SaveSettings();
            var btnCancel = new Button { Text = "取消", Location = new System.Drawing.Point(395, 430), Size = new System.Drawing.Size(75, 28), DialogResult = DialogResult.Cancel };

            Controls.Add(lblPrinter);
            Controls.Add(cmbPrinter);
            Controls.Add(lblCopies);
            Controls.Add(numCopies);
            Controls.Add(lblDsCount);
            Controls.Add(numDsCount);
            Controls.Add(dsPanel);
            Controls.Add(btnOk);
            Controls.Add(btnCancel);

            RebuildDsFields((int)numDsCount.Value, currentConfigs);
        }

        private void RebuildDsFields(int count, DataSourceConfig[] existing = null)
        {
            dsPanel.Controls.Clear();
            txtDisplayNames = new TextBox[count];
            txtFieldNames = new TextBox[count];

            for (int i = 0; i < count; i++)
            {
                int y = i * 32 + 5;
                var lblNum = new Label { Text = $"{i + 1}.", Location = new System.Drawing.Point(5, y + 3), Size = new System.Drawing.Size(20, 20) };
                var lblName = new Label { Text = "显示名称：", Location = new System.Drawing.Point(25, y + 3), Size = new System.Drawing.Size(65, 20) };
                var txtName = new TextBox { Location = new System.Drawing.Point(90, y), Size = new System.Drawing.Size(140, 25) };
                var lblField = new Label { Text = "映射字段：", Location = new System.Drawing.Point(240, y + 3), Size = new System.Drawing.Size(65, 20) };
                var txtField = new TextBox { Location = new System.Drawing.Point(305, y), Size = new System.Drawing.Size(140, 25) };

                if (existing != null && i < existing.Length)
                {
                    txtName.Text = existing[i].DisplayName ?? "";
                    txtField.Text = existing[i].FieldName ?? "";
                }
                else
                {
                    txtName.Text = $"数据源{i + 1}";
                    txtField.Text = $"Field{i + 1}";
                }

                txtDisplayNames[i] = txtName;
                txtFieldNames[i] = txtField;
                dsPanel.Controls.Add(lblNum);
                dsPanel.Controls.Add(lblName);
                dsPanel.Controls.Add(txtName);
                dsPanel.Controls.Add(lblField);
                dsPanel.Controls.Add(txtField);
            }
        }

        private void SaveSettings()
        {
            int count = (int)numDsCount.Value;
            Configs = new DataSourceConfig[count];
            for (int i = 0; i < count; i++)
            {
                Configs[i] = new DataSourceConfig
                {
                    DisplayName = txtDisplayNames[i]?.Text?.Trim() ?? $"数据源{i + 1}",
                    FieldName = txtFieldNames[i]?.Text?.Trim() ?? $"Field{i + 1}"
                };
            }
            SaveConfigValue("Printer", cmbPrinter.SelectedItem?.ToString() ?? "");
            SaveConfigValue("Copies", numCopies.Value.ToString());
        }

        private string GetConfigValue(string key)
        {
            try
            {
                if (!File.Exists(_configFile)) return "";
                var json = File.ReadAllText(_configFile);
                var config = ParseJson(json);
                return config.ContainsKey(key) ? config[key] : "";
            }
            catch { return ""; }
        }

        private void SaveConfigValue(string key, string value)
        {
            try
            {
                var dir = Path.GetDirectoryName(_configFile);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                Dictionary<string, string> config;
                if (File.Exists(_configFile))
                    config = ParseJson(File.ReadAllText(_configFile));
                else
                    config = new Dictionary<string, string>();
                config[key] = value;
                File.WriteAllText(_configFile, SerializeJson(config));
            }
            catch { }
        }

        private static Dictionary<string, string> ParseJson(string json)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrWhiteSpace(json)) return result;
            json = json.Trim();
            if (json.StartsWith("{")) json = json.Substring(1);
            if (json.EndsWith("}")) json = json.Substring(0, json.Length - 1);
            int depth = 0, start = 0;
            bool inString = false;
            var pairs = new List<string>();
            for (int i = 0; i < json.Length; i++)
            {
                var c = json[i];
                if (c == '"') inString = !inString;
                if (inString) continue;
                if (c == '{' || c == '[') depth++;
                if (c == '}' || c == ']') depth--;
                if (c == ',' && depth == 0)
                {
                    pairs.Add(json.Substring(start, i - start));
                    start = i + 1;
                }
            }
            pairs.Add(json.Substring(start));
            foreach (var pair in pairs)
            {
                var colonIdx = pair.IndexOf(':');
                if (colonIdx < 0) continue;
                var k = pair.Substring(0, colonIdx).Trim().Trim('"');
                var v = pair.Substring(colonIdx + 1).Trim().Trim('"');
                result[k] = v;
            }
            return result;
        }

        private static string SerializeJson(Dictionary<string, string> dict)
        {
            var pairs = dict.Select(kv => $"\"{kv.Key}\": \"{kv.Value?.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"");
            return "{\n  " + string.Join(",\n  ", pairs) + "\n}";
        }
    }
}
