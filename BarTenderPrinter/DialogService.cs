using System.Windows.Forms;

namespace BarTenderPrinter
{
    public interface IDialogService
    {
        DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1);
    }

    public class DialogService : IDialogService
    {
        public DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1)
        {
            return MessageBox.Show(owner, text, caption, buttons, icon, defaultButton);
        }
    }
}
