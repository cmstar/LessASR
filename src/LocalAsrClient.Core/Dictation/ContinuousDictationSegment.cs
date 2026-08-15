namespace LocalAsrClient.Core.Dictation;

public sealed record ContinuousDictationSegment(
    Guid Id,
    ContinuousSegmentState State,
    string Text,
    string? ErrorMessage,
    string? BackendId = null,
    string? ModelId = null);

public sealed record ContinuousDictationHistory(
    string Text,
    string BackendId,
    string ModelId);
