namespace LocalAsrClient.Core.Dictation;

public sealed record ContinuousDictationSegment(
    Guid Id,
    ContinuousSegmentState State,
    string Text,
    string? ErrorMessage);
