namespace LocalAsrClient.App.TextInjection;

internal static class FileExplorerTargetResolver
{
    public const string InputSiteWindowClass = "InputSiteWindowClass";

    public static bool IsExplorerInputSiteClassName(string className)
    {
        return string.Equals(className, InputSiteWindowClass, StringComparison.OrdinalIgnoreCase);
    }

    public static IntPtr ResolveInjectionTarget(IntPtr rootWindow, IntPtr capturedEdit, IntPtr rawFocusWindow)
    {
        if (!FileExplorerInjectionPolicy.IsExplorerWindow(rootWindow))
        {
            return capturedEdit;
        }

        if (IsInjectableExplorerFocus(rawFocusWindow))
        {
            return rawFocusWindow;
        }

        if (IsInjectableExplorerFocus(capturedEdit))
        {
            return capturedEdit;
        }

        return IntPtr.Zero;
    }

    private static bool IsInjectableExplorerFocus(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !Win32FocusNative.IsWindow(hwnd))
        {
            return false;
        }

        var className = EditableFocusDetector.GetClassName(hwnd);
        if (IsExplorerInputSiteClassName(className))
        {
            return true;
        }

        return EditableFocusDetector.IsEditableWindow(hwnd);
    }
}
