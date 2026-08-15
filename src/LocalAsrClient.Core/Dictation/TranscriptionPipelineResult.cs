namespace LocalAsrClient.Core.Dictation;

public sealed record TranscriptionPipelineResult(
    bool Succeeded,
    string Text,
    string? ErrorMessage,
    TimeSpan RecordingDuration,
    TimeSpan ProcessingDuration,
    string BackendId,
    string ModelId);
