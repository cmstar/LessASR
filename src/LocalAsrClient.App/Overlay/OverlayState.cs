namespace LocalAsrClient.App.Overlay;

public enum OverlayState
{
    LoadingModel,
    Recording,
    Transcribing,
    Injecting,
    Injected,
    ResultNeedsAction,
    Error
}
