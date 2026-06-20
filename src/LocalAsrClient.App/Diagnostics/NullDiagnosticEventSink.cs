namespace LocalAsrClient.App.Diagnostics;

public sealed class NullDiagnosticEventSink : IDiagnosticEventSink
{
    public static NullDiagnosticEventSink Instance { get; } = new();

    private NullDiagnosticEventSink()
    {
    }

    public string? FilePath => null;

    public Task WriteAsync(DiagnosticEvent diagnosticEvent) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
