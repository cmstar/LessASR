namespace LocalAsrClient.App.Overlay;

public enum OverlayState
{
    LoadingModel,
    Ready,
    Recording,
    Transcribing,
    Injecting,
    Injected,
    ResultNeedsAction,
    Error
}
