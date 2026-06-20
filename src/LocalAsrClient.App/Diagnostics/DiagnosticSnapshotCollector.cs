using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using LocalAsrClient.App.TextInjection;

namespace LocalAsrClient.App.Diagnostics;

public static class DiagnosticSnapshotCollector
{
    public static DiagnosticWindowSnapshot Capture()
    {
        var foreground = Win32FocusNative.GetForegroundWindow();
        var info = GetGuiThreadInfo(foreground);
        return new DiagnosticWindowSnapshot(
            Describe(foreground),
            Describe(info.HwndFocus),
            Describe(info.HwndActive),
            Describe(info.HwndCaret));
    }

    public static DiagnosticWindowInfo Describe(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !Win32FocusNative.IsWindow(hwnd))
        {
            return DiagnosticWindowInfo.Empty;
        }

        Win32FocusNative.GetWindowThreadProcessId(hwnd, out var processId);
        var processName = string.Empty;
        try
        {
            processName = Process.GetProcessById((int)processId).ProcessName;
        }
        catch
        {
        }

        return new DiagnosticWindowInfo(
            $"0x{hwnd.ToInt64():X}",
            GetClassName(hwnd),
            (int)processId,
            processName,
            GetWindowTitle(hwnd));
    }

    private static Win32FocusNative.GuiThreadInfo GetGuiThreadInfo(IntPtr foreground)
    {
        var info = new Win32FocusNative.GuiThreadInfo
        {
            CbSize = Marshal.SizeOf<Win32FocusNative.GuiThreadInfo>()
        };

        if (foreground == IntPtr.Zero)
        {
            return info;
        }

        var threadId = Win32FocusNative.GetWindowThreadProcessId(foreground, out _);
        Win32FocusNative.GetGUIThreadInfo(threadId, ref info);
        return info;
    }

    private static string GetClassName(IntPtr hwnd)
    {
        var builder = new StringBuilder(256);
        return Win32FocusNative.GetClassName(hwnd, builder, builder.Capacity) > 0 ? builder.ToString() : string.Empty;
    }

    private static string GetWindowTitle(IntPtr hwnd)
    {
        var length = GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        return GetWindowText(hwnd, builder, builder.Capacity) > 0 ? builder.ToString() : string.Empty;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);
}
