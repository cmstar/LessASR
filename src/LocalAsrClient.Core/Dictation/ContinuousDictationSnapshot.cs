namespace LocalAsrClient.Core.Dictation;

public sealed record ContinuousDictationSnapshot(
    IReadOnlyList<ContinuousDictationSegment> Segments,
    bool IsRecordingActive,
    int CompletedCount,
    int TotalCount,
    string? BannerMessage);
