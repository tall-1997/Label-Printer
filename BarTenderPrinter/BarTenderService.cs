using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Runtime.InteropServices;

namespace BarTenderPrinter
{
    public class BarTenderService : IDisposable
    {
        private dynamic _btApp;
        private bool _connected;
        private bool _offlineMode;
        private readonly SemaphoreSlim _operationLock = new SemaphoreSlim(1, 1);
        private DateTime _lastOperationTime = DateTime.MinValue;
        private const int MinOperationIntervalMs = 2000;
        private const int MaxRetries = 3;
        private const int RetryDelayMs = 3000;

        public bool IsConnected => _connected;
        public bool IsOfflineMode => _offlineMode;

        public bool Connect()
        {
            LoggerService.Info("正在连接 BarTender...");

            try
            {
                var comType = Type.GetTypeFromProgID("BarTender.Application");
                if (comType == null)
                {
                    LoggerService.Warn("BarTender COM 未注册 (GetTypeFromProgID 返回 null)");
                    _offlineMode = true;
                    _connected = false;
                    return false;
                }
                LoggerService.Info("BarTender COM 已注册");

                _btApp = Activator.CreateInstance(comType);
                if (_btApp == null)
                {
                    LoggerService.Warn("BarTender COM 创建失败");
                    _offlineMode = true;
                    _connected = false;
                    return false;
                }
                LoggerService.Info("BarTender COM 实例创建成功");

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

            _operationLock.Wait();
            try
            {
                EnsureOperationInterval();
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
            }
            finally
            {
                _operationLock.Release();
            }
            return result;
        }

        public void RunDiagnostics(string templatePath)
        {
            LoggerService.Info("========== BarTender 诊断开始 ==========");
            
            // 1. 检查连接状态
            LoggerService.Info($"[诊断] 连接状态: {(_connected ? "已连接" : "未连接")}");
            LoggerService.Info($"[诊断] COM 对象: {(_btApp != null ? "已创建" : "未创建")}");
            
            if (!_connected || _btApp == null)
            {
                LoggerService.Error("[诊断] BarTender 未连接，无法进行诊断");
                return;
            }

            // 2. 检查模板文件
            LoggerService.Info($"[诊断] 模板路径: {templatePath}");
            LoggerService.Info($"[诊断] 模板存在: {File.Exists(templatePath)}");
            
            if (!File.Exists(templatePath))
            {
                LoggerService.Error("[诊断] 模板文件不存在");
                return;
            }

            // 3. 尝试打开模板
            dynamic btFormat = null;
            try
            {
                LoggerService.Info("[诊断] 尝试打开模板...");
                btFormat = _btApp.Formats.Open(templatePath, false, "");
                LoggerService.Info("[诊断] 模板打开成功");
                
                // 4. 检查模板属性
                try
                {
                    LoggerService.Info($"[诊断] 模板名称: {btFormat.Name}");
                    LoggerService.Info($"[诊断] 模板文件名: {btFormat.FileName}");
                }
                catch (Exception ex)
                {
                    LoggerService.Warn($"[诊断] 获取模板属性失败: {ex.Message}");
                }

                // 5. 检查数据源
                try
                {
                    var subStrings = btFormat.NamedSubStrings;
                    var count = (int)subStrings.Count;
                    LoggerService.Info($"[诊断] 数据源数量: {count}");
                    for (int i = 1; i <= Math.Min(count, 5); i++)
                    {
                        try
                        {
                            var sub = subStrings.Item(i);
                            LoggerService.Info($"[诊断] 数据源 {i}: {sub.Name}");
                        }
                        catch (Exception ex)
                        {
                            LoggerService.Warn($"[诊断] 获取数据源 {i} 失败: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    LoggerService.Warn($"[诊断] 获取数据源列表失败: {ex.Message}");
                }

                // 6. 检查打印机
                try
                {
                    LoggerService.Info($"[诊断] 默认打印机: {btFormat.Printer}");
                }
                catch (Exception ex)
                {
                    LoggerService.Warn($"[诊断] 获取打印机失败: {ex.Message}");
                }

                // 7. 检查 BarTender 版本
                try
                {
                    LoggerService.Info($"[诊断] BarTender 版本: {_btApp.Version}");
                }
                catch (Exception ex)
                {
                    LoggerService.Warn($"[诊断] 获取版本失败: {ex.Message}");
                }

                // 9. 检查许可
                try
                {
                    LoggerService.Info($"[诊断] 许可状态: {_btApp.LicenseStatus}");
                }
                catch (Exception ex)
                {
                    LoggerService.Warn($"[诊断] 获取许可状态失败: {ex.Message}");
                }

                CloseFormat(btFormat);
            }
            catch (Exception ex)
            {
                LoggerService.Error($"[诊断] 打开模板失败: {ex.Message}");
                LoggerService.Error($"[诊断] 异常类型: {ex.GetType().Name}");
                if (ex.InnerException != null)
                {
                    LoggerService.Error($"[诊断] 内部异常: {ex.InnerException.Message}");
                }
                CloseFormat(btFormat);
            }
            
            LoggerService.Info("========== BarTender 诊断结束 ==========");
        }

        public PrintResult Print(string templatePath, Dictionary<string, string> fieldValues, string printer, int copies)
        {
            if (!_connected || _btApp == null)
                return new PrintResult(false, "BarTender 未连接");

            for (int retry = 0; retry < MaxRetries; retry++)
            {
                if (retry > 0)
                {
                    LoggerService.Info($"打印重试 {retry}/{MaxRetries - 1}，等待 {RetryDelayMs}ms...");
                    Thread.Sleep(RetryDelayMs);
                }

                _operationLock.Wait();
                try
                {
                    EnsureOperationInterval();
                    return PrintInternal(templatePath, fieldValues, printer, copies);
                }
                catch (Exception ex) when (IsComBusyError(ex))
                {
                    LoggerService.Warn($"BarTender 忙碌，将在 {RetryDelayMs}ms 后重试: {ex.Message}");
                    continue;
                }
                finally
                {
                    _operationLock.Release();
                }
            }

            return new PrintResult(false, "打印失败：BarTender 持续忙碌，请稍后重试");
        }

        private PrintResult PrintInternal(string templatePath, Dictionary<string, string> fieldValues, string printer, int copies)
        {
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
                throw;
            }
        }

        private void EnsureOperationInterval()
        {
            var elapsed = (DateTime.Now - _lastOperationTime).TotalMilliseconds;
            if (elapsed < MinOperationIntervalMs)
            {
                var delay = (int)(MinOperationIntervalMs - elapsed);
                LoggerService.Debug($"操作间隔等待 {delay}ms");
                Thread.Sleep(delay);
            }
            _lastOperationTime = DateTime.Now;
        }

        private static bool IsComBusyError(Exception ex)
        {
            var message = ex.Message?.ToLower() ?? "";
            return message.Contains("正在打印") ||
                   message.Contains("当前正在") ||
                   message.Contains("busy") ||
                   message.Contains("0x80010105") ||
                   message.Contains("rpc_e_serverfault") ||
                   message.Contains("0x80010001") ||
                   message.Contains("rpc_e_call_rejected");
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
            _operationLock.Wait();
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
            finally
            {
                _connected = false;
                _offlineMode = false;
                _operationLock.Release();
            }
        }

        public void Dispose()
        {
            Disconnect();
            _operationLock?.Dispose();
        }
    }

    public class PrintResult
    {
        public bool Success { get; }
        public string ErrorMessage { get; }
        public PrintResult(bool success, string msg) { Success = success; ErrorMessage = msg ?? ""; }
    }
}
