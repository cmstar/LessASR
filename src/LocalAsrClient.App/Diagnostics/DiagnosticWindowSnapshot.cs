namespace LocalAsrClient.App.Diagnostics;

public sealed record DiagnosticWindowSnapshot(
    DiagnosticWindowInfo ForegroundWindow,
    DiagnosticWindowInfo FocusWindow,
    DiagnosticWindowInfo ActiveWindow,
    DiagnosticWindowInfo CaretWindow)
{
    public static DiagnosticWindowSnapshot Empty { get; } = new(
        DiagnosticWindowInfo.Empty,
        DiagnosticWindowInfo.Empty,
        DiagnosticWindowInfo.Empty,
        DiagnosticWindowInfo.Empty);
}

public sealed record DiagnosticWindowInfo(
    string Hwnd,
    string ClassName,
    int ProcessId,
    string ProcessName,
    string WindowTitle)
{
    public static DiagnosticWindowInfo Empty { get; } = new("0x0", string.Empty, 0, string.Empty, string.Empty);
}
