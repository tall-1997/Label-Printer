using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace BarTenderPrinter
{
    internal enum AppIcon
    {
        Export,
        Preview,
        Menu,
        Print,
        Orders,
        Refresh,
        Search,
        Clear,
        Import,
        Reprint,
        Info,
        Log
    }

    internal static class SvgIconRenderer
    {
        public static Bitmap Render(AppIcon icon, Color color, int size = 18)
        {
            var bitmap = new Bitmap(size, size);
            using (var graphics = Graphics.FromImage(bitmap))
            using (var pen = new Pen(color, 1.8F) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.ScaleTransform(size / 24F, size / 24F);
                Draw(graphics, pen, icon);
            }
            return bitmap;
        }

        private static void Draw(Graphics graphics, Pen pen, AppIcon icon)
        {
            switch (icon)
            {
                case AppIcon.Export:
                    graphics.DrawLine(pen, 12, 3, 12, 15);
                    graphics.DrawLine(pen, 8, 7, 12, 3);
                    graphics.DrawLine(pen, 16, 7, 12, 3);
                    graphics.DrawLines(pen, new[] { new PointF(5, 14), new PointF(5, 20), new PointF(19, 20), new PointF(19, 14) });
                    break;
                case AppIcon.Preview:
                    graphics.DrawEllipse(pen, 2, 7, 20, 10);
                    graphics.DrawEllipse(pen, 9, 9, 6, 6);
                    break;
                case AppIcon.Menu:
                    graphics.DrawLine(pen, 4, 6, 20, 6);
                    graphics.DrawLine(pen, 4, 12, 20, 12);
                    graphics.DrawLine(pen, 4, 18, 20, 18);
                    break;
                case AppIcon.Print:
                    graphics.DrawRectangle(pen, 6, 3, 12, 6);
                    graphics.DrawRectangle(pen, 6, 15, 12, 6);
                    graphics.DrawLines(pen, new[] { new PointF(6, 17), new PointF(3, 17), new PointF(3, 9), new PointF(21, 9), new PointF(21, 17), new PointF(18, 17) });
                    graphics.DrawEllipse(pen, 17, 11, 1, 1);
                    break;
                case AppIcon.Orders:
                    graphics.DrawRectangle(pen, 5, 4, 14, 17);
                    graphics.DrawLine(pen, 9, 4, 9, 2);
                    graphics.DrawLine(pen, 15, 4, 15, 2);
                    graphics.DrawLine(pen, 8, 9, 16, 9);
                    graphics.DrawLine(pen, 8, 13, 16, 13);
                    graphics.DrawLine(pen, 8, 17, 13, 17);
                    break;
                case AppIcon.Refresh:
                    graphics.DrawArc(pen, 4, 4, 16, 16, 35, 285);
                    graphics.DrawLine(pen, 16, 3, 20, 4);
                    graphics.DrawLine(pen, 20, 4, 19, 8);
                    break;
                case AppIcon.Search:
                    graphics.DrawEllipse(pen, 4, 4, 12, 12);
                    graphics.DrawLine(pen, 15, 15, 21, 21);
                    break;
                case AppIcon.Clear:
                    graphics.DrawLine(pen, 5, 7, 19, 7);
                    graphics.DrawLine(pen, 9, 4, 15, 4);
                    graphics.DrawLines(pen, new[] { new PointF(7, 7), new PointF(8, 21), new PointF(16, 21), new PointF(17, 7) });
                    break;
                case AppIcon.Import:
                    graphics.DrawLine(pen, 12, 3, 12, 15);
                    graphics.DrawLine(pen, 8, 11, 12, 15);
                    graphics.DrawLine(pen, 16, 11, 12, 15);
                    graphics.DrawLines(pen, new[] { new PointF(5, 15), new PointF(5, 21), new PointF(19, 21), new PointF(19, 15) });
                    break;
                case AppIcon.Reprint:
                    graphics.DrawRectangle(pen, 6, 10, 12, 5);
                    graphics.DrawRectangle(pen, 7, 15, 10, 6);
                    graphics.DrawArc(pen, 4, 2, 16, 12, 205, 250);
                    graphics.DrawLine(pen, 4, 4, 4, 9);
                    graphics.DrawLine(pen, 4, 4, 9, 4);
                    break;
                case AppIcon.Info:
                    graphics.DrawEllipse(pen, 3, 3, 18, 18);
                    graphics.DrawLine(pen, 12, 11, 12, 17);
                    graphics.DrawEllipse(pen, 11.5F, 7, 1, 1);
                    break;
                case AppIcon.Log:
                    graphics.DrawRectangle(pen, 5, 3, 14, 18);
                    graphics.DrawLine(pen, 8, 8, 16, 8);
                    graphics.DrawLine(pen, 8, 12, 16, 12);
                    graphics.DrawLine(pen, 8, 16, 14, 16);
                    break;
            }
        }
    }
}
