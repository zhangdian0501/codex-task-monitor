using System.Windows;

namespace CodexTaskMonitor.App.Services;

internal static class WindowPlacement
{
    private const double EdgeMargin = 16;

    public static void PlaceBottomRight(Window window)
    {
        window.UpdateLayout();
        var workArea = SystemParameters.WorkArea;
        var width = window.ActualWidth > 0 ? window.ActualWidth : window.Width;
        var height = window.ActualHeight > 0 ? window.ActualHeight : window.Height;

        window.Left = Math.Max(workArea.Left, workArea.Right - width - EdgeMargin);
        window.Top = Math.Max(workArea.Top, workArea.Bottom - height - EdgeMargin);
    }
}
