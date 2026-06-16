using System;
using System.Collections.Generic;
using System.IO;

namespace BarTenderPrinter
{
    public class BarTenderService : IDisposable
    {
        private dynamic _btApp;
        private bool _connected;
        private bool _offlineMode;

        public bool IsConnected => _connected;
        public bool IsOfflineMode => _offlineMode;

        public bool Connect()
        {
            LoggerService.Info("正在连接 BarTender...");

            try
            {
                // Step 1: Check if COM is registered
                var comType = Type.GetTypeFromProgID("BarTender.Application");
                if (comType == null)
                {
                    LoggerService.Warn("BarTender COM 未注册 (GetTypeFromProgID 返回 null)");
                    _offlineMode = true;
                    _connected = false;
                    return false;
                }
                LoggerService.Info("BarTender COM 已注册");

                // Step 2: Create COM instance
                _btApp = Activator.CreateInstance(comType);
                if (_btApp == null)
                {
                    LoggerService.Warn("BarTender COM 创建失败");
                    _offlineMode = true;
                    _connected = false;
                    return false;
                }
                LoggerService.Info("BarTender COM 实例创建成功");

                // Step 3: Set visible
                try
                {
                    _btApp.Visible = false;
                    LoggerService.Info("BarTender Visible=False 设置成功");
                }
                catch (Exception ex)
                {
                    LoggerService.Warn($"设置 Visible 失败: {ex.Message}");
                }

                _connected = true;
                _offlineMode = false;
                LoggerService.Info("BarTender 连接成功");
                return true;
            }
            catch (Exception ex)
            {
                LoggerService.Error($"BarTender 连接失败: {ex.Message}");
                _offlineMode = true;
                _connected = false;
                _btApp = null;
                return false;
            }
        }

        public List<string> GetTemplateDataSources(string templatePath)
        {
            var result = new List<string>();
            if (!_connected || _btApp == null) return result;

            dynamic btFormat = null;
            try
            {
                btFormat = _btApp.Formats.Open(templatePath, false, "");
                var subStrings = btFormat.NamedSubStrings;
                var count = (int)subStrings.Count;
                for (int i = 1; i <= count; i++)
                {
                    try
                    {
                        var sub = subStrings.Item(i);
                        var name = (string)sub.Name;
                        if (!string.IsNullOrEmpty(name))
                            result.Add(name);
                    }
                    catch { }
                }
                CloseFormat(btFormat);
            }
            catch (Exception ex)
            {
                LoggerService.Error($"获取数据源失败: {ex.Message}");
                CloseFormat(btFormat);
            }
            return result;
        }

        public string ExportPreview(string templatePath)
        {
            if (!_connected || _btApp == null) return null;

            dynamic btFormat = null;
            try
            {
                btFormat = _btApp.Formats.Open(templatePath, false, "");
                var tempPath = Path.Combine(Path.GetTempPath(), $"bt_preview_{Guid.NewGuid():N}.png");

                // Method 1: ExportImageToClipboard (most reliable in COM mode)
                try
                {
                    btFormat.ExportImageToClipboard(300, 300);
                    var img = System.Windows.Forms.Clipboard.GetImage();
                    if (img != null)
                    {
                        img.Save(tempPath, System.Drawing.Imaging.ImageFormat.Png);
                        img.Dispose();
                        LoggerService.Info("预览成功 (剪贴板方式)");
                        CloseFormat(btFormat);
                        return File.Exists(tempPath) ? tempPath : null;
                    }
                }
                catch (Exception ex) { LoggerService.Debug($"剪贴板方式失败: {ex.Message}"); }

                // Method 2: ExportImageToFile with various parameters
                var methods = new (string name, Action tryFunc)[]
                {
                    ("ExportImageToFile(path)", () => btFormat.ExportImageToFile(tempPath)),
                    ("ExportImageToFile(path, 3)", () => btFormat.ExportImageToFile(tempPath, 3)),
                    ("ExportImageToFile(path, 3, 0)", () => btFormat.ExportImageToFile(tempPath, 3, 0)),
                    ("ExportImageToFile(path, 3, 0, 300)", () => btFormat.ExportImageToFile(tempPath, 3, 0, 300)),
                    ("ExportImageToFile(path, 3, 0, 300, 300)", () => btFormat.ExportImageToFile(tempPath, 3, 0, 300, 300)),
                    ("ExportImageToFile(path, 3, 0, 300, 300, 1)", () => btFormat.ExportImageToFile(tempPath, 3, 0, 300, 300, 1)),
                };

                foreach (var (name, tryFunc) in methods)
                {
                    try
                    {
                        tryFunc();
                        if (File.Exists(tempPath))
                        {
                            LoggerService.Info($"预览成功 ({name})");
                            CloseFormat(btFormat);
                            return tempPath;
                        }
                    }
                    catch (Exception ex) { LoggerService.Debug($"{name} 失败: {ex.Message}"); }
                }

                CloseFormat(btFormat);
                LoggerService.Warn("预览导出失败：所有方式都失败");
                return null;
            }
            catch (Exception ex)
            {
                LoggerService.Error($"预览失败: {ex.Message}");
                CloseFormat(btFormat);
                return null;
            }
        }

        public PrintResult Print(string templatePath, Dictionary<string, string> fieldValues, string printer, int copies)
        {
            if (!_connected || _btApp == null)
                return new PrintResult(false, "BarTender 未连接");

            dynamic btFormat = null;
            try
            {
                LoggerService.Info($"打开模板: {templatePath}");
                btFormat = _btApp.Formats.Open(templatePath, false, "");
                LoggerService.Info("模板打开成功");

                var missing = new List<string>();
                foreach (var kv in fieldValues)
                {
                    try
                    {
                        btFormat.SetNamedSubStringValue(kv.Key, kv.Value);
                        LoggerService.Info($"数据源: {kv.Key}={kv.Value}");
                    }
                    catch { missing.Add(kv.Key); }
                }
                if (missing.Count > 0)
                {
                    CloseFormat(btFormat);
                    return new PrintResult(false, $"模板中未找到字段: {string.Join(", ", missing)}");
                }

                try { btFormat.Printer = printer; LoggerService.Info($"打印机: {printer}"); }
                catch (Exception ex) { LoggerService.Warn($"设置打印机失败: {ex.Message}"); }

                try { btFormat.PrintSetup.IdenticalCopiesOfLabel = copies; LoggerService.Info($"份数: {copies}"); }
                catch (Exception ex) { LoggerService.Warn($"设置份数失败: {ex.Message}"); }

                btFormat.PrintOut(false, false);
                LoggerService.Info("PrintOut 完成");

                CloseFormat(btFormat);
                LoggerService.Info("打印完成");
                return new PrintResult(true, "");
            }
            catch (Exception ex)
            {
                LoggerService.Error($"打印失败: {ex.Message}");
                CloseFormat(btFormat);
                return new PrintResult(false, ex.Message);
            }
        }

        private void CloseFormat(dynamic btFormat)
        {
            if (btFormat == null) return;
            try { btFormat.Close(false); } catch { }
        }

        public string[] GetAvailableTemplates(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return new string[0];
            return Directory.GetFiles(directory, "*.btw", SearchOption.TopDirectoryOnly);
        }

        public string[] GetPrinters()
        {
            try
            {
                var printers = new string[System.Drawing.Printing.PrinterSettings.InstalledPrinters.Count];
                System.Drawing.Printing.PrinterSettings.InstalledPrinters.CopyTo(printers, 0);
                return printers;
            }
            catch { return new string[0]; }
        }

        public void Disconnect()
        {
            try
            {
                if (_btApp != null)
                {
                    try { _btApp.Quit(0); } catch { }
                    try { System.Runtime.InteropServices.Marshal.ReleaseComObject(_btApp); } catch { }
                    _btApp = null;
                }
            }
            catch { }
            _connected = false;
            _offlineMode = false;
        }

        public void Dispose() => Disconnect();
    }

    public class PrintResult
    {
        public bool Success { get; }
        public string ErrorMessage { get; }
        public PrintResult(bool success, string msg) { Success = success; ErrorMessage = msg ?? ""; }
    }
}
