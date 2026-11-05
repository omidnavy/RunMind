using System;
using System.Runtime.InteropServices;
using System.Windows;

namespace RunMind.Shared;

/// <summary>
/// Places newly-created RunMind windows inside the working area of the monitor
/// under the pointer. The size is applied only once, during native window creation;
/// subsequent user resizing is never overridden.
/// </summary>
internal static class WindowLayout
{
    private const uint MonitorDefaultToNearest = 2;
    private const double SafeMarginDip = 12;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("shcore.dll", SetLastError = true)]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    public static void ConfigureInitialPlacement(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.SourceInitialized += (_, _) => ApplyInitialPlacement(window);
    }

    private static void ApplyInitialPlacement(Window window)
    {
        try
        {
            var workArea = GetMonitorWorkArea(window);
            if (workArea.Width <= 0 || workArea.Height <= 0)
                workArea = SystemParameters.WorkArea;
            if (workArea.Width <= 0 || workArea.Height <= 0)
                return;

            var availableWidth = Math.Max(1, workArea.Width - (SafeMarginDip * 2));
            var availableHeight = Math.Max(1, workArea.Height - (SafeMarginDip * 2));
            var width = Math.Min(window.Width, availableWidth);
            var height = Math.Min(window.Height, availableHeight);

            // If the monitor is smaller than the normal minimum, lower the minimum
            // only enough to allow the title bar and close controls to remain reachable.
            if (window.MinWidth > width) window.MinWidth = width;
            if (window.MinHeight > height) window.MinHeight = height;

            window.Width = width;
            window.Height = height;
            window.Left = workArea.Left + ((workArea.Width - width) / 2);
            window.Top = workArea.Top + ((workArea.Height - height) / 2);
        }
        catch
        {
            // Keep WPF's normal placement if monitor discovery fails.
        }
    }

    private static Rect GetMonitorWorkArea(Window window)
    {
        if (!GetCursorPos(out var cursor))
            return SystemParameters.WorkArea;

        var monitor = MonitorFromPoint(cursor, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
            return SystemParameters.WorkArea;

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
            return SystemParameters.WorkArea;

        var scaleX = 1.0;
        var scaleY = 1.0;
        try
        {
            // Read the DPI of the selected monitor, not the monitor on which the
            // not-yet-shown WPF HWND happened to be created.
            if (GetDpiForMonitor(monitor, 0, out var dpiX, out var dpiY) == 0)
            {
                if (dpiX > 0) scaleX = dpiX / 96.0;
                if (dpiY > 0) scaleY = dpiY / 96.0;
            }
        }
        catch
        {
            // Older Windows builds fall back to 96-DPI coordinate conversion.
        }

        return new Rect(
            info.Work.Left / scaleX,
            info.Work.Top / scaleY,
            (info.Work.Right - info.Work.Left) / scaleX,
            (info.Work.Bottom - info.Work.Top) / scaleY);
    }
}
