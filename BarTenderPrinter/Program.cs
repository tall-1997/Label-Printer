using System;
using System.Windows.Forms;

namespace BarTenderPrinter
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            AppPaths.Initialize();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(args != null && args.Length > 0 ? args[0] : null));
        }
    }
}
