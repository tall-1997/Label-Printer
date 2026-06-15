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
        private bool _connected;

        public bool IsConnected => _connected;

        public bool Connect()
        {
            try
            {
                LoggerService.Info("正在加载 BarTender SDK...");
                var asm = Assembly.Load("Seagull.BarTender.Print");
                if (asm == null) { LoggerService.Error("Seagull.BarTender.Print 未找到"); return false; }
                _engineType = asm.GetType("Seagull.BarTender.Print.Engine");
                if (_engineType == null) { LoggerService.Error("Engine 类型未找到"); return false; }
                _engine = Activator.CreateInstance(_engineType, new object[] { true });
                _connected = true;
                LoggerService.Info("BarTender SDK 引擎已启动");
                return true;
            }
            catch (Exception ex)
            {
                LoggerService.Error("BarTender SDK 加载失败", ex);
                _connected = false;
                return false;
            }
        }

        public List<string> GetTemplateDataSources(string templatePath)
        {
            var result = new List<string>();
            if (!_connected || _engine == null) return result;
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
            if (!_connected || _engine == null) return null;
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
            if (!_connected || _engine == null)
                return new PrintResult(false, "BarTender 未连接");
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
                if (_engine != null)
                    _engineType?.GetMethod("Stop")?.Invoke(_engine, null);
            }
            catch { }
            _connected = false;
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
