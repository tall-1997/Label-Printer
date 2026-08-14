using System;
using System.Threading;
using System.Windows.Forms;

namespace BarTenderPrinter
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            using var singleInstance = new Mutex(true, @"Local\BarTenderPrinter-SingleInstance", out var createdNew);
            if (!createdNew)
            {
                MessageBox.Show("BarTender 标签打印工具已在运行。", "程序已启动", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            AppPaths.Initialize();
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(args != null && args.Length > 0 ? args[0] : null));
        }
    }
}
