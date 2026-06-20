using LocalAsrClient.App.Diagnostics;

namespace LocalAsrClient.App.Tests.Diagnostics;

public sealed class RecordingDiagnosticSink : IDiagnosticEventSink
{
    public List<DiagnosticEvent> Events { get; } = [];

    public string? FilePath => null;

    public Task WriteAsync(DiagnosticEvent diagnosticEvent)
    {
        Events.Add(diagnosticEvent);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
