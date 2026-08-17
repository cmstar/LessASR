namespace LocalAsrClient.App.Hotkeys;

public sealed class DictationHotkeyRouter
{
    private readonly Func<bool> _isInPlaceSessionOpen;
    private readonly Func<bool> _isInPlaceRecording;
    private readonly Func<bool> _isIndependentWindowOpen;
    private readonly Action _toggleInPlace;
    private readonly Action _commitInPlaceBoundary;
    private readonly Action _commitIndependentBoundary;
    private readonly Action _toggleIndependent;

    public DictationHotkeyRouter(
        Func<bool> isInPlaceSessionOpen,
        Func<bool> isInPlaceRecording,
        Func<bool> isIndependentWindowOpen,
        Action toggleInPlace,
        Action commitInPlaceBoundary,
        Action commitIndependentBoundary,
        Action toggleIndependent)
    {
        _isInPlaceSessionOpen = isInPlaceSessionOpen;
        _isInPlaceRecording = isInPlaceRecording;
        _isIndependentWindowOpen = isIndependentWindowOpen;
        _toggleInPlace = toggleInPlace;
        _commitInPlaceBoundary = commitInPlaceBoundary;
        _commitIndependentBoundary = commitIndependentBoundary;
        _toggleIndependent = toggleIndependent;
    }

    public void HandleRightAlt()
    {
        if (!_isIndependentWindowOpen())
        {
            _toggleInPlace();
        }
    }

    public void HandleRightControl()
    {
        if (_isInPlaceRecording())
        {
            _commitInPlaceBoundary();
        }
        else if (_isIndependentWindowOpen())
        {
            _commitIndependentBoundary();
        }
    }

    public void HandleF9()
    {
        if (!_isInPlaceSessionOpen())
        {
            _toggleIndependent();
        }
    }
}
