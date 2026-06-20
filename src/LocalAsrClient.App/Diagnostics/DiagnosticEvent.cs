namespace LocalAsrClient.App.Diagnostics;

public sealed record DiagnosticEvent(
    long SequenceId,
    DateTimeOffset Timestamp,
    string EventName,
    string? State,
    int ThreadId,
    DiagnosticWindowSnapshot Snapshot,
    IReadOnlyDictionary<string, string?> Properties);
