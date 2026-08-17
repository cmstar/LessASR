namespace LocalAsrClient.App.Overlay;

public enum OverlayState
{
    LoadingModel,
    Recording,
    Reviewing,
    Transcribing,
    Injecting,
    Injected,
    ResultNeedsAction,
    Error
}
