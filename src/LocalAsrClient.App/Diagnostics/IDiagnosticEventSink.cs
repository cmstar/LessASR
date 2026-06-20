namespace LocalAsrClient.App.Diagnostics;

public interface IDiagnosticEventSink : IAsyncDisposable
{
    string? FilePath { get; }

    Task WriteAsync(DiagnosticEvent diagnosticEvent);
}
