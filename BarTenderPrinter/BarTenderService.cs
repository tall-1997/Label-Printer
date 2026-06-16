using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BarTenderPrinter
{
    public class BarTenderService : IDisposable
    {
        private object _btApp;
        private bool _connected;
        private bool _offlineMode;

        public bool IsConnected => _connected;
        public bool IsOfflineMode => _offlineMode;

        public bool Connect()
        {
            LoggerService.Info("正在连接 BarTender...");

            // Try COM first (most reliable for printing)
            if (TryConnectViaCom())
            {
                _connected = true;
                _offlineMode = false;
                LoggerService.Info("BarTender COM 模式已连接");
                return true;
            }

            // Offline mode
            _connected = false;
            _offlineMode = true;
            LoggerService.Warn("BarTender 未安装，进入离线模式");
            return false;
        }

        private bool TryConnectViaCom()
        {
            try
            {
                var comType = Type.GetTypeFromProgID("BarTender.Application");
                if (comType == null) return false;

                LoggerService.Info("找到 BarTender COM 组件");
                _btApp = Activator.CreateInstance(comType);
                if (_btApp == null) return false;

                try { ((dynamic)_btApp).Visible = false; } catch { }
                return true;
            }
            catch (Exception ex)
            {
                LoggerService.Warn($"COM 连接失败: {ex.Message}");
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
                dynamic btApp = _btApp;
                btFormat = btApp.Formats.Open(templatePath, false, "");

                try
                {
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
                }
                catch { }

                CloseFormat(btFormat);
            }
            catch (Exception ex)
            {
                LoggerService.Error($"获取数据源失败: {ex.Message}");
                CloseFormat(btFormat);
            }
            return result;
        }

        public string ExportPreview(string templatePath, int dpi = 300)
        {
            if (!_connected || _btApp == null) return null;

            dynamic btFormat = null;
            try
            {
                dynamic btApp = _btApp;
                btFormat = btApp.Formats.Open(templatePath, false, "");
                var tempPath = Path.Combine(Path.GetTempPath(), $"bt_preview_{Guid.NewGuid():N}.png");

                try
                {
                    btFormat.ExportImageToFile(tempPath, 3, 0, dpi, dpi, 1);
                }
                catch
                {
                    try { btFormat.ExportImageToFile(tempPath, 3, 0, 0, dpi, dpi); }
                    catch { try { btFormat.ExportImageToFile(tempPath); } catch { } }
                }

                CloseFormat(btFormat);
                return File.Exists(tempPath) ? tempPath : null;
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
                dynamic btApp = _btApp;
                btFormat = btApp.Formats.Open(templatePath, false, "");
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
                return new PrintResult(false, $"COM: {ex.Message}");
            }
        }

        private void CloseFormat(dynamic btFormat)
        {
            if (btFormat == null) return;
            try
            {
                // Try Close(false) - don't save changes
                btFormat.Close(false);
            }
            catch
            {
                try
                {
                    // Try Close(0) - btDoNotSaveChanges
                    btFormat.Close(0);
                }
                catch
                {
                    try
                    {
                        // Try Close() without parameters
                        btFormat.Close();
                    }
                    catch { }
                }
            }
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
                    try { ((dynamic)_btApp).Quit(0); } catch { }
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
