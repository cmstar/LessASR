namespace LocalAsrClient.App.TextInjection;

internal static class FileExplorerInjectionPolicy
{
    public static bool IsExplorerClassName(string rootClassName)
    {
        return string.Equals(rootClassName, "CabinetWClass", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsExplorerWindow(IntPtr rootWindow)
    {
        return rootWindow != IntPtr.Zero
            && Win32FocusNative.IsWindow(rootWindow)
            && IsExplorerClassName(EditableFocusDetector.GetClassName(rootWindow));
    }

    public static bool ShouldUseClipboardOnly(IntPtr rootWindow, string editClassName)
    {
        return ShouldUseClipboardOnly(EditableFocusDetector.GetClassName(rootWindow), editClassName);
    }

    public static bool ShouldUseClipboardOnly(string rootClassName, string editClassName)
    {
        if (!IsExplorerClassName(rootClassName))
        {
            return false;
        }

        return string.Equals(editClassName, "Edit", StringComparison.OrdinalIgnoreCase)
            || FileExplorerTargetResolver.IsExplorerInputSiteClassName(editClassName);
    }
}
