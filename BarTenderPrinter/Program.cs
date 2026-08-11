using System;
using System.Windows.Forms;

namespace BarTenderPrinter
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            AppPaths.Initialize();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
