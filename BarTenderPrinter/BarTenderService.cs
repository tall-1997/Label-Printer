using System;
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
                if (asm == null)
                {
                    LoggerService.Error("Seagull.BarTender.Print 程序集未找到");
                    return false;
                }

                _engineType = asm.GetType("Seagull.BarTender.Print.Engine");
                if (_engineType == null)
                {
                    LoggerService.Error("Engine 类型未找到");
                    return false;
                }

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

        public PrintResult Print(string templatePath, DataSourceField[] fields, string printer, int copies)
        {
            if (!_connected || _engine == null)
                return new PrintResult(false, "BarTender 未连接");

            object doc = null;
            try
            {
                LoggerService.Info($"准备打开模板: {templatePath}");
                var openMethod = _engineType.GetMethod("Documents.Open", new[] { typeof(string) });
                if (openMethod == null)
                {
                    var docsProp = _engineType.GetProperty("Documents");
                    var docs = docsProp?.GetValue(_engine);
                    openMethod = docs?.GetType().GetMethod("Open", new[] { typeof(string) });
                    doc = openMethod?.Invoke(docs, new object[] { templatePath });
                }
                else
                {
                    doc = openMethod.Invoke(_engine, new object[] { templatePath });
                }

                if (doc == null)
                    return new PrintResult(false, "无法打开模板文件");

                LoggerService.Info("模板打开成功");

                // Set data sources via SubStrings
                var subStringsProp = doc.GetType().GetProperty("SubStrings");
                var subStrings = subStringsProp?.GetValue(doc);
                if (subStrings != null)
                {
                    var missingFields = new System.Collections.Generic.List<string>();
                    foreach (var f in fields)
                    {
                        try
                        {
                            // subStrings[fieldName] -> SubString object
                            var indexer = subStrings.GetType().GetProperty("Item", new[] { typeof(string) });
                            var sub = indexer?.GetValue(subStrings, new object[] { f.FieldName });
                            if (sub != null)
                            {
                                var valueProp = sub.GetType().GetProperty("Value");
                                valueProp?.SetValue(sub, f.Value);
                                LoggerService.Info($"数据源设置: {f.FieldName}={f.Value}");
                            }
                            else
                            {
                                missingFields.Add(f.FieldName);
                            }
                        }
                        catch
                        {
                            missingFields.Add(f.FieldName);
                        }
                    }
                    if (missingFields.Count > 0)
                    {
                        var msg = $"模板中未找到以下字段: {string.Join(", ", missingFields.Distinct())}";
                        LoggerService.Error(msg);
                        return new PrintResult(false, msg);
                    }
                }

                // Print: doc.Print("BarPrint" + DateTime.Now, copies)
                var printMethod = doc.GetType().GetMethod("Print", new[] { typeof(string), typeof(int) });
                if (printMethod != null)
                {
                    printMethod.Invoke(doc, new object[] { $"BarPrint{DateTime.Now:yyyyMMddHHmmss}", copies });
                    LoggerService.Info("Print 执行完成");
                }
                else
                {
                    // Fallback: try PrintOut
                    var printOut = doc.GetType().GetMethod("PrintOut", new Type[0]);
                    printOut?.Invoke(doc, null);
                    LoggerService.Info("PrintOut 执行完成");
                }

                // Close document via reflection
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

                // doc.ExportImageToFile(tempPath, ImageType.PNG, ColorDepth.ColorDepth256, new Resolution(300, 300), OverwriteOptions.Overwrite)
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
                    var imageType = GetEnumValue("Seagull.BarTender.Print.ImageType", "PNG");
                    var colorDepth = GetEnumValue("Seagull.BarTender.Print.ColorDepth", "ColorDepth256");
                    var overwrite = GetEnumValue("Seagull.BarTender.Print.OverwriteOptions", "Overwrite");
                    var resolution = CreateResolution(dpi, dpi);

                    exportMethod.Invoke(doc, new object[] { tempPath, imageType, colorDepth, resolution, overwrite });
                }
                else
                {
                    // Fallback: try simpler overload
                    var simpleExport = doc.GetType().GetMethods()
                        .FirstOrDefault(m => m.Name == "ExportImageToFile" && m.GetParameters().Length >= 1);
                    simpleExport?.Invoke(doc, new object[] { tempPath });
                }

                CloseDocument(doc);
                doc = null;

                if (File.Exists(tempPath))
                    return tempPath;
                return null;
            }
            catch (Exception ex)
            {
                LoggerService.Error($"预览导出失败: {ex.Message}", ex);
                CloseDocument(doc);
                return null;
            }
        }

        public string[] GetAvailableTemplates(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                return new string[0];
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
            catch (Exception ex)
            {
                LoggerService.Error("获取打印机列表失败", ex);
                return new string[0];
            }
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
                        var dontSaveValue = Enum.ToObject(enumType, 0);
                        closeMethod.Invoke(doc, new object[] { dontSaveValue });
                    }
                    else
                    {
                        closeMethod.Invoke(doc, null);
                    }
                }
            }
            catch { }
        }

        private Type GetEnumType(string typeName)
        {
            try
            {
                var asm = Assembly.Load("Seagull.BarTender.Print");
                return asm?.GetType(typeName);
            }
            catch { return null; }
        }

        private object GetEnumValue(string typeName, string valueName)
        {
            var type = GetEnumType(typeName);
            if (type == null) return null;
            return Enum.Parse(type, valueName);
        }

        private Type GetResolutionType()
        {
            return GetEnumType("Seagull.BarTender.Print.Resolution");
        }

        private object CreateResolution(int x, int y)
        {
            var type = GetResolutionType();
            if (type == null) return null;
            return Activator.CreateInstance(type, new object[] { x, y });
        }

        public void Disconnect()
        {
            try
            {
                if (_engine != null)
                {
                    var stopMethod = _engineType?.GetMethod("Stop");
                    stopMethod?.Invoke(_engine, null);
                }
            }
            catch { }
            _connected = false;
        }

        public void Dispose() => Disconnect();
    }

    public class DataSourceField
    {
        public string DisplayName { get; set; }
        public string FieldName { get; set; }
        public string Value { get; set; }
    }

    public class DataSourceConfig
    {
        public string DisplayName { get; set; }
        public string FieldName { get; set; }
    }

    public class PrintResult
    {
        public bool Success { get; }
        public string ErrorMessage { get; }
        public PrintResult(bool success, string errorMessage)
        {
            Success = success;
            ErrorMessage = errorMessage ?? "";
        }
    }
}
