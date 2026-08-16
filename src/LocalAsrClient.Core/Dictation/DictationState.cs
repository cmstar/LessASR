namespace LocalAsrClient.Core.Dictation;

public enum DictationState
{
    Idle,
    EnsuringModelReady,
    Recording,
    Transcribing,
    Injecting,
    ResultNeedsAction,
    Error
}
