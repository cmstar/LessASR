namespace LocalAsrClient.Core.Dictation;

public enum InPlaceDictationState
{
    Idle,
    EnsuringModelReady,
    Recording,
    Finishing,
    Reviewing,
    Injecting,
    ResultNeedsAction,
    Error
}

public sealed record InPlaceDictationStatus(
    InPlaceDictationState State,
    IReadOnlyList<ContinuousDictationSegment> Segments,
    bool IsRecordingActive,
    bool HasSegmented,
    string Message,
    string? ResultText = null,
    string? ErrorMessage = null)
{
    public static InPlaceDictationStatus Idle { get; } = new(
        InPlaceDictationState.Idle,
        [],
        IsRecordingActive: false,
        HasSegmented: false,
        "空闲");
}
