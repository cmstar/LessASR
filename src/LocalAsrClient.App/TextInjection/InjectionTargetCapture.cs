using System.Diagnostics;

namespace LocalAsrClient.App.TextInjection;

public sealed class InjectionTargetCapture
{
    private static readonly uint CurrentProcessId = (uint)Process.GetCurrentProcess().Id;

    public IntPtr ForegroundWindow { get; private set; }
    public IntPtr FocusWindow { get; private set; }

    public void Capture()
    {
        var foreground = Win32FocusNative.GetForegroundWindow();
        if (foreground == IntPtr.Zero || BelongsToCurrentProcess(foreground))
        {
            ForegroundWindow = IntPtr.Zero;
            FocusWindow = IntPtr.Zero;
            return;
        }

        ForegroundWindow = foreground;

        var focused = EditableFocusDetector.GetFocusedWindowFromGuiThreadInfo(foreground);
        FocusWindow = EditableFocusDetector.IsEditableWindow(focused)
            ? focused
            : EditableFocusDetector.ResolveEditableTarget(foreground);
    }

    public void Clear()
    {
        ForegroundWindow = IntPtr.Zero;
        FocusWindow = IntPtr.Zero;
    }

    public bool HasCapturedTarget => ForegroundWindow != IntPtr.Zero;

    public IntPtr GetInjectionTarget()
    {
        if (EditableFocusDetector.IsEditableWindow(FocusWindow))
        {
            return FocusWindow;
        }

        var fromCapturedRoot = EditableFocusDetector.ResolveEditableTarget(ForegroundWindow);
        if (EditableFocusDetector.IsEditableWindow(fromCapturedRoot))
        {
            return fromCapturedRoot;
        }

        return IntPtr.Zero;
    }

    public IntPtr GetRootWindow()
    {
        if (ForegroundWindow != IntPtr.Zero && !BelongsToCurrentProcess(ForegroundWindow))
        {
            return ForegroundWindow;
        }

        var foreground = Win32FocusNative.GetForegroundWindow();
        return BelongsToCurrentProcess(foreground) ? IntPtr.Zero : foreground;
    }

    private static bool BelongsToCurrentProcess(IntPtr hwnd)
    {
        Win32FocusNative.GetWindowThreadProcessId(hwnd, out var processId);
        return processId == CurrentProcessId;
    }
}
