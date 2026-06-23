namespace LocalAsrClient.Core.Dictation;

public enum ContinuousSegmentState
{
    WaitingInput,
    Transcribing,
    Completed,
    Failed
}
