using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BarTenderPrinter
{
    public static class MiuiTheme
    {
        private static readonly Font BodyFont = new Font("Microsoft YaHei UI", 9.25F, FontStyle.Regular);
        private static readonly Font ButtonFont = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);
        private static readonly Font ButtonBoldFont = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
        private static readonly Font GridHeaderFont = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
        private static readonly Font CaptionFont = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular);
        private static readonly Font BadgeFont = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold);
        private static readonly Font ProductFont = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold);
        private static readonly Font EmphasisFont = new Font(BodyFont, FontStyle.Bold);
        private static readonly Font DialogTitleFont = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
        private static readonly Font DragHandleFont = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
        public static Font SectionFont => ButtonBoldFont;
        public static Font SecondaryFont => CaptionFont;
        public static Font VersionFont => BadgeFont;
        public static Font ProductTitleFont => ProductFont;
        public static Font EmphasizedBodyFont => EmphasisFont;
        public static Font DialogHeadingFont => DialogTitleFont;
        public static Font DragHandleTextFont => DragHandleFont;
        public static readonly Color Primary = Color.FromArgb(37, 99, 235);
        public static readonly Color PrimaryDark = Color.FromArgb(29, 78, 216);
        public static readonly Color PrimaryLight = Color.FromArgb(235, 243, 255);
        public static readonly Color Accent = Color.FromArgb(99, 102, 241);
        public static readonly Color Background = Color.FromArgb(244, 247, 251);
        public static readonly Color CardBackground = Color.White;
        public static readonly Color Sidebar = Color.FromArgb(248, 250, 252);
        public static readonly Color SidebarHover = Color.FromArgb(235, 243, 255);
        public static readonly Color TextPrimary = Color.FromArgb(25, 35, 52);
        public static readonly Color TextSecondary = Color.FromArgb(100, 116, 139);
        public static readonly Color TextHint = Color.FromArgb(148, 163, 184);
        public static readonly Color Success = Color.FromArgb(34, 197, 94);
        public static readonly Color Error = Color.FromArgb(239, 68, 68);
        public static readonly Color Warning = Color.FromArgb(245, 158, 11);
        public static readonly Color WarningLight = Color.FromArgb(255, 247, 237);
        public static readonly Color WarningText = Color.FromArgb(154, 52, 18);
        public static readonly Color Divider = Color.FromArgb(226, 232, 240);
        public static readonly Color InputBackground = Color.FromArgb(248, 250, 252);
        public static readonly Color Border = Color.FromArgb(203, 213, 225);

        public static void ApplyTheme(System.Windows.Forms.Form form)
        {
            form.BackColor = Background;
            form.Font = BodyFont;
            form.ForeColor = TextPrimary;
            StyleControls(form.Controls);
        }

        public static void StyleButton(System.Windows.Forms.Button btn, bool isPrimary = false)
        {
            var scale = Math.Max(1F, btn.DeviceDpi / 96F);
            int S(int value) => (int)Math.Round(value * scale);
            if (isPrimary)
            {
                btn.BackColor = Primary;
                btn.ForeColor = Color.White;
                btn.FlatStyle = FlatStyle.Flat;
                btn.FlatAppearance.BorderColor = PrimaryDark;
                btn.FlatAppearance.BorderSize = 1;
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
            btn.Font = isPrimary ? ButtonBoldFont : ButtonFont;
            btn.TextImageRelation = TextImageRelation.ImageBeforeText;
            btn.ImageAlign = ContentAlignment.MiddleCenter;
            btn.TextAlign = ContentAlignment.MiddleCenter;
            btn.Padding = new Padding(btn.Image == null ? S(8) : S(6), 0, S(8), 0);
            btn.FlatAppearance.MouseOverBackColor = isPrimary ? PrimaryDark : PrimaryLight;
            btn.FlatAppearance.MouseDownBackColor = isPrimary ? Color.FromArgb(30, 64, 175) : Color.FromArgb(219, 234, 254);
            btn.Resize -= RoundedControl_Resize;
            var previousRegion = btn.Region;
            btn.Region = null;
            previousRegion?.Dispose();
        }

        public static void StyleCard(System.Windows.Forms.Panel panel)
        {
            panel.BackColor = CardBackground;
            panel.Padding = new System.Windows.Forms.Padding(12);
        }

        public static void StyleNavigationButton(Button button, bool isActive)
        {
            button.BackColor = isActive ? Primary : Sidebar;
            button.ForeColor = isActive ? Color.White : TextPrimary;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = isActive ? PrimaryDark : SidebarHover;
            button.FlatAppearance.MouseDownBackColor = isActive ? PrimaryDark : PrimaryLight;
            button.Cursor = Cursors.Hand;
            button.Font = isActive ? ButtonBoldFont : ButtonFont;
            button.TextImageRelation = TextImageRelation.ImageBeforeText;
            button.ImageAlign = ContentAlignment.MiddleLeft;
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Padding = new Padding(12, 0, 10, 0);
            button.Resize -= RoundedControl_Resize;
            button.Resize += RoundedControl_Resize;
            ApplyRoundedRegion(button, 8);
        }

        public static void StyleGroupBox(System.Windows.Forms.GroupBox grp)
        {
            grp.BackColor = CardBackground;
            grp.ForeColor = TextPrimary;
            grp.Font = ButtonBoldFont;
        }

        public static void StyleTextBox(System.Windows.Forms.TextBox txt)
        {
            txt.BackColor = InputBackground;
            txt.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            txt.ForeColor = TextPrimary;
        }

        public static void StyleComboBox(ComboBox combo)
        {
            combo.BackColor = CardBackground;
            combo.ForeColor = TextPrimary;
            combo.FlatStyle = FlatStyle.Standard;
        }

        public static void StyleNumericUpDown(NumericUpDown number)
        {
            number.BackColor = CardBackground;
            number.ForeColor = TextPrimary;
            number.BorderStyle = BorderStyle.FixedSingle;
        }

        public static void StyleCheckBox(CheckBox checkBox)
        {
            checkBox.ForeColor = checkBox.Enabled ? TextPrimary : TextSecondary;
            checkBox.FlatStyle = FlatStyle.Flat;
            checkBox.Cursor = Cursors.Hand;
        }

        public static void StyleTabControl(TabControl tabs, int dpi = 96)
        {
            tabs.Font = ButtonBoldFont;
            tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            tabs.SizeMode = TabSizeMode.Fixed;
            var scale = Math.Max(1F, dpi / 96F);
            tabs.ItemSize = new Size((int)Math.Round(110 * scale), (int)Math.Round(34 * scale));
            tabs.DrawItem -= DrawModernTab;
            tabs.DrawItem += DrawModernTab;
            foreach (TabPage page in tabs.TabPages) page.BackColor = CardBackground;
        }

        public static void RefreshDpi(Control root, int dpi)
        {
            foreach (Control control in root.Controls)
            {
                if (control is TabControl tabs) StyleTabControl(tabs, dpi);
                if (control is DataGridView grid) StyleDataGridView(grid);
                if (control.HasChildren) RefreshDpi(control, dpi);
            }
        }

        private static void DrawModernTab(object sender, DrawItemEventArgs e)
        {
            if (sender is not TabControl tabs || e.Index < 0 || e.Index >= tabs.TabPages.Count) return;
            var selected = e.Index == tabs.SelectedIndex;
            var bounds = e.Bounds;
            var scale = Math.Max(1F, tabs.DeviceDpi / 96F);
            var inset = (int)Math.Round(14 * scale);
            var indicatorHeight = Math.Max(3, (int)Math.Round(3 * scale));
            using (var background = new SolidBrush(selected ? CardBackground : Background))
                e.Graphics.FillRectangle(background, bounds);
            TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, selected ? ButtonBoldFont : ButtonFont,
                bounds, selected ? Primary : TextSecondary, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (selected)
            {
                using (var accent = new SolidBrush(Primary))
                    e.Graphics.FillRectangle(accent, bounds.Left + inset, bounds.Bottom - indicatorHeight, Math.Max(1, bounds.Width - inset * 2), indicatorHeight);
            }
        }

        public static void StyleDataGridView(DataGridView grid)
        {
            var scale = Math.Max(1F, grid.DeviceDpi / 96F);
            int S(int value) => (int)Math.Round(value * scale);
            grid.BackgroundColor = CardBackground;
            grid.BorderStyle = BorderStyle.FixedSingle;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.Single;
            grid.GridColor = Border;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            grid.ColumnHeadersHeight = S(38);
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(241, 245, 249);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = TextSecondary;
            grid.ColumnHeadersDefaultCellStyle.Font = GridHeaderFont;
            grid.RowTemplate.Height = S(34);
            grid.DefaultCellStyle.BackColor = CardBackground;
            grid.DefaultCellStyle.ForeColor = TextPrimary;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(224, 236, 255);
            grid.DefaultCellStyle.SelectionForeColor = TextPrimary;
            grid.DefaultCellStyle.Padding = new Padding(S(5), S(2), S(5), S(2));
        }

        public static void StyleLabel(System.Windows.Forms.Label lbl, bool isSecondary = false)
        {
            lbl.ForeColor = isSecondary ? TextSecondary : TextPrimary;
        }

        public static void StyleStatusStrip(System.Windows.Forms.StatusStrip strip)
        {
            strip.BackColor = CardBackground;
            strip.ForeColor = TextSecondary;
            strip.SizingGrip = false;
            strip.Padding = new Padding(12, 3, 12, 3);
            foreach (ToolStripItem item in strip.Items)
                item.Margin = new Padding(6, 0, 6, 0);
        }

        private static void StyleControls(Control.ControlCollection controls)
        {
            foreach (Control control in controls)
            {
                switch (control)
                {
                    case TextBox textBox: StyleTextBox(textBox); break;
                    case ComboBox comboBox: StyleComboBox(comboBox); break;
                    case NumericUpDown number: StyleNumericUpDown(number); break;
                    case CheckBox checkBox: StyleCheckBox(checkBox); break;
                    case DataGridView grid: StyleDataGridView(grid); break;
                    case TabControl tabs: StyleTabControl(tabs, tabs.DeviceDpi); break;
                }
                if (control.HasChildren) StyleControls(control.Controls);
            }
        }

        private static void RoundedControl_Resize(object sender, System.EventArgs e)
        {
            if (sender is Control control) ApplyRoundedRegion(control, 8);
        }

        private static void ApplyRoundedRegion(Control control, int radius)
        {
            if (control.Width <= 0 || control.Height <= 0) return;
            var scaledRadius = Math.Min(
                Math.Max(radius, (int)Math.Round(radius * Math.Max(1F, control.DeviceDpi / 96F))),
                Math.Max(1, Math.Min(control.Width, control.Height) / 2));
            var diameter = scaledRadius * 2;
            using (var path = new GraphicsPath())
            {
                path.AddArc(0, 0, diameter, diameter, 180, 90);
                path.AddArc(control.Width - diameter - 1, 0, diameter, diameter, 270, 90);
                path.AddArc(control.Width - diameter - 1, control.Height - diameter - 1, diameter, diameter, 0, 90);
                path.AddArc(0, control.Height - diameter - 1, diameter, diameter, 90, 90);
                path.CloseFigure();
                var previousRegion = control.Region;
                control.Region = new Region(path);
                previousRegion?.Dispose();
            }
        }
    }
}
