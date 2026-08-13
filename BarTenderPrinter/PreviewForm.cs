using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace BarTenderPrinter
{
    public sealed class PreviewForm : Form
    {
        private readonly PictureBox _pictureBox;
        private readonly Label _statusLabel;

        public event EventHandler PreviewClosed;

        public PreviewForm()
        {
            Text = "标签预览";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            MinimumSize = new Size(320, 420);
            BackColor = MiuiTheme.Background;

            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 42,
                BackColor = MiuiTheme.CardBackground
            };
            var closeButton = new Button
            {
                Text = "关闭",
                Dock = DockStyle.Right,
                Width = 58,
                FlatStyle = FlatStyle.Flat,
                BackColor = MiuiTheme.CardBackground,
                ForeColor = MiuiTheme.TextSecondary
            };
            closeButton.FlatAppearance.BorderSize = 0;
            closeButton.Click += (sender, args) => Close();
            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12, 11, 12, 8),
                Text = "等待预览",
                BackColor = MiuiTheme.CardBackground,
                ForeColor = MiuiTheme.TextSecondary,
                AutoEllipsis = true
            };
            header.Controls.Add(_statusLabel);
            header.Controls.Add(closeButton);
            _pictureBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = MiuiTheme.CardBackground,
                SizeMode = PictureBoxSizeMode.Zoom,
                Margin = new Padding(12)
            };
            Controls.Add(_pictureBox);
            Controls.Add(header);
            FormClosed += (sender, args) =>
            {
                ReplaceImage(null);
                PreviewClosed?.Invoke(this, EventArgs.Empty);
            };
        }

        public void ShowLoading(string source)
        {
            _statusLabel.Text = $"正在生成预览 | {source}";
            _statusLabel.ForeColor = MiuiTheme.TextSecondary;
        }

        public void ShowError(string message)
        {
            ReplaceImage(null);
            _statusLabel.Text = message;
            _statusLabel.ForeColor = MiuiTheme.Error;
        }

        public void ShowPreview(string imagePath, string source)
        {
            using (var stream = new MemoryStream(File.ReadAllBytes(imagePath)))
            using (var image = Image.FromStream(stream))
                ReplaceImage(new Bitmap(image));
            _statusLabel.Text = source;
            _statusLabel.ForeColor = MiuiTheme.TextPrimary;
        }

        private void ReplaceImage(Image image)
        {
            var previous = _pictureBox.Image;
            _pictureBox.Image = image;
            previous?.Dispose();
        }
    }
}
