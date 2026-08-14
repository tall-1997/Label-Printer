using System;
using System.Drawing;

namespace BarTenderPrinter
{
    internal static class UiLayoutPolicy
    {
        public static Rectangle ConstrainToWorkingArea(Rectangle bounds, Rectangle workingArea)
        {
            var width = Math.Max(1, Math.Min(bounds.Width, workingArea.Width));
            var height = Math.Max(1, Math.Min(bounds.Height, workingArea.Height));
            var left = Math.Max(workingArea.Left, Math.Min(bounds.Left, workingArea.Right - width));
            var top = Math.Max(workingArea.Top,
                Math.Min(bounds.Top, workingArea.Bottom - height));
            return new Rectangle(left, top, width, height);
        }

        public static int CalculateInputPanelHeight(int requiredHeight, int availableHeight, int minimumHeight, int maximumHeight)
        {
            var upperBound = Math.Max(1, Math.Min(maximumHeight, availableHeight));
            var lowerBound = Math.Min(upperBound, Math.Max(1, minimumHeight));
            return Math.Max(lowerBound, Math.Min(requiredHeight, upperBound));
        }

        public static int CalculateToolbarHeight(int preferredHeight, int availableHeight, int minimumHeight)
        {
            var maximumHeight = Math.Max(minimumHeight, availableHeight);
            return Math.Max(minimumHeight, Math.Min(preferredHeight, maximumHeight));
        }

        public static string GetPrintCompletionStatus(bool success, bool historySaved, bool uncertain)
        {
            if (success) return historySaved ? "就绪" : "打印作业已提交，历史保存失败";
            if (uncertain) return historySaved ? "打印结果待核查" : "打印结果待核查，历史保存失败";
            return historySaved ? "打印提交失败" : "打印提交失败，历史保存失败";
        }

        public static bool IsUncertainStatus(string status) =>
            string.Equals(status, "UNCERTAIN", StringComparison.OrdinalIgnoreCase);

        public static (int MainWidth, int PreviewWidth) CalculateTileWidths(int availableWidth, int desiredPreviewWidth, int minimumMainWidth, int minimumPreviewWidth)
        {
            availableWidth = Math.Max(2, availableWidth);
            minimumMainWidth = Math.Max(1, Math.Min(minimumMainWidth, availableWidth - 1));
            minimumPreviewWidth = Math.Max(1, Math.Min(minimumPreviewWidth, availableWidth - minimumMainWidth));
            var previewWidth = Math.Max(minimumPreviewWidth, Math.Min(desiredPreviewWidth, availableWidth - minimumMainWidth));
            return (availableWidth - previewWidth, previewWidth);
        }
    }
}
