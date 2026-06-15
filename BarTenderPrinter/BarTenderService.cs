using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace BarTenderPrinter
{
    public class BarTenderService : IDisposable
    {
        private object _engine;
        private Type _engineType;
        private object _comObject;
        private bool _useCom;
        private bool _connected;
        private bool _offlineMode;

        public bool IsConnected => _connected;
        public bool IsOfflineMode => _offlineMode;

        public bool Connect()
        {
            try
            {
                LoggerService.Info("正在加载 BarTender SDK...");

                // Try to find Seagull.BarTender.Print.dll in common locations
                var asm = TryLoadAssembly();
                if (asm == null && !_useCom)
                {
                    LoggerService.Warn("BarTender 未安装，进入离线模式");
                    _offlineMode = true;
                    _connected = false;
                    return false;
                }

                if (_useCom)
                {
                    _connected = true;
                    return true;
                }

                _engineType = asm.GetType("Seagull.BarTender.Print.Engine");
                if (_engineType == null)
                {
                    LoggerService.Warn("Engine 类型未找到，进入离线模式");
                    _offlineMode = true;
                    _connected = false;
                    return false;
                }

                _engine = Activator.CreateInstance(_engineType, new object[] { true });
                _connected = true;
                LoggerService.Info("BarTender SDK 引擎已启动");
                return true;
            }
            catch (Exception ex)
            {
                LoggerService.Warn($"BarTender 加载失败，进入离线模式: {ex.Message}");
                _offlineMode = true;
                _connected = false;
                return false;
            }
        }

        private Assembly TryLoadAssembly()
        {
            // 1. Try default load (GAC or app directory)
            try
            {
                var asm = Assembly.Load("Seagull.BarTender.Print");
                if (asm != null) { LoggerService.Info("从 GAC 加载成功"); return asm; }
            }
            catch { }

            // 2. Try loading from BarTender installation directory
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var programFiles64 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var searchPaths = new[]
            {
                Path.Combine(programFiles, "BarTender"),
                Path.Combine(programFiles64, "BarTender"),
                @"C:\Program Files (x86)\BarTender",
                @"C:\Program Files\BarTender",
            };

            foreach (var dir in searchPaths)
            {
                var dllPath = Path.Combine(dir, "Seagull.BarTender.Print.dll");
                if (File.Exists(dllPath))
                {
                    try
                    {
                        var asm = Assembly.LoadFrom(dllPath);
                        LoggerService.Info($"从 {dllPath} 加载成功");
                        return asm;
                    }
                    catch (Exception ex)
                    {
                        LoggerService.Warn($"加载 {dllPath} 失败: {ex.Message}");
                    }
                }

                // Also try subdirectories
                if (Directory.Exists(dir))
                {
                    try
                    {
                        var files = Directory.GetFiles(dir, "Seagull.BarTender.Print.dll", SearchOption.AllDirectories);
                        foreach (var f in files)
                        {
                            try
                            {
                                var asm = Assembly.LoadFrom(f);
                                LoggerService.Info($"从 {f} 加载成功");
                                return asm;
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }

            // 3. Try COM interop as fallback
            try
            {
                var comType = Type.GetTypeFromProgID("BarTender.Application");
                if (comType != null)
                {
                    LoggerService.Info("找到 BarTender COM 组件，尝试 COM 模式");
                    // Use COM directly
                    var comObj = Activator.CreateInstance(comType);
                    if (comObj != null)
                    {
                        // Store COM object for direct use
                        _comObject = comObj;
                        _useCom = true;
                        _connected = true;
                        LoggerService.Info("BarTender COM 模式已连接");
                        return null; // Will use COM path
                    }
                }
            }
            catch { }

            return null;
        }

        public List<string> GetTemplateDataSources(string templatePath)
        {
            var result = new List<string>();
            if (!_connected) return result;

            // COM mode
            if (_useCom)
            {
                dynamic btFormat = null;
                try
                {
                    dynamic btApp = _comObject;
                    btFormat = btApp.Formats.Open(templatePath, false, "");
                    var subStrings = btFormat.NamedSubStrings;
                    // Try to enumerate sub strings
                    try
                    {
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
                    btFormat.Close();
                }
                catch (Exception ex)
                {
                    LoggerService.Error($"[COM] 获取模板数据源失败: {ex.Message}", ex);
                    try { btFormat?.Close(); } catch { }
                }
                return result;
            }

            // SDK mode
            if (_engine == null) return result;
            object doc = null;
            try
            {
                var docsProp = _engineType.GetProperty("Documents");
                var docs = docsProp?.GetValue(_engine);
                var openMethod = docs?.GetType().GetMethod("Open", new[] { typeof(string) });
                doc = openMethod?.Invoke(docs, new object[] { templatePath });
                if (doc == null) return result;

                var subStringsProp = doc.GetType().GetProperty("SubStrings");
                var subStrings = subStringsProp?.GetValue(doc);
                if (subStrings != null)
                {
                    var countProp = subStrings.GetType().GetProperty("Count");
                    var count = (int)(countProp?.GetValue(subStrings) ?? 0);
                    var indexer = subStrings.GetType().GetProperty("Item", new[] { typeof(int) });
                    if (indexer == null)
                    {
                        indexer = subStrings.GetType().GetProperty("Item", new[] { typeof(string) });
                    }
                    // Try by index
                    for (int i = 0; i < count; i++)
                    {
                        try
                        {
                            var sub = indexer?.GetValue(subStrings, new object[] { i });
                            if (sub != null)
                            {
                                var nameProp = sub.GetType().GetProperty("Name");
                                var name = nameProp?.GetValue(sub)?.ToString();
                                if (!string.IsNullOrEmpty(name))
                                    result.Add(name);
                            }
                        }
                        catch { }
                    }

                    // If index didn't work, try reflection on properties
                    if (result.Count == 0)
                    {
                        var enumerator = subStrings.GetType().GetMethod("GetEnumerator");
                        var iter = enumerator?.Invoke(subStrings, null);
                        if (iter != null)
                        {
                            var moveNext = iter.GetType().GetMethod("MoveNext");
                            var current = iter.GetType().GetProperty("Current");
                            while ((bool)(moveNext?.Invoke(iter, null) ?? false))
                            {
                                var sub = current?.GetValue(iter);
                                var nameProp = sub?.GetType().GetProperty("Name");
                                var name = nameProp?.GetValue(sub)?.ToString();
                                if (!string.IsNullOrEmpty(name))
                                    result.Add(name);
                            }
                        }
                    }
                }
                CloseDocument(doc);
                doc = null;
            }
            catch (Exception ex)
            {
                LoggerService.Error($"获取模板数据源失败: {ex.Message}", ex);
                CloseDocument(doc);
            }
            return result;
        }

        public string ExportPreview(string templatePath, int dpi = 300)
        {
            if (!_connected) return null;

            // COM mode
            if (_useCom)
            {
                dynamic btFormat = null;
                try
                {
                    dynamic btApp = _comObject;
                    btFormat = btApp.Formats.Open(templatePath, false, "");
                    var tempPath = Path.Combine(Path.GetTempPath(), $"bt_preview_{Guid.NewGuid():N}.png");
                    btFormat.ExportImageToFile(tempPath, 3, 0, 0, dpi, dpi);
                    btFormat.Close();
                    return File.Exists(tempPath) ? tempPath : null;
                }
                catch (Exception ex)
                {
                    LoggerService.Error($"[COM] 预览导出失败: {ex.Message}", ex);
                    try { btFormat?.Close(); } catch { }
                    return null;
                }
            }

            // SDK mode
            if (_engine == null) return null;
            object doc = null;
            try
            {
                var docsProp = _engineType.GetProperty("Documents");
                var docs = docsProp?.GetValue(_engine);
                var openMethod = docs?.GetType().GetMethod("Open", new[] { typeof(string) });
                doc = openMethod?.Invoke(docs, new object[] { templatePath });
                if (doc == null) return null;
                var tempPath = Path.Combine(Path.GetTempPath(), $"bt_preview_{Guid.NewGuid():N}.png");
                var exportMethod = doc.GetType().GetMethod("ExportImageToFile", new[]
                {
                    typeof(string),
                    GetEnumType("Seagull.BarTender.Print.ImageType"),
                    GetEnumType("Seagull.BarTender.Print.ColorDepth"),
                    GetResolutionType(),
                    GetEnumType("Seagull.BarTender.Print.OverwriteOptions")
                });
                if (exportMethod != null)
                {
                    exportMethod.Invoke(doc, new object[] { tempPath,
                        GetEnumValue("Seagull.BarTender.Print.ImageType", "PNG"),
                        GetEnumValue("Seagull.BarTender.Print.ColorDepth", "ColorDepth256"),
                        CreateResolution(dpi, dpi),
                        GetEnumValue("Seagull.BarTender.Print.OverwriteOptions", "Overwrite") });
                }
                CloseDocument(doc);
                doc = null;
                return File.Exists(tempPath) ? tempPath : null;
            }
            catch (Exception ex)
            {
                LoggerService.Error($"预览导出失败: {ex.Message}", ex);
                CloseDocument(doc);
                return null;
            }
        }

        public PrintResult Print(string templatePath, Dictionary<string, string> fieldValues, string printer, int copies)
        {
            if (!_connected)
                return new PrintResult(false, "BarTender 未连接");

            // Use COM mode if SDK not available
            if (_useCom)
                return PrintViaCom(templatePath, fieldValues, printer, copies);

            if (_engine == null)
                return new PrintResult(false, "BarTender 引擎未初始化");

            object doc = null;
            try
            {
                LoggerService.Info($"准备打开模板: {templatePath}");
                var docsProp = _engineType.GetProperty("Documents");
                var docs = docsProp?.GetValue(_engine);
                var openMethod = docs?.GetType().GetMethod("Open", new[] { typeof(string) });
                doc = openMethod?.Invoke(docs, new object[] { templatePath });
                if (doc == null) return new PrintResult(false, "无法打开模板文件");

                var subStringsProp = doc.GetType().GetProperty("SubStrings");
                var subStrings = subStringsProp?.GetValue(doc);
                if (subStrings != null)
                {
                    var missing = new List<string>();
                    foreach (var kv in fieldValues)
                    {
                        try
                        {
                            var indexer = subStrings.GetType().GetProperty("Item", new[] { typeof(string) });
                            var sub = indexer?.GetValue(subStrings, new object[] { kv.Key });
                            if (sub != null)
                            {
                                var valueProp = sub.GetType().GetProperty("Value");
                                valueProp?.SetValue(sub, kv.Value);
                                LoggerService.Info($"数据源设置: {kv.Key}={kv.Value}");
                            }
                            else missing.Add(kv.Key);
                        }
                        catch { missing.Add(kv.Key); }
                    }
                    if (missing.Count > 0)
                        return new PrintResult(false, $"模板中未找到字段: {string.Join(", ", missing.Distinct())}");
                }

                // Set printer and copies
                try
                {
                    var printSetupProp = doc.GetType().GetProperty("PrintSetup");
                    var printSetup = printSetupProp?.GetValue(doc);
                    if (printSetup != null)
                    {
                        var printerProp = printSetup.GetType().GetProperty("Printer");
                        printerProp?.SetValue(printSetup, printer);
                        LoggerService.Info($"打印机设置: {printer}");

                        var copiesProp = printSetup.GetType().GetProperty("IdenticalCopiesOfLabel");
                        copiesProp?.SetValue(printSetup, copies);
                        LoggerService.Info($"打印份数设置: {copies}");
                    }
                }
                catch (Exception ex) { LoggerService.Warn($"设置打印机/份数失败: {ex.Message}"); }

                var printMethod = doc.GetType().GetMethod("Print", new[] { typeof(string), typeof(int) });
                if (printMethod != null)
                    printMethod.Invoke(doc, new object[] { $"BarPrint{DateTime.Now:yyyyMMddHHmmss}", copies });
                else
                {
                    var printOut = doc.GetType().GetMethod("PrintOut", new Type[0]);
                    printOut?.Invoke(doc, null);
                }
                LoggerService.Info("打印完成");
                CloseDocument(doc);
                return new PrintResult(true, "");
            }
            catch (Exception ex)
            {
                LoggerService.Error($"打印失败: {ex.Message}", ex);
                CloseDocument(doc);
                return new PrintResult(false, $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        private PrintResult PrintViaCom(string templatePath, Dictionary<string, string> fieldValues, string printer, int copies)
        {
            dynamic btFormat = null;
            try
            {
                LoggerService.Info($"[COM] 准备打开模板: {templatePath}");
                dynamic btApp = _comObject;
                btFormat = btApp.Formats.Open(templatePath, false, "");
                LoggerService.Info("[COM] 模板打开成功");

                foreach (var kv in fieldValues)
                {
                    btFormat.SetNamedSubStringValue(kv.Key, kv.Value);
                    LoggerService.Info($"[COM] 数据源设置: {kv.Key}={kv.Value}");
                }

                btFormat.Printer = printer;
                LoggerService.Info($"[COM] 打印机设置: {printer}");

                btFormat.PrintSetup.IdenticalCopiesOfLabel = copies;
                LoggerService.Info($"[COM] 打印份数: {copies}");

                btFormat.PrintOut(false, false);
                LoggerService.Info("[COM] PrintOut 执行完成");

                btFormat.Close();
                LoggerService.Info("[COM] 打印完成");
                return new PrintResult(true, "");
            }
            catch (Exception ex)
            {
                LoggerService.Error($"[COM] 打印失败: {ex.Message}", ex);
                try { btFormat?.Close(); } catch { }
                return new PrintResult(false, $"COM: {ex.Message}");
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

        private void CloseDocument(object doc)
        {
            if (doc == null) return;
            try
            {
                var closeMethod = doc.GetType().GetMethod("Close");
                if (closeMethod != null)
                {
                    var parms = closeMethod.GetParameters();
                    if (parms != null && parms.Length == 1)
                    {
                        var enumType = parms[0].ParameterType;
                        closeMethod.Invoke(doc, new object[] { Enum.ToObject(enumType, 0) });
                    }
                    else closeMethod.Invoke(doc, null);
                }
            }
            catch { }
        }

        private Type GetEnumType(string typeName)
        {
            try { return Assembly.Load("Seagull.BarTender.Print")?.GetType(typeName); } catch { return null; }
        }
        private object GetEnumValue(string typeName, string valueName)
        {
            var type = GetEnumType(typeName);
            return type != null ? Enum.Parse(type, valueName) : null;
        }
        private Type GetResolutionType() => GetEnumType("Seagull.BarTender.Print.Resolution");
        private object CreateResolution(int x, int y) => Activator.CreateInstance(GetResolutionType(), new object[] { x, y });

        public void Disconnect()
        {
            try
            {
                if (_useCom && _comObject != null)
                {
                    ((dynamic)_comObject).Quit(0);
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(_comObject);
                    _comObject = null;
                }
                if (_engine != null)
                    _engineType?.GetMethod("Stop")?.Invoke(_engine, null);
            }
            catch { }
            _connected = false;
            _useCom = false;
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
