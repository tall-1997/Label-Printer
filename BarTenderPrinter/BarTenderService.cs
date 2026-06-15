using System;
using System.IO;
using System.Runtime.InteropServices;

namespace BarTenderPrinter
{
    public class BarTenderService : IDisposable
    {
        private dynamic _btApp;
        private bool _connected;

        public bool IsConnected => _connected;

        public bool Connect()
        {
            try
            {
                LoggerService.Info("正在连接 BarTender...");
                var type = Type.GetTypeFromProgID("BarTender.Application");
                if (type == null)
                {
                    LoggerService.Error("BarTender COM 组件未注册");
                    return false;
                }
                _btApp = Activator.CreateInstance(type);
                _btApp.Visible = false;
                _connected = true;
                LoggerService.Info("BarTender 已连接");
                return true;
            }
            catch (Exception ex)
            {
                LoggerService.Error("BarTender 连接失败", ex);
                _connected = false;
                return false;
            }
        }

        public PrintResult Print(string templatePath, DataSourceField[] fields, string printer, int copies)
        {
            if (!_connected || _btApp == null)
                return new PrintResult(false, "BarTender 未连接");

            dynamic btFormat = null;
            try
            {
                LoggerService.Info($"准备打开模板: {templatePath}");
                btFormat = _btApp.Formats.Open(templatePath, false, "");
                LoggerService.Info("模板打开成功");

                foreach (var f in fields)
                {
                    btFormat.SetNamedSubStringValue(f.FieldName, f.Value);
                    LoggerService.Info($"数据源设置: {f.FieldName}={f.Value}");
                }

                btFormat.Printer = printer;
                LoggerService.Info($"打印机设置成功: {printer}");

                btFormat.PrintSetup.IdenticalCopiesOfLabel = copies;
                LoggerService.Info($"打印份数: {copies}");

                btFormat.PrintOut(false, false);
                LoggerService.Info("PrintOut 执行完成");

                btFormat.Close();
                LoggerService.Info("模板关闭成功");
                return new PrintResult(true, "");
            }
            catch (Exception ex)
            {
                LoggerService.Error($"打印失败: {ex.Message}", ex);
                try { btFormat?.Close(); } catch { }
                return new PrintResult(false, $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        public string ExportPreview(string templatePath, int width = 400, int height = 300)
        {
            if (!_connected || _btApp == null) return null;
            dynamic btFormat = null;
            try
            {
                btFormat = _btApp.Formats.Open(templatePath, false, "");
                var tempPath = Path.Combine(Path.GetTempPath(), $"bt_preview_{Guid.NewGuid():N}.png");
                btFormat.ExportImageToFile(tempPath, 3, 0, 0, width, height);
                btFormat.Close();
                return tempPath;
            }
            catch (Exception ex)
            {
                LoggerService.Error($"预览导出失败: {ex.Message}", ex);
                try { btFormat?.Close(); } catch { }
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

        public void Disconnect()
        {
            try
            {
                if (_btApp != null)
                {
                    _btApp.Quit(0);
                    Marshal.ReleaseComObject(_btApp);
                    _btApp = null;
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
