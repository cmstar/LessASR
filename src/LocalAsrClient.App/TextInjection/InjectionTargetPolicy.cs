namespace LocalAsrClient.App.TextInjection;

internal static class InjectionTargetPolicy
{
    private static readonly HashSet<string> DesktopShellClassNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
    };

    public static bool IsDesktopShellClassName(string className) =>
        DesktopShellClassNames.Contains(className);

    public static bool CanUseCapturedRoot(
        bool hasCapturedWindow,
        bool capturedWindowExists,
        bool belongsToCurrentProcess,
        string rootClassName) =>
        hasCapturedWindow
        && capturedWindowExists
        && !belongsToCurrentProcess
        && !IsDesktopShellClassName(rootClassName);
}
