using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Runtime.InteropServices;

namespace BarTenderPrinter
{
    public class BarTenderService : IDisposable
    {
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, ref System.Drawing.Rectangle lpRect);
        
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

        public string ExportPreview(string templatePath)
        {
            if (!_connected || _btApp == null) return null;

            for (int retry = 0; retry < MaxRetries; retry++)
            {
                if (retry > 0)
                {
                    LoggerService.Info($"预览重试 {retry}/{MaxRetries - 1}，等待 {RetryDelayMs}ms...");
                    Thread.Sleep(RetryDelayMs);
                }

                _operationLock.Wait();
                try
                {
                    EnsureOperationInterval();
                    var result = ExportPreviewInternal(templatePath);
                    if (result != null) return result;
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

            LoggerService.Error("预览失败：已达到最大重试次数");
            return null;
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

                // 7. 尝试导出预览
                var tempPath = Path.Combine(Path.GetTempPath(), $"bt_diagnostic_{Guid.NewGuid():N}.png");
                
                // 7.1 尝试 ExportImageToFile
                LoggerService.Info("[诊断] 尝试 ExportImageToFile...");
                try
                {
                    btFormat.ExportImageToFile(tempPath);
                    if (File.Exists(tempPath))
                    {
                        var fileInfo = new FileInfo(tempPath);
                        LoggerService.Info($"[诊断] ExportImageToFile 成功，文件大小: {fileInfo.Length} bytes");
                        if (fileInfo.Length > 0)
                        {
                            LoggerService.Info("[诊断] 预览导出功能正常");
                        }
                        else
                        {
                            LoggerService.Warn("[诊断] 导出的文件大小为 0");
                        }
                        try { File.Delete(tempPath); } catch { }
                    }
                    else
                    {
                        LoggerService.Warn("[诊断] ExportImageToFile 未创建文件");
                    }
                }
                catch (Exception ex)
                {
                    LoggerService.Error($"[诊断] ExportImageToFile 失败: {ex.Message}");
                    LoggerService.Error($"[诊断] 异常类型: {ex.GetType().Name}");
                    if (ex.InnerException != null)
                    {
                        LoggerService.Error($"[诊断] 内部异常: {ex.InnerException.Message}");
                    }
                }

                // 7.2 尝试 ExportImageToClipboard
                LoggerService.Info("[诊断] 尝试 ExportImageToClipboard...");
                try
                {
                    btFormat.ExportImageToClipboard(300, 300);
                    var img = System.Windows.Forms.Clipboard.GetImage();
                    if (img != null)
                    {
                        LoggerService.Info($"[诊断] ExportImageToClipboard 成功，图像尺寸: {img.Width}x{img.Height}");
                        img.Dispose();
                    }
                    else
                    {
                        LoggerService.Warn("[诊断] ExportImageToClipboard 未获取到图像");
                    }
                }
                catch (Exception ex)
                {
                    LoggerService.Error($"[诊断] ExportImageToClipboard 失败: {ex.Message}");
                }

                // 8. 检查 BarTender 版本
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

        private string ExportPreviewInternal(string templatePath)
        {
            dynamic btFormat = null;
            try
            {
                LoggerService.Info($"打开模板: {templatePath}");
                btFormat = _btApp.Formats.Open(templatePath, false, "");
                LoggerService.Info("模板打开成功");
                var tempPath = Path.Combine(Path.GetTempPath(), $"bt_preview_{Guid.NewGuid():N}.png");

                // 方式1: 尝试 ExportImageToFile（标准方式）
                var exportMethods = new (string name, Action tryFunc)[]
                {
                    ("ExportImageToFile(path, 3, 0, 300, 300)", () => btFormat.ExportImageToFile(tempPath, 3, 0, 300, 300)),
                    ("ExportImageToFile(path, 3, 0, 300)", () => btFormat.ExportImageToFile(tempPath, 3, 0, 300)),
                    ("ExportImageToFile(path, 3, 0)", () => btFormat.ExportImageToFile(tempPath, 3, 0)),
                    ("ExportImageToFile(path, 3)", () => btFormat.ExportImageToFile(tempPath, 3)),
                    ("ExportImageToFile(path)", () => btFormat.ExportImageToFile(tempPath)),
                };

                foreach (var (name, tryFunc) in exportMethods)
                {
                    try
                    {
                        LoggerService.Debug($"尝试 {name}");
                        tryFunc();
                        var fileInfo = new FileInfo(tempPath);
                        if (File.Exists(tempPath) && fileInfo.Length > 0)
                        {
                            LoggerService.Info($"预览成功 ({name})，文件大小: {fileInfo.Length} bytes");
                            CloseFormat(btFormat);
                            return tempPath;
                        }
                    }
                    catch (Exception ex)
                    {
                        LoggerService.Debug($"{name} 异常: {ex.Message}");
                        try { if (File.Exists(tempPath) && new FileInfo(tempPath).Length == 0) File.Delete(tempPath); } catch { }
                    }
                }

                // 方式2: 尝试 ExportImageToClipboard
                try
                {
                    LoggerService.Debug("尝试 ExportImageToClipboard");
                    btFormat.ExportImageToClipboard(300, 300);
                    var img = System.Windows.Forms.Clipboard.GetImage();
                    if (img != null)
                    {
                        img.Save(tempPath, System.Drawing.Imaging.ImageFormat.Png);
                        img.Dispose();
                        var fileInfo = new FileInfo(tempPath);
                        if (File.Exists(tempPath) && fileInfo.Length > 0)
                        {
                            LoggerService.Info($"预览成功 (ExportImageToClipboard)，文件大小: {fileInfo.Length} bytes");
                            CloseFormat(btFormat);
                            return tempPath;
                        }
                    }
                }
                catch (Exception ex) { LoggerService.Debug($"ExportImageToClipboard 异常: {ex.Message}"); }

                // 方式3: 尝试使用 Format 的 PrintOut 方法打印到 PDF
                try
                {
                    LoggerService.Debug("尝试 PrintOut 到 PDF");
                    var pdfPath = Path.Combine(Path.GetTempPath(), $"bt_preview_{Guid.NewGuid():N}.pdf");
                    
                    // 保存当前打印机设置
                    string currentPrinter = null;
                    try { currentPrinter = (string)btFormat.Printer; } catch { }
                    
                    // 设置打印机为 Microsoft Print to PDF
                    try
                    {
                        btFormat.Printer = "Microsoft Print to PDF";
                        LoggerService.Debug("已设置打印机为 Microsoft Print to PDF");
                    }
                    catch (Exception ex)
                    {
                        LoggerService.Debug($"设置打印机失败: {ex.Message}");
                    }
                    
                    // 尝试打印
                    btFormat.PrintOut(false, false);
                    
                    // 恢复原来的打印机设置
                    if (!string.IsNullOrEmpty(currentPrinter))
                    {
                        try { btFormat.Printer = currentPrinter; } catch { }
                    }
                    
                    LoggerService.Info("PrintOut 执行完成");
                }
                catch (Exception ex) { LoggerService.Debug($"PrintOut 异常: {ex.Message}"); }

                // 方式4: 尝试使用 Application 的 Visible 属性显示预览窗口，然后截取窗口
                try
                {
                    LoggerService.Debug("尝试显示 BarTender 窗口并截取窗口");
                    
                    // 显示 BarTender 窗口
                    _btApp.Visible = true;
                    System.Threading.Thread.Sleep(1500); // 等待窗口显示和渲染
                    
                    // 获取 BarTender 窗口句柄
                    IntPtr hWnd = IntPtr.Zero;
                    try
                    {
                        // 尝试通过 COM 获取窗口句柄
                        hWnd = (IntPtr)_btApp.hWnd;
                        LoggerService.Debug($"获取到 BarTender 窗口句柄: {hWnd}");
                    }
                    catch (Exception ex)
                    {
                        LoggerService.Debug($"获取窗口句柄失败: {ex.Message}");
                    }
                    
                    // 如果获取到窗口句柄，截取该窗口
                    if (hWnd != IntPtr.Zero)
                    {
                        // 使用 Windows API 获取窗口位置
                        var rect = new System.Drawing.Rectangle();
                        GetWindowRect(hWnd, ref rect);
                        
                        // 截取窗口区域
                        int width = rect.Right - rect.Left;
                        int height = rect.Bottom - rect.Top;
                        
                        if (width > 0 && height > 0)
                        {
                            var windowBitmap = new System.Drawing.Bitmap(width, height);
                            using (var g = System.Drawing.Graphics.FromImage(windowBitmap))
                            {
                                g.CopyFromScreen(rect.Left, rect.Top, 0, 0, new System.Drawing.Size(width, height));
                            }
                            
                            // 裁剪掉标题栏和菜单栏（大约 100 像素）
                            int cropTop = 100;
                            if (height > cropTop + 50)
                            {
                                var croppedBitmap = new System.Drawing.Bitmap(width, height - cropTop);
                                using (var g = System.Drawing.Graphics.FromImage(croppedBitmap))
                                {
                                    g.DrawImage(windowBitmap, 0, -cropTop);
                                }
                                windowBitmap.Dispose();
                                windowBitmap = croppedBitmap;
                            }
                            
                            windowBitmap.Save(tempPath, System.Drawing.Imaging.ImageFormat.Png);
                            windowBitmap.Dispose();
                            
                            LoggerService.Info($"预览成功 (窗口截取方式)，窗口大小: {width}x{height}");
                        }
                    }
                    else
                    {
                        // 如果获取不到窗口句柄，截取整个屏幕的中心区域
                        LoggerService.Debug("无法获取窗口句柄，截取屏幕中心区域");
                        var screenWidth = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Width;
                        var screenHeight = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Height;
                        
                        // 截取屏幕中心区域（假设 BarTender 窗口在屏幕中心）
                        int cropWidth = Math.Min(800, screenWidth);
                        int cropHeight = Math.Min(600, screenHeight);
                        int cropX = (screenWidth - cropWidth) / 2;
                        int cropY = (screenHeight - cropHeight) / 2;
                        
                        var screenBitmap = new System.Drawing.Bitmap(cropWidth, cropHeight);
                        using (var g = System.Drawing.Graphics.FromImage(screenBitmap))
                        {
                            g.CopyFromScreen(cropX, cropY, 0, 0, new System.Drawing.Size(cropWidth, cropHeight));
                        }
                        
                        screenBitmap.Save(tempPath, System.Drawing.Imaging.ImageFormat.Png);
                        screenBitmap.Dispose();
                        
                        LoggerService.Info("预览成功 (屏幕中心区域截取方式)");
                    }
                    
                    // 隐藏 BarTender 窗口
                    _btApp.Visible = false;
                    
                    if (File.Exists(tempPath) && new FileInfo(tempPath).Length > 0)
                    {
                        CloseFormat(btFormat);
                        return tempPath;
                    }
                }
                catch (Exception ex) 
                { 
                    LoggerService.Debug($"窗口截取方式异常: {ex.Message}");
                    // 确保隐藏 BarTender 窗口
                    try { _btApp.Visible = false; } catch { }
                }

                // 方式5: 尝试使用 Format 的 ExportImageToFile 方法（使用不同的参数）
                try
                {
                    LoggerService.Debug("尝试 ExportImageToFile 使用不同的参数");
                    btFormat.ExportImageToFile(tempPath, 3, 0, 300, 300, 1);
                    if (File.Exists(tempPath) && new FileInfo(tempPath).Length > 0)
                    {
                        LoggerService.Info("预览成功 (ExportImageToFile with different params)");
                        CloseFormat(btFormat);
                        return tempPath;
                    }
                }
                catch (Exception ex) { LoggerService.Debug($"ExportImageToFile with different params 异常: {ex.Message}"); }

                CloseFormat(btFormat);
                LoggerService.Warn("预览导出失败：所有方式都失败");
                return null;
            }
            catch (Exception ex)
            {
                LoggerService.Error($"预览失败: {ex.Message}");
                CloseFormat(btFormat);
                throw;
            }
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
