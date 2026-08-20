using System.Runtime.InteropServices;

namespace LocalAsrClient.App.Overlay;

internal readonly record struct OverlayPixelRectangle(
    int Left,
    int Top,
    int Right,
    int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;
}

internal readonly record struct OverlayPixelPosition(int Left, int Top);

internal static class OverlayPlacementCalculator
{
    public static OverlayPixelPosition BottomCenter(
        OverlayPixelRectangle workingArea,
        int windowWidth,
        int windowHeight,
        int topMargin,
        int bottomMargin)
    {
        var left = workingArea.Left + ((workingArea.Width - windowWidth) / 2);
        var top = workingArea.Bottom - windowHeight - bottomMargin;
        top = Math.Max(top, workingArea.Top + topMargin);

        return new OverlayPixelPosition(left, top);
    }

    public static int DevicePixels(double deviceIndependentPixels, uint dpi)
    {
        var effectiveDpi = dpi == 0 ? OverlayMonitorPlacement.DefaultDpi : dpi;
        return Math.Max(
            0,
            (int)Math.Round(
                deviceIndependentPixels * effectiveDpi / OverlayMonitorPlacement.DefaultDpi,
                MidpointRounding.AwayFromZero));
    }
}

internal static class OverlayMonitorPlacement
{
    internal const uint DefaultDpi = 96;

    private const uint MonitorDefaultToNearest = 0x00000002;
    private const uint MonitorDefaultToPrimary = 0x00000001;

    public static IntPtr CaptureMonitor(IntPtr targetWindow)
    {
        if (targetWindow != IntPtr.Zero)
        {
            var targetMonitor = MonitorFromWindow(targetWindow, MonitorDefaultToNearest);
            if (targetMonitor != IntPtr.Zero)
            {
                return targetMonitor;
            }
        }

        if (GetCursorPos(out var cursorPosition))
        {
            var cursorMonitor = MonitorFromPoint(cursorPosition, MonitorDefaultToNearest);
            if (cursorMonitor != IntPtr.Zero)
            {
                return cursorMonitor;
            }
        }

        return MonitorFromWindow(IntPtr.Zero, MonitorDefaultToPrimary);
    }

    public static bool TryGetWorkingArea(IntPtr monitor, out OverlayPixelRectangle workingArea)
    {
        var monitorInfo = new NativeMonitorInfo
        {
            Size = Marshal.SizeOf<NativeMonitorInfo>()
        };

        if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref monitorInfo))
        {
            workingArea = monitorInfo.Work.ToOverlayRectangle();
            return true;
        }

        workingArea = default;
        return false;
    }

    public static bool TryGetWindowBounds(IntPtr window, out OverlayPixelRectangle bounds)
    {
        if (window != IntPtr.Zero && GetWindowRect(window, out var windowBounds))
        {
            bounds = windowBounds.ToOverlayRectangle();
            return true;
        }

        bounds = default;
        return false;
    }

    public static uint GetWindowDpiOrDefault(IntPtr window)
    {
        var dpi = window == IntPtr.Zero ? 0 : GetDpiForWindow(window);
        return dpi == 0 ? DefaultDpi : dpi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly OverlayPixelRectangle ToOverlayRectangle() =>
            new(Left, Top, Right, Bottom);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct NativeMonitorInfo
    {
        public int Size;
        public NativeRectangle Monitor;
        public NativeRectangle Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref NativeMonitorInfo monitorInfo);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRectangle rectangle);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);
}
