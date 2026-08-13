using System.Windows.Forms;

namespace BarTenderPrinter
{
    public interface IDialogService
    {
        DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1);
        void ShowInfo(IWin32Window owner, string text, string caption = "提示");
        void ShowWarning(IWin32Window owner, string text, string caption = "警告");
        void ShowError(IWin32Window owner, string text, string caption = "错误");
        bool Confirm(IWin32Window owner, string text, string caption = "确认");
    }

    public class DialogService : IDialogService
    {
        public DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1)
        {
            return MessageBox.Show(owner, text, caption, buttons, icon, defaultButton);
        }

        public void ShowInfo(IWin32Window owner, string text, string caption = "提示") => Show(owner, text, caption, MessageBoxButtons.OK, MessageBoxIcon.Information);
        public void ShowWarning(IWin32Window owner, string text, string caption = "警告") => Show(owner, text, caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        public void ShowError(IWin32Window owner, string text, string caption = "错误") => Show(owner, text, caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
        public bool Confirm(IWin32Window owner, string text, string caption = "确认") => Show(owner, text, caption, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
    }
}
