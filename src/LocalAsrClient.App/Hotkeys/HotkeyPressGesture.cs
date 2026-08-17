namespace LocalAsrClient.App.Hotkeys;

/// <summary>
/// Recognizes either a complete unmodified press or an exclusively captured initial key-down.
/// </summary>
internal sealed class HotkeyPressGesture
{
    private readonly HashSet<int> _pressedKeys = [];
    private readonly bool _captureTargetKeyExclusively;
    private readonly bool _suppressSoloPress;
    private readonly int _targetVirtualKeyCode;
    private bool _isChord;
    private bool _suppressCurrentPress;
    private bool _targetPressStarted;

    public HotkeyPressGesture(
        int targetVirtualKeyCode,
        bool suppressSoloPress = false,
        bool captureTargetKeyExclusively = false)
    {
        _targetVirtualKeyCode = targetVirtualKeyCode;
        _captureTargetKeyExclusively = captureTargetKeyExclusively;
        // Modifier key edges must always be delivered as a pair. Suppressing only
        // one edge leaves foreground applications believing the modifier is held.
        // Exclusive capture is safe because it suppresses both edges unconditionally.
        _suppressSoloPress = suppressSoloPress
            && !Win32HotkeyNative.IsModifierKey(targetVirtualKeyCode);
    }

    public bool ShouldSuppressCurrentEvent { get; private set; }

    public bool Process(int message, int virtualKeyCode)
    {
        ShouldSuppressCurrentEvent = false;

        if (message is Win32HotkeyNative.WmKeyDown or Win32HotkeyNative.WmSysKeyDown)
        {
            return ProcessKeyDown(virtualKeyCode);
        }

        if (message is Win32HotkeyNative.WmKeyUp or Win32HotkeyNative.WmSysKeyUp)
        {
            return ProcessKeyUp(virtualKeyCode);
        }

        return false;
    }

    public void Reset()
    {
        _pressedKeys.Clear();
        ResetTargetPress();
        ShouldSuppressCurrentEvent = false;
    }

    private bool ProcessKeyDown(int virtualKeyCode)
    {
        var isRepeatedMessage = !_pressedKeys.Add(virtualKeyCode);
        if (virtualKeyCode == _targetVirtualKeyCode)
        {
            if (_captureTargetKeyExclusively)
            {
                ShouldSuppressCurrentEvent = true;
                return !isRepeatedMessage;
            }

            if (!isRepeatedMessage)
            {
                _targetPressStarted = true;
                _isChord = _pressedKeys.Any(key => key != _targetVirtualKeyCode);
                _suppressCurrentPress = _suppressSoloPress && !_isChord;
            }

            ShouldSuppressCurrentEvent = _suppressCurrentPress;
            return false;
        }

        if (_targetPressStarted)
        {
            _isChord = true;
        }

        return false;
    }

    private bool ProcessKeyUp(int virtualKeyCode)
    {
        _pressedKeys.Remove(virtualKeyCode);
        if (virtualKeyCode != _targetVirtualKeyCode)
        {
            return false;
        }

        if (_captureTargetKeyExclusively)
        {
            ShouldSuppressCurrentEvent = true;
            ResetTargetPress();
            return false;
        }

        var triggered = _targetPressStarted && !_isChord;
        ShouldSuppressCurrentEvent = _suppressCurrentPress;
        ResetTargetPress();
        return triggered;
    }

    private void ResetTargetPress()
    {
        _targetPressStarted = false;
        _isChord = false;
        _suppressCurrentPress = false;
    }
}
