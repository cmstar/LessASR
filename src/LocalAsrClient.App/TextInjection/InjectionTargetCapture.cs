using System.Diagnostics;
using LocalAsrClient.App.Diagnostics;

namespace LocalAsrClient.App.TextInjection;

public sealed class InjectionTargetCapture
{
    private static readonly uint CurrentProcessId = (uint)Process.GetCurrentProcess().Id;
    private readonly IDiagnosticEventSink _diagnostics;

    public InjectionTargetCapture()
        : this(NullDiagnosticEventSink.Instance)
    {
    }

    public InjectionTargetCapture(IDiagnosticEventSink diagnostics)
    {
        _diagnostics = diagnostics;
    }

    public IntPtr ForegroundWindow { get; private set; }
    public IntPtr FocusWindow { get; private set; }
    public IntPtr RawFocusWindow { get; private set; }

    public void Capture()
    {
        _ = _diagnostics.WriteAsync(CreateEvent("InjectionTargetCapture.Before", null));

        var foreground = Win32FocusNative.GetForegroundWindow();
        if (foreground == IntPtr.Zero || BelongsToCurrentProcess(foreground))
        {
            ForegroundWindow = IntPtr.Zero;
            FocusWindow = IntPtr.Zero;
            RawFocusWindow = IntPtr.Zero;
            WriteAfterEvent();
            return;
        }

        ForegroundWindow = foreground;
        RawFocusWindow = EditableFocusDetector.GetRawFocusedWindow(foreground);
        FocusWindow = EditableFocusDetector.ResolveEditableTarget(foreground);

        WriteAfterEvent();
    }

    public void Clear()
    {
        ForegroundWindow = IntPtr.Zero;
        FocusWindow = IntPtr.Zero;
        RawFocusWindow = IntPtr.Zero;
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

    private void WriteAfterEvent()
    {
        _ = _diagnostics.WriteAsync(CreateEvent("InjectionTargetCapture.After", new Dictionary<string, string?>
        {
            ["foregroundWindow"] = $"0x{ForegroundWindow.ToInt64():X}",
            ["rawFocusWindow"] = $"0x{RawFocusWindow.ToInt64():X}",
            ["rawFocusClassName"] = EditableFocusDetector.GetClassName(RawFocusWindow),
            ["focusWindow"] = $"0x{FocusWindow.ToInt64():X}",
            ["focusClassName"] = EditableFocusDetector.GetClassName(FocusWindow)
        }));
    }

    private DiagnosticEvent CreateEvent(string eventName, IReadOnlyDictionary<string, string?>? properties)
    {
        return new DiagnosticEvent(
            0,
            DateTimeOffset.Now,
            eventName,
            null,
            Environment.CurrentManagedThreadId,
            DiagnosticSnapshotCollector.Capture(),
            properties ?? new Dictionary<string, string?>());
    }

    private static bool BelongsToCurrentProcess(IntPtr hwnd)
    {
        Win32FocusNative.GetWindowThreadProcessId(hwnd, out var processId);
        return processId == CurrentProcessId;
    }
}
