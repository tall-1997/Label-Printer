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
        private readonly string _version = "v4.8.0";

        private List<DataSourceItem> _dataSources = new List<DataSourceItem>();
        private TextBox[] _inputTextBoxes = new TextBox[0];
        private string _templatesFolder = "";
        private string _selectedTemplatePath = "";
        private string _previewTempFile = null;
        private HashSet<string> _localData = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string _localDataPath = "";
        private bool _useLocalDataValidation = false;

        public MainForm()
        {
            InitializeComponent();
            _configFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".bartender-printer", "config.ini");
            Text = $"BarTender 标签打印工具 {_version}";
            MiuiTheme.ApplyTheme(this);
            Load += MainForm_Load;
            FormClosing += (s, e) => { CleanupPreview(); _btService.Dispose(); };
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            LoadConfig(_configFile);
            PopulateTemplateList(_templatesFolder);
            RebuildInputFields();
            _history.Load();
            LoadHistory();
            RefreshPrinters();
            RefreshStats();
            InitBarTender();
            AddLog("系统启动完成", "INFO");
        }

        #region BarTender

        private void InitBarTender()
        {
            if (_btService.Connect())
            {
                SetStatus("BarTender 已连接");
                AddLog("BarTender 已连接", "SUCCESS");
                btnPrint.Enabled = true;
                btnPrint.Text = "打印";
            }
            else
            {
                if (_btService.IsOfflineMode)
                {
                    SetStatus("离线模式 - BarTender 未安装");
                    AddLog("BarTender 未安装，进入离线模式", "WARNING");
                    btnPrint.Enabled = false;
                    btnPrint.Text = "打印（需要安装 BarTender）";
                }
                else
                {
                    SetStatus("BarTender 连接失败");
                    AddLog("BarTender 连接失败", "ERROR");
                }
            }
        }

        #endregion

        #region Printer & Copies

        private void RefreshPrinters()
        {
            cmbPrinter.Items.Clear();
            foreach (var p in _btService.GetPrinters())
                cmbPrinter.Items.Add(p);
            var saved = IniReadValue("General", "Printer", _configFile);
            if (!string.IsNullOrEmpty(saved) && cmbPrinter.Items.Contains(saved))
                cmbPrinter.SelectedItem = saved;
            else if (cmbPrinter.Items.Count > 0)
                cmbPrinter.SelectedIndex = 0;
        }

        private void btnRefreshPrinter_Click(object sender, EventArgs e) => RefreshPrinters();

        #endregion

        #region Template

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
            cmbTemplate.Items.Clear();
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            { lblSelectedTemplate.Text = "未找到模板"; _selectedTemplatePath = ""; return; }
            foreach (var f in Directory.GetFiles(folder, "*.btw"))
                cmbTemplate.Items.Add(new TemplateItem(Path.GetFileName(f), f));
            if (cmbTemplate.Items.Count > 0) cmbTemplate.SelectedIndex = 0;
            else { lblSelectedTemplate.Text = "未找到模板"; _selectedTemplatePath = ""; }
        }

        private void cmbTemplate_SelectedIndexChanged(object sender, EventArgs e)
        {
            var item = cmbTemplate.SelectedItem as TemplateItem;
            if (item == null) return;
            _selectedTemplatePath = item.FullPath;
            lblSelectedTemplate.Text = item.Name;
            LoadTemplatePreview(_selectedTemplatePath);
            LoadTemplateDataSources(_selectedTemplatePath);
        }

        private void LoadTemplatePreview(string path)
        {
            CleanupPreview(); pictureBoxPreview.Image = null;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            if (!_btService.IsConnected)
            {
                // Offline mode: show placeholder
                var bmp = new Bitmap(145, 50);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.WhiteSmoke);
                    using (var font = new Font("Microsoft YaHei UI", 8F))
                    using (var brush = new SolidBrush(Color.Gray))
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        g.DrawString("预览需要 BarTender", font, brush, new RectangleF(0, 0, 145, 50), sf);
                }
                pictureBoxPreview.Image = bmp;
                return;
            }
            Task.Run(() =>
            {
                var tmp = _btService.ExportPreview(path);
                BeginInvoke((Action)(() =>
                {
                    if (tmp != null && File.Exists(tmp))
                    {
                        try { _previewTempFile = tmp; pictureBoxPreview.Image = Image.FromFile(tmp); }
                        catch { }
                    }
                }));
            });
        }

        private void LoadTemplateDataSources(string path)
        {
            if (!_btService.IsConnected)
            {
                AddLog("离线模式：请手动配置数据源（点击 编辑数据源 按钮）", "INFO");
                return;
            }
            Task.Run(() =>
            {
                var names = _btService.GetTemplateDataSources(path);
                BeginInvoke((Action)(() =>
                {
                    if (names.Count == 0) return;
                    var dlg = new DataSourceSelectDialog(names, _dataSources);
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        _dataSources = dlg.SelectedSources;
                        RebuildInputFields();
                        AddLog($"已加载 {names.Count} 个数据源，选择了 {_dataSources.Count} 个", "SUCCESS");
                    }
                }));
            });
        }

        private void CleanupPreview()
        {
            if (_previewTempFile != null && File.Exists(_previewTempFile))
            { try { File.Delete(_previewTempFile); } catch { } _previewTempFile = null; }
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
                // Offline mode or no template: show manual input dialog
                var result = PromptForManualDataSources();
                if (result != null && result.Count > 0)
                    fields = result;
                else
                    fields = new List<string> { "IMEI1" };
            }
            var dlg = new DataSourceSelectDialog(fields, _dataSources);
            if (dlg.ShowDialog(this) == DialogResult.OK) { _dataSources = dlg.SelectedSources; RebuildInputFields(); }
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

                if (f.ShowDialog(this) == DialogResult.OK)
                    return txt.Text.Split('\n').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();
                return null;
            }
        }

        private void RebuildInputFields()
        {
            inputPanel.Controls.Clear();
            var enabled = _dataSources.Where(d => d.Enabled).ToList();
            _inputTextBoxes = new TextBox[enabled.Count];
            int y = 8;
            for (int i = 0; i < enabled.Count; i++)
            {
                var lbl = new Label { Text = enabled[i].Name + "：", Location = new Point(8, y + 3), Size = new Size(85, 20), TextAlign = ContentAlignment.MiddleRight };
                MiuiTheme.StyleLabel(lbl);
                var txt = new TextBox { Location = new Point(98, y), Size = new Size(inputPanel.Width - 110, 25), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
                MiuiTheme.StyleTextBox(txt); txt.Tag = i; txt.KeyDown += Input_KeyDown;
                inputPanel.Controls.Add(lbl); inputPanel.Controls.Add(txt);
                _inputTextBoxes[i] = txt; y += 32;
            }
        }

        private void Input_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            int idx = (int)((TextBox)sender).Tag;
            if (idx < _inputTextBoxes.Length - 1)
            { _inputTextBoxes[idx + 1].Focus(); _inputTextBoxes[idx + 1].SelectAll(); }
            else DoPrint();
        }

        private void ClearInputs()
        {
            foreach (var tb in _inputTextBoxes) if (tb != null) tb.Text = "";
            if (_inputTextBoxes.Length > 0) { _inputTextBoxes[0].Focus(); _inputTextBoxes[0].SelectAll(); }
        }

        private void SetInputsReadOnly(bool ro)
        {
            foreach (var tb in _inputTextBoxes)
                if (tb != null) { tb.ReadOnly = ro; tb.BackColor = ro ? SystemColors.Control : MiuiTheme.InputBackground; }
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
                        if (ext == ".csv")
                            LoadCsvData(ofd.FileName);
                        else if (ext == ".xlsx" || ext == ".xls")
                            LoadExcelData(ofd.FileName);
                        else
                            LoadTextData(ofd.FileName);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, $"加载失败: {ex.Message}");
                    }
                }
            }
        }

        private void LoadCsvData(string path)
        {
            var lines = File.ReadAllLines(path);
            if (lines.Length < 2) { MessageBox.Show(this, "CSV 文件为空或无数据行"); return; }

            var headers = ParseCsvLine(lines[0]);
            if (headers.Count <= 1)
            {
                // Single column, load directly
                _localData.Clear();
                foreach (var line in lines.Skip(1))
                {
                    var val = line.Trim();
                    if (!string.IsNullOrEmpty(val)) _localData.Add(val);
                }
                _localDataPath = path;
                _useLocalDataValidation = true;
                chkUseLocalData.Checked = true;
                lblLocalData.Text = $"已加载: {_localData.Count} 条 ({Path.GetFileName(path)})";
                AddLog($"加载本地数据: {_localData.Count} 条", "SUCCESS");
                return;
            }

            // Multiple columns - show column selection
            var colIdx = PromptForColumnSelection(headers, Path.GetFileName(path));
            if (colIdx < 0) return;

            _localData.Clear();
            foreach (var line in lines.Skip(1))
            {
                var cols = ParseCsvLine(line);
                if (colIdx < cols.Count && !string.IsNullOrWhiteSpace(cols[colIdx]))
                    _localData.Add(cols[colIdx].Trim());
            }
            _localDataPath = path;
            _useLocalDataValidation = true;
            chkUseLocalData.Checked = true;
            lblLocalData.Text = $"已加载: {_localData.Count} 条 [{headers[colIdx]}] ({Path.GetFileName(path)})";
            AddLog($"加载本地数据: {_localData.Count} 条, 列: {headers[colIdx]}", "SUCCESS");
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

                        // Read all data at once using Value2 (much faster)
                        dynamic allData = usedRange.Value2;

                        // Read headers from first row
                        var headers = new List<string>();
                        for (int c = 1; c <= cols; c++)
                        {
                            var val = allData[1, c]?.ToString()?.Trim() ?? $"列{c}";
                            headers.Add(val);
                        }

                        int colIdx = 0;
                        if (headers.Count > 1)
                        {
                            // Need to show column selection on UI thread
                            colIdx = -1;
                            // Show column selection dialog synchronously
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

                        // Read data from selected column
                        var data = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        for (int r = 2; r <= rows; r++)
                        {
                            var val = allData[r, colIdx + 1]?.ToString()?.Trim();
                            if (!string.IsNullOrEmpty(val)) data.Add(val);
                        }

                        wb.Close(false);
                        excel.Quit();

                        BeginInvoke((Action)(() =>
                        {
                            _localData = data;
                            _localDataPath = path;
                            _useLocalDataValidation = true;
                            chkUseLocalData.Checked = true;
                            lblLocalData.Text = $"已加载: {data.Count} 条 [{headers[colIdx]}] ({Path.GetFileName(path)})";
                            AddLog($"加载 Excel: {data.Count} 条, 列: {headers[colIdx]}", "SUCCESS");
                            SetStatus("就绪");
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
                    BeginInvoke((Action)(() =>
                    {
                        MessageBox.Show(this, $"读取 Excel 失败: {ex.Message}\n\n请保存为 CSV 格式后加载");
                        SetStatus("就绪");
                    }));
                }
            });
        }

        private void LoadTextData(string path)
        {
            _localData.Clear();
            foreach (var line in File.ReadAllLines(path))
            {
                var val = line.Trim();
                if (!string.IsNullOrEmpty(val)) _localData.Add(val);
            }
            _localDataPath = path;
            _useLocalDataValidation = true;
            chkUseLocalData.Checked = true;
            lblLocalData.Text = $"已加载: {_localData.Count} 条 ({Path.GetFileName(path)})";
            AddLog($"加载本地数据: {_localData.Count} 条", "SUCCESS");
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
        }

        private bool ValidateLocalData(Dictionary<string, string> fieldValues)
        {
            if (!_useLocalDataValidation || _localData.Count == 0) return true;

            var notInLocal = new List<string>();
            foreach (var kv in fieldValues)
            {
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

        private void btnPrint_Click(object sender, EventArgs e) => DoPrint();

        private void DoPrint()
        {
            if (string.IsNullOrEmpty(_selectedTemplatePath) || !File.Exists(_selectedTemplatePath))
            { MessageBox.Show(this, "请先选择模板文件"); return; }
            if (!_btService.IsConnected)
            {
                if (_btService.IsOfflineMode)
                    MessageBox.Show(this, "BarTender 未安装，打印功能不可用。\n\n请安装 BarTender 后重新启动程序。", "离线模式");
                else
                    AddLog("BarTender 未连接", "ERROR");
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
                if (string.IsNullOrEmpty(val))
                { MessageBox.Show(this, $"\"{enabled[i].Name}\" 不能为空"); _inputTextBoxes[i]?.Focus(); return; }
                fieldValues[enabled[i].Field] = val;
            }

            // Duplicate check
            var duplicates = fieldValues.Where(kv => _history.IsPrinted(kv.Value)).Select(kv => $"{kv.Key}={kv.Value}").ToList();
            if (duplicates.Count > 0)
            {
                if (MessageBox.Show(this, $"以下数据已打印过：\n{string.Join("\n", duplicates)}\n\n是否继续？", "数据重复",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                { AddLog("用户取消（数据重复）", "WARNING"); return; }
            }

            // Local data validation
            if (!ValidateLocalData(fieldValues))
            {
                AddLog("用户取消（本地数据校验失败）", "WARNING");
                return;
            }

            int copies = (int)numCopies.Value;
            var historyKey = string.Join("|", fieldValues.Values);
            SetStatus("打印中..."); SetInputsReadOnly(true); btnPrint.Enabled = false;
            AddLog($"打印: {string.Join(", ", fieldValues.Select(kv => $"{kv.Key}={kv.Value}"))}", "INFO");

            Task.Run(() =>
            {
                var result = _btService.Print(_selectedTemplatePath, fieldValues, printer, copies);
                BeginInvoke((Action)(() =>
                {
                    if (result.Success)
                    { SetStatus("打印完成"); AddLog("打印完成", "SUCCESS"); _history.Add(historyKey, "PASS"); ClearInputs(); }
                    else
                    { SetStatus("打印失败"); AddLog($"失败: {result.ErrorMessage}", "ERROR"); _history.Add(historyKey, "FAIL"); }
                    SetInputsReadOnly(false); btnPrint.Enabled = true; RefreshStats();
                }));
            });
        }

        #endregion

        #region History

        private void txtSearch_TextChanged(object sender, EventArgs e) => LoadHistory();
        private void btnClearSearch_Click(object sender, EventArgs e) { txtSearch.Text = ""; }
        private void btnClearHistory_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show(this, "确定清空所有记录？", "确认", MessageBoxButtons.YesNo) == DialogResult.Yes)
            { _history.Clear(); LoadHistory(); RefreshStats(); }
        }
        private void btnExportHistory_Click(object sender, EventArgs e)
        {
            if (_history.Records.Count == 0) { MessageBox.Show(this, "没有记录"); return; }
            using (var sfd = new SaveFileDialog { Filter = "CSV|*.csv", FileName = $"records_{DateTime.Now:yyyyMMdd_HHmmss}.csv" })
            {
                if (sfd.ShowDialog(this) == DialogResult.OK)
                { try { _history.Export(sfd.FileName, txtSearch?.Text?.Trim() ?? ""); MessageBox.Show(this, "导出成功"); } catch (Exception ex) { MessageBox.Show(this, ex.Message); } }
            }
        }
        private void LoadHistory()
        {
            dgvHistory.DataSource = null;
            var dt = new DataTable(); dt.Columns.Add("IMEI"); dt.Columns.Add("打印时间"); dt.Columns.Add("状态");
            var kw = txtSearch?.Text?.Trim().ToLower() ?? "";
            foreach (var r in _history.Records.AsEnumerable().Reverse())
            {
                if (!string.IsNullOrEmpty(kw) && !r.Imei.ToLower().Contains(kw) && !r.PrintTime.ToLower().Contains(kw) && !r.Status.ToLower().Contains(kw)) continue;
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

        #region Config

        private void btnSaveConfig_Click(object sender, EventArgs e)
        { SaveConfig(); MessageBox.Show(this, "配置已保存"); AddLog("配置已保存", "SUCCESS"); }
        private void btnLoadConfig_Click(object sender, EventArgs e)
        { LoadConfig(_configFile); PopulateTemplateList(_templatesFolder); RebuildInputFields(); MessageBox.Show(this, "配置已加载"); }

        private void SaveConfig()
        {
            var dir = Path.GetDirectoryName(_configFile); if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            IniWriteValue("General", "TemplatesFolder", _templatesFolder ?? "", _configFile);
            IniWriteValue("General", "Printer", cmbPrinter.SelectedItem?.ToString() ?? "", _configFile);
            IniWriteValue("General", "Copies", numCopies.Value.ToString(), _configFile);
            IniWriteValue("General", "DSCount", _dataSources.Count.ToString(), _configFile);
            for (int i = 0; i < _dataSources.Count; i++)
            {
                IniWriteValue($"DS{i}", "Name", _dataSources[i].Name, _configFile);
                IniWriteValue($"DS{i}", "Field", _dataSources[i].Field, _configFile);
                IniWriteValue($"DS{i}", "Enabled", _dataSources[i].Enabled.ToString(), _configFile);
            }
        }

        private void LoadConfig(string path)
        {
            _templatesFolder = IniReadValue("General", "TemplatesFolder", path);
            if (string.IsNullOrWhiteSpace(_templatesFolder)) _templatesFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "templates");
            txtTemplateDir.Text = _templatesFolder;
            var copies = 1; int.TryParse(IniReadValue("General", "Copies", path), out copies); numCopies.Value = Math.Max(1, Math.Min(99, copies));
            int.TryParse(IniReadValue("General", "DSCount", path), out int count);
            _dataSources = new List<DataSourceItem>();
            for (int i = 0; i < count; i++)
            {
                var en = true; bool.TryParse(IniReadValue($"DS{i}", "Enabled", path), out en);
                _dataSources.Add(new DataSourceItem { Name = IniReadValue($"DS{i}", "Name", path), Field = IniReadValue($"DS{i}", "Field", path), Enabled = en });
            }
            if (_dataSources.Count == 0) _dataSources.Add(new DataSourceItem { Name = "IMEI", Field = "IMEI1", Enabled = true });
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

    public class DataSourceSelectDialog : Form
    {
        public List<DataSourceItem> SelectedSources { get; private set; }
        private CheckBox[] _cbs; private TextBox[] _names;
        private readonly List<string> _fields; private readonly List<DataSourceItem> _current;

        public DataSourceSelectDialog(List<string> fields, List<DataSourceItem> current)
        {
            _fields = fields; _current = current;
            Text = "选择数据源"; Size = new Size(440, 380);
            FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false;

            var lbl = new Label { Text = "勾选需要的数据源并设置显示名称：", Location = new Point(10, 10), Size = new Size(400, 20) };
            var panel = new Panel { Location = new Point(10, 35), Size = new Size(405, 260), AutoScroll = true, BorderStyle = BorderStyle.FixedSingle };
            _cbs = new CheckBox[fields.Count]; _names = new TextBox[fields.Count];
            for (int i = 0; i < fields.Count; i++)
            {
                int y = i * 30 + 5; var ex = current.FirstOrDefault(d => d.Field == fields[i]);
                _cbs[i] = new CheckBox { Text = fields[i], Location = new Point(5, y + 2), Size = new Size(115, 20), Checked = ex?.Enabled ?? false };
                _names[i] = new TextBox { Location = new Point(125, y), Size = new Size(265, 25), Text = ex?.Name ?? fields[i] };
                panel.Controls.Add(_cbs[i]); panel.Controls.Add(_names[i]);
            }
            var ok = new Button { Text = "确定", Location = new Point(250, 305), Size = new Size(75, 28), DialogResult = DialogResult.OK };
            ok.Click += (s, e) => { SelectedSources = new List<DataSourceItem>(); for (int i = 0; i < _fields.Count; i++) if (_cbs[i].Checked) SelectedSources.Add(new DataSourceItem { Name = _names[i].Text?.Trim() ?? _fields[i], Field = _fields[i], Enabled = true }); };
            var cancel = new Button { Text = "取消", Location = new Point(340, 305), Size = new Size(75, 28), DialogResult = DialogResult.Cancel };
            Controls.AddRange(new Control[] { lbl, panel, ok, cancel }); AcceptButton = ok; CancelButton = cancel;
        }
    }

    public class DataSourceItem
    {
        public string Name { get; set; }
        public string Field { get; set; }
        public bool Enabled { get; set; }
    }
}
