namespace LocalAsrClient.Core.Dictation;

public enum DictationState
{
    Idle,
    EnsuringModelReady,
    Ready,
    Recording,
    Transcribing,
    Injecting,
    ResultNeedsAction,
    Error
}