using System;
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

        public PrintResult Print(string templatePath, string datasource, string imei, string printer, int copies)
        {
            if (!_connected || _btApp == null)
            {
                return new PrintResult(false, "BarTender 未连接");
            }

            dynamic btFormat = null;
            try
            {
                LoggerService.Info($"准备打开模板: {templatePath}");
                btFormat = _btApp.Formats.Open(templatePath, false, "");
                LoggerService.Info("模板打开成功");

                btFormat.SetNamedSubStringValue(datasource, imei);
                LoggerService.Info($"数据源设置成功: {datasource}={imei}");

                btFormat.Printer = printer;
                LoggerService.Info($"打印机设置成功: {printer}");

                btFormat.PrintSetup.IdenticalCopiesOfLabel = copies;
                LoggerService.Info($"打印份数设置成功: {copies}");

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

        public string[] GetPrinters()
        {
            try
            {
                if (System.Drawing.Printing.PrinterSettings.InstalledPrinters.Count == 0)
                    return new string[0];

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

        public void Dispose()
        {
            Disconnect();
        }
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
