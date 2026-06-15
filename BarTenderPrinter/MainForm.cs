using System;
using System.Collections.Generic;
using System.Data;
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
        private List<string> _excelData = new List<string>();
        private readonly string _configFile;
        private readonly string _version = "v3.0.0";

        public MainForm()
        {
            InitializeComponent();
            _configFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".bartender-printer", "config.json");
            Text = $"BarTender 标签打印工具 {_version}";
            titleLabel.Text = $"BarTender 标签打印工具 {_version}";
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            LoadConfig();
            LoadHistory();
            RefreshStats();
            InitBarTender();
            LoadPrinters();
            if (!string.IsNullOrEmpty(txtExcel.Text) && File.Exists(txtExcel.Text))
            {
                LoadExcelData();
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            SaveConfig();
            _btService.Dispose();
        }

        #region BarTender

        private void InitBarTender()
        {
            LoggerService.Info("正在初始化 BarTender...");
            if (_btService.Connect())
            {
                lblStatus.Text = "BarTender 已连接";
            }
            else
            {
                lblStatus.Text = "BarTender 连接失败";
                AppendStatus("BarTender 连接失败，请检查是否已安装", true);
            }
        }

        private void LoadPrinters()
        {
            var printers = _btService.GetPrinters();
            cmbPrinter.Items.Clear();
            foreach (var p in printers)
            {
                cmbPrinter.Items.Add(p);
            }
            if (cmbPrinter.Items.Count > 0)
            {
                var savedPrinter = GetConfigString("Printer");
                var idx = cmbPrinter.Items.IndexOf(savedPrinter);
                cmbPrinter.SelectedIndex = idx >= 0 ? idx : 0;
            }
        }

        #endregion

        #region Print

        private void btnPrint_Click(object sender, EventArgs e)
        {
            using (var dialog = new ImeiInputDialog())
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    var imei = dialog.ImeiValue;
                    if (!string.IsNullOrWhiteSpace(imei))
                    {
                        ProcessImei(new List<string> { imei.Trim() });
                    }
                }
            }
        }

        private void btnImportFile_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Filter = "文本文件|*.txt|CSV 文件|*.csv|所有文件|*.*";
                ofd.Title = "选择 IMEI 文件";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        var lines = File.ReadAllLines(ofd.FileName)
                            .Where(l => !string.IsNullOrWhiteSpace(l))
                            .Select(l => l.Trim())
                            .ToList();
                        if (lines.Count > 0)
                        {
                            ProcessImei(lines);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"文件读取失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void ProcessImei(List<string> imeiList)
        {
            var printer = cmbPrinter.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(printer))
            {
                AppendStatus("错误：请选择打印机", true);
                return;
            }
            var templatePath = txtTemplate.Text;
            if (string.IsNullOrEmpty(templatePath) || !File.Exists(templatePath))
            {
                AppendStatus("错误：请选择有效的 BarTender 模板文件", true);
                return;
            }
            if (!_btService.IsConnected)
            {
                AppendStatus("错误：BarTender 未连接", true);
                return;
            }
            var datasource = txtDatasource.Text;
            if (string.IsNullOrEmpty(datasource)) datasource = "IMEI1";
            int copies = (int)numCopies.Value;

            // Excel verification
            if (chkVerifyExcel.Checked && _excelData.Count > 0)
            {
                var invalid = imeiList.Where(i => !_excelData.Contains(i)).ToList();
                if (invalid.Count > 0)
                {
                    var msg = $"发现 {invalid.Count} 个 IMEI 不在 Excel 数据中！\n\n" +
                              $"无效 IMEI:\n{string.Join("\n", invalid.Take(5))}\n\n" +
                              "是：继续打印 / 否：跳过无效 / 取消：取消打印";
                    var result = MessageBox.Show(msg, "数据不在文件中", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                    if (result == DialogResult.Cancel) return;
                    if (result == DialogResult.No)
                    {
                        imeiList = imeiList.Where(i => _excelData.Contains(i)).ToList();
                        if (imeiList.Count == 0) { AppendStatus("没有有效的 IMEI", true); return; }
                    }
                }
            }

            // Duplicate check
            var printed = imeiList.Where(i => _history.IsPrinted(i)).ToList();
            if (printed.Count > 0)
            {
                var msg = $"发现 {printed.Count} 个 IMEI 已打印过！\n\n" +
                          $"已打印:\n{string.Join("\n", printed.Take(5))}\n\n" +
                          "是：继续打印 / 否：跳过已打印 / 取消：取消打印";
                var result = MessageBox.Show(msg, "数据重复", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                if (result == DialogResult.Cancel) return;
                if (result == DialogResult.No)
                {
                    imeiList = imeiList.Where(i => !_history.IsPrinted(i)).ToList();
                    if (imeiList.Count == 0) { AppendStatus("所有 IMEI 都已打印过", true); return; }
                }
            }

            // Print
            ClearStatus();
            AppendStatus($"开始打印 {imeiList.Count} 个 IMEI...");
            lblStatus.Text = "打印中...";

            Task.Run(() =>
            {
                int ok = 0, fail = 0;
                foreach (var imei in imeiList)
                {
                    var result = _btService.Print(templatePath, datasource, imei, printer, copies);
                    if (result.Success)
                    {
                        _history.Add(imei, "PASS");
                        ok++;
                        this.Invoke((Action)(() => AppendStatus($"PASS {imei}")));
                    }
                    else
                    {
                        _history.Add(imei, "FAIL");
                        fail++;
                        this.Invoke((Action)(() => AppendStatus($"FAIL {imei} - {result.ErrorMessage}", true)));
                    }
                }
                this.Invoke((Action)(() =>
                {
                    AppendStatus($"\n完成：成功 {ok}，失败 {fail}");
                    lblStatus.Text = $"完成：成功 {ok}，失败 {fail}";
                    LoadHistory();
                    RefreshStats();
                }));
            });
        }

        #endregion

        #region History

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
                if (!string.IsNullOrEmpty(keyword) &&
                    !r.Imei.ToLower().Contains(keyword) &&
                    !r.PrintTime.ToLower().Contains(keyword) &&
                    !r.Status.ToLower().Contains(keyword))
                    continue;
                dt.Rows.Add(r.Imei, r.PrintTime, r.Status);
            }
            dgvHistory.DataSource = dt;
        }

        private void RefreshStats()
        {
            lblTodayCount.Text = _history.TodayCount().ToString();
            lblTotalCount.Text = _history.TotalCount().ToString();
        }

        private void txtSearch_TextChanged(object sender, EventArgs e)
        {
            LoadHistory();
        }

        private void btnClearSearch_Click(object sender, EventArgs e)
        {
            txtSearch.Text = "";
        }

        private void btnClearHistory_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("确定要清空所有记录吗？", "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _history.Clear();
                LoadHistory();
                RefreshStats();
            }
        }

        private void btnExportHistory_Click(object sender, EventArgs e)
        {
            if (_history.Records.Count == 0)
            {
                MessageBox.Show("没有可导出的历史记录", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (var sfd = new SaveFileDialog())
            {
                sfd.Filter = "CSV 文件|*.csv|所有文件|*.*";
                sfd.FileName = $"print_records_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        _history.Export(sfd.FileName, txtSearch?.Text?.Trim() ?? "");
                        MessageBox.Show($"已导出记录到:\n{sfd.FileName}", "导出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"导出失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        #endregion

        #region Template & Excel

        private void btnBrowseTemplate_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Filter = "BarTender 文件|*.btw|所有文件|*.*";
                ofd.Title = "选择 BarTender 模板";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    txtTemplate.Text = ofd.FileName;
                    SaveConfig();
                }
            }
        }

        private void btnBrowseExcel_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Filter = "Excel 文件|*.xlsx;*.xls|CSV 文件|*.csv|所有文件|*.*";
                ofd.Title = "选择 Excel 文件";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    txtExcel.Text = ofd.FileName;
                    SaveConfig();
                    LoadExcelData();
                }
            }
        }

        private void LoadExcelData()
        {
            var path = txtExcel.Text;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                _excelData = new List<string>();
                lblExcelCount.Text = "已加载：0 条";
                return;
            }
            try
            {
                var col = txtExcelCol.Text.Trim();
                if (string.IsNullOrEmpty(col)) { _excelData = new List<string>(); return; }
                if (path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                {
                    var lines = File.ReadAllLines(path);
                    if (lines.Length < 2) { _excelData = new List<string>(); return; }
                    var headers = lines[0].Split(',').Select(h => h.Trim().Trim('"')).ToList();
                    var colIdx = headers.IndexOf(col);
                    if (colIdx < 0) { _excelData = new List<string>(); return; }
                    _excelData = lines.Skip(1)
                        .Select(l => l.Split(','))
                        .Where(parts => colIdx < parts.Length)
                        .Select(parts => parts[colIdx].Trim().Trim('"'))
                        .Where(v => !string.IsNullOrEmpty(v))
                        .ToList();
                }
                lblExcelCount.Text = $"已加载：{_excelData.Count} 条";
            }
            catch (Exception ex)
            {
                LoggerService.Error("加载 Excel 数据失败", ex);
                _excelData = new List<string>();
                lblExcelCount.Text = "已加载：0 条";
            }
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
                if (config.ContainsKey("TemplatePath")) txtTemplate.Text = config["TemplatePath"];
                if (config.ContainsKey("Datasource")) txtDatasource.Text = config["Datasource"];
                if (config.ContainsKey("ExcelPath")) txtExcel.Text = config["ExcelPath"];
                if (config.ContainsKey("ExcelColumn")) txtExcelCol.Text = config["ExcelColumn"];
                if (config.ContainsKey("Copies")) numCopies.Value = Math.Max(1, Math.Min(99, int.Parse(config["Copies"])));
                if (config.ContainsKey("VerifyExcel")) chkVerifyExcel.Checked = bool.Parse(config["VerifyExcel"]);
            }
            catch (Exception ex)
            {
                LoggerService.Error("加载配置失败", ex);
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
                    ["TemplatePath"] = txtTemplate.Text ?? "",
                    ["Datasource"] = txtDatasource.Text ?? "IMEI1",
                    ["ExcelPath"] = txtExcel.Text ?? "",
                    ["ExcelColumn"] = txtExcelCol.Text ?? "IMEI1",
                    ["Printer"] = cmbPrinter.SelectedItem?.ToString() ?? "",
                    ["Copies"] = numCopies.Value.ToString(),
                    ["VerifyExcel"] = chkVerifyExcel.Checked.ToString()
                };
                File.WriteAllText(_configFile, SerializeJson(config));
            }
            catch (Exception ex)
            {
                LoggerService.Error("保存配置失败", ex);
            }
        }

        private string GetConfigString(string key)
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

        #endregion

        #region Settings & Log

        private void btnRefreshPrinter_Click(object sender, EventArgs e)
        {
            LoadPrinters();
        }

        private void btnSettings_Click(object sender, EventArgs e)
        {
            using (var dialog = new SettingsDialog())
            {
                dialog.Datasource = txtDatasource.Text;
                dialog.VerifyExcel = chkVerifyExcel.Checked;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    txtDatasource.Text = dialog.Datasource;
                    chkVerifyExcel.Checked = dialog.VerifyExcel;
                    SaveConfig();
                }
            }
        }

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
                sfd.Filter = "日志文件|*.log|文本文件|*.txt|所有文件|*.*";
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
            txtStatus.AppendText($"{prefix}{msg}{Environment.NewLine}");
            LoggerService.Info(msg);
        }

        private void ClearStatus()
        {
            txtStatus.Clear();
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
            int depth = 0;
            int start = 0;
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
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        #endregion
    }

    // IMEI Input Dialog
    public class ImeiInputDialog : Form
    {
        public string ImeiValue { get; private set; }
        private TextBox txtImei;

        public ImeiInputDialog()
        {
            Text = "输入 IMEI";
            Size = new System.Drawing.Size(420, 180);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;

            var lbl = new Label { Text = "扫码或输入 IMEI：", Location = new System.Drawing.Point(15, 15), Size = new System.Drawing.Size(380, 20) };
            txtImei = new TextBox { Location = new System.Drawing.Point(15, 40), Size = new System.Drawing.Size(375, 25) };
            txtImei.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    ImeiValue = txtImei.Text;
                    DialogResult = DialogResult.OK;
                    Close();
                }
            };

            var btnOk = new Button { Text = "打印", Location = new System.Drawing.Point(230, 80), Size = new System.Drawing.Size(75, 28) };
            btnOk.Click += (s, e) => { ImeiValue = txtImei.Text; DialogResult = DialogResult.OK; Close(); };
            var btnCancel = new Button { Text = "取消", Location = new System.Drawing.Point(315, 80), Size = new System.Drawing.Size(75, 28), DialogResult = DialogResult.Cancel };

            Controls.Add(lbl);
            Controls.Add(txtImei);
            Controls.Add(btnOk);
            Controls.Add(btnCancel);
            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            txtImei.Focus();
        }
    }

    // Settings Dialog
    public class SettingsDialog : Form
    {
        public string Datasource { get; set; }
        public bool VerifyExcel { get; set; }
        private TextBox txtDs;
        private CheckBox chkVerify;

        public SettingsDialog()
        {
            Text = "设置";
            Size = new System.Drawing.Size(400, 200);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;

            var lblDs = new Label { Text = "数据源名称：", Location = new System.Drawing.Point(15, 20), Size = new System.Drawing.Size(90, 20) };
            txtDs = new TextBox { Location = new System.Drawing.Point(110, 17), Size = new System.Drawing.Size(250, 25) };
            chkVerify = new CheckBox { Text = "打印前校验 Excel 数据", Location = new System.Drawing.Point(15, 55), Size = new System.Drawing.Size(200, 22) };

            var btnOk = new Button { Text = "确定", Location = new System.Drawing.Point(205, 100), Size = new System.Drawing.Size(75, 28), DialogResult = DialogResult.OK };
            btnOk.Click += (s, e) => { Datasource = txtDs.Text; VerifyExcel = chkVerify.Checked; };
            var btnCancel = new Button { Text = "取消", Location = new System.Drawing.Point(290, 100), Size = new System.Drawing.Size(75, 28), DialogResult = DialogResult.Cancel };

            Controls.Add(lblDs);
            Controls.Add(txtDs);
            Controls.Add(chkVerify);
            Controls.Add(btnOk);
            Controls.Add(btnCancel);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            txtDs.Text = Datasource;
            chkVerify.Checked = VerifyExcel;
        }
    }
}
