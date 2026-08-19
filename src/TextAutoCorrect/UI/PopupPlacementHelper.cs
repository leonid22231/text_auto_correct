using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using TextAutoCorrect.Native;
using Point = System.Windows.Point;

namespace TextAutoCorrect.UI;

internal static class PopupPlacementHelper
{
    public static void ApplyNoActivateTopmost(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            var style = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExstyle);
            NativeMethods.SetWindowLongPtr(
                handle,
                NativeMethods.GwlExstyle,
                style | NativeMethods.WsExNoActivate | NativeMethods.WsExToolwindow | NativeMethods.WsExTopmost);
        };
    }

    public static void PlaceNearPointer(Window window, double offsetX = 12, double offsetY = 16)
    {
        if (!NativeMethods.GetCursorPos(out var point))
            return;

        var source = PresentationSource.FromVisual(window);
        var fromDevice = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var logicalPoint = fromDevice.Transform(new Point(point.X, point.Y));
        var workArea = GetWorkAreaForPoint(point, fromDevice);

        var margin = 8.0;

        // 1) Попробуем расположить справа/снизу от курсора.
        var targetLeft = logicalPoint.X + offsetX;
        var targetTop = logicalPoint.Y + offsetY;

        window.Left = targetLeft;
        window.Top = targetTop;
        window.UpdateLayout();

        var w = window.ActualWidth;
        var h = window.ActualHeight;

        // 2) Flip по вертикали: если не хватает места снизу — ставим вверх.
        if (targetTop + h > workArea.Bottom - margin)
            targetTop = logicalPoint.Y - h - offsetY;

        // 3) Clamp в workArea (последняя страховка, особенно при разных DPI).
        targetLeft = Math.Min(targetLeft, workArea.Right - w - margin);
        targetLeft = Math.Max(targetLeft, workArea.Left + margin);

        targetTop = Math.Min(targetTop, workArea.Bottom - h - margin);
        targetTop = Math.Max(targetTop, workArea.Top + margin);

        window.Left = targetLeft;
        window.Top = targetTop;
    }

    private static Rect GetWorkAreaForPoint(NativeMethods.POINT point, Matrix fromDevice)
    {
        var monitor = NativeMethods.MonitorFromPoint(point, NativeMethods.MonitorDefaultToNearest);
        var info = new NativeMethods.MONITORINFO
        {
            CbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>()
        };

        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
            return SystemParameters.WorkArea;

        var topLeft = fromDevice.Transform(new Point(info.RcWork.Left, info.RcWork.Top));
        var bottomRight = fromDevice.Transform(new Point(info.RcWork.Right, info.RcWork.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    public static string Compact(string text, int maxLength = 160)
    {
        var normalized = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        while (normalized.Contains("  ", StringComparison.Ordinal))
            normalized = normalized.Replace("  ", " ", StringComparison.Ordinal);

        return normalized.Length <= maxLength ? normalized : normalized[..(maxLength - 3)] + "...";
    }
}
