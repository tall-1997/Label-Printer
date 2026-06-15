using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BarTenderPrinter
{
    public static class MiuiTheme
    {
        // Colors
        public static readonly Color Primary = Color.FromArgb(0, 122, 255);
        public static readonly Color PrimaryDark = Color.FromArgb(0, 100, 210);
        public static readonly Color PrimaryLight = Color.FromArgb(230, 243, 255);
        public static readonly Color Background = Color.FromArgb(245, 245, 245);
        public static readonly Color CardBackground = Color.White;
        public static readonly Color TextPrimary = Color.FromArgb(33, 33, 33);
        public static readonly Color TextSecondary = Color.FromArgb(117, 117, 117);
        public static readonly Color TextHint = Color.FromArgb(189, 189, 189);
        public static readonly Color Success = Color.FromArgb(76, 175, 80);
        public static readonly Color Error = Color.FromArgb(244, 67, 54);
        public static readonly Color Warning = Color.FromArgb(255, 152, 0);
        public static readonly Color Divider = Color.FromArgb(224, 224, 224);
        public static readonly Color InputBackground = Color.FromArgb(250, 250, 250);
        public static readonly Color Border = Color.FromArgb(220, 220, 220);

        public static void ApplyTheme(System.Windows.Forms.Form form)
        {
            form.BackColor = Background;
            form.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);
            form.ForeColor = TextPrimary;
        }

        public static void StyleButton(System.Windows.Forms.Button btn, bool isPrimary = false)
        {
            if (isPrimary)
            {
                btn.BackColor = Primary;
                btn.ForeColor = Color.White;
                btn.FlatStyle = FlatStyle.Flat;
                btn.FlatAppearance.BorderSize = 0;
            }
            else
            {
                btn.BackColor = CardBackground;
                btn.ForeColor = TextPrimary;
                btn.FlatStyle = FlatStyle.Flat;
                btn.FlatAppearance.BorderColor = Border;
                btn.FlatAppearance.BorderSize = 1;
            }
            btn.Cursor = System.Windows.Forms.Cursors.Hand;
        }

        public static void StyleCard(System.Windows.Forms.Panel panel)
        {
            panel.BackColor = CardBackground;
            panel.Padding = new System.Windows.Forms.Padding(12);
        }

        public static void StyleGroupBox(System.Windows.Forms.GroupBox grp)
        {
            grp.BackColor = CardBackground;
            grp.ForeColor = TextPrimary;
            grp.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
        }

        public static void StyleTextBox(System.Windows.Forms.TextBox txt)
        {
            txt.BackColor = InputBackground;
            txt.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            txt.ForeColor = TextPrimary;
        }

        public static void StyleLabel(System.Windows.Forms.Label lbl, bool isSecondary = false)
        {
            lbl.ForeColor = isSecondary ? TextSecondary : TextPrimary;
        }

        public static void StyleStatusStrip(System.Windows.Forms.StatusStrip strip)
        {
            strip.BackColor = CardBackground;
            strip.ForeColor = TextSecondary;
        }
    }
}
