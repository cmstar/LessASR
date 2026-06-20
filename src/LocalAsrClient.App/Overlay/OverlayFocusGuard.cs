using LocalAsrClient.App.TextInjection;

namespace LocalAsrClient.App.Overlay;

internal readonly struct OverlayFocusSnapshot
{
    public IntPtr ForegroundWindow { get; init; }

    public IntPtr FocusWindow { get; init; }
}

internal static class OverlayFocusGuard
{
    public static OverlayFocusSnapshot Capture()
    {
        var foreground = Win32FocusNative.GetForegroundWindow();
        return new OverlayFocusSnapshot
        {
            ForegroundWindow = foreground,
            FocusWindow = ResolveFocusWindow(foreground)
        };
    }

    public static void RestoreIfChanged(OverlayFocusSnapshot snapshot, IntPtr overlayWindow)
    {
        if (snapshot.ForegroundWindow == IntPtr.Zero
            || !Win32FocusNative.IsWindow(snapshot.ForegroundWindow))
        {
            return;
        }

        var currentForeground = Win32FocusNative.GetForegroundWindow();
        if (currentForeground == overlayWindow)
        {
            RestoreSnapshot(snapshot);
            return;
        }

        if (currentForeground != snapshot.ForegroundWindow)
        {
            return;
        }

        if (snapshot.FocusWindow == IntPtr.Zero
            || !Win32FocusNative.IsWindow(snapshot.FocusWindow))
        {
            return;
        }

        var currentFocus = ResolveFocusWindow(snapshot.ForegroundWindow);
        if (currentFocus != snapshot.FocusWindow)
        {
            EditableFocusDetector.TryActivateForInjection(snapshot.ForegroundWindow, snapshot.FocusWindow);
        }
    }

    private static void RestoreSnapshot(OverlayFocusSnapshot snapshot)
    {
        var editTarget = snapshot.FocusWindow != IntPtr.Zero && Win32FocusNative.IsWindow(snapshot.FocusWindow)
            ? snapshot.FocusWindow
            : snapshot.ForegroundWindow;
        EditableFocusDetector.TryActivateForInjection(snapshot.ForegroundWindow, editTarget);
    }

    private static IntPtr ResolveFocusWindow(IntPtr foregroundWindow)
    {
        if (foregroundWindow == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var focused = EditableFocusDetector.GetFocusedWindowFromGuiThreadInfo(foregroundWindow);
        if (focused != IntPtr.Zero)
        {
            return focused;
        }

        var threadId = Win32FocusNative.GetWindowThreadProcessId(foregroundWindow, out _);
        var currentThread = Win32FocusNative.GetCurrentThreadId();
        var attached = false;
        if (threadId != currentThread)
        {
            attached = Win32FocusNative.AttachThreadInput(currentThread, threadId, attach: true);
        }

        try
        {
            return Win32FocusNative.GetFocus();
        }
        finally
        {
            if (attached)
            {
                Win32FocusNative.AttachThreadInput(currentThread, threadId, attach: false);
            }
        }
    }
}
