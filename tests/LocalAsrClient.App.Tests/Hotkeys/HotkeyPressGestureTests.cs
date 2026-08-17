using LocalAsrClient.App.Hotkeys;

namespace LocalAsrClient.App.Tests.Hotkeys;

public sealed class HotkeyPressGestureTests
{
    private const int TargetKey = 0xA3;
    private const int OtherKey = 0x43;

    [Fact]
    public void TargetKey_TriggersOnlyAfterKeyUp()
    {
        var gesture = new HotkeyPressGesture(TargetKey);

        Assert.False(gesture.Process(Win32HotkeyNative.WmKeyDown, TargetKey));
        Assert.True(gesture.Process(Win32HotkeyNative.WmKeyUp, TargetKey));
    }

    [Fact]
    public void RepeatedKeyDown_StillTriggersOnlyOnceOnKeyUp()
    {
        var gesture = new HotkeyPressGesture(TargetKey);

        Assert.False(gesture.Process(Win32HotkeyNative.WmKeyDown, TargetKey));
        Assert.False(gesture.Process(Win32HotkeyNative.WmKeyDown, TargetKey));
        Assert.True(gesture.Process(Win32HotkeyNative.WmKeyUp, TargetKey));
        Assert.False(gesture.Process(Win32HotkeyNative.WmKeyUp, TargetKey));
    }

    [Fact]
    public void OtherKeyPressedDuringTargetPress_CancelsGesture()
    {
        var gesture = new HotkeyPressGesture(TargetKey);

        Assert.False(gesture.Process(Win32HotkeyNative.WmKeyDown, TargetKey));
        Assert.False(gesture.Process(Win32HotkeyNative.WmKeyDown, OtherKey));
        Assert.False(gesture.Process(Win32HotkeyNative.WmKeyUp, OtherKey));
        Assert.False(gesture.Process(Win32HotkeyNative.WmKeyUp, TargetKey));
    }

    [Fact]
    public void TargetPressedWhileOtherKeyIsDown_DoesNotTrigger()
    {
        var gesture = new HotkeyPressGesture(TargetKey);

        Assert.False(gesture.Process(Win32HotkeyNative.WmKeyDown, OtherKey));
        Assert.False(gesture.Process(Win32HotkeyNative.WmKeyDown, TargetKey));
        Assert.False(gesture.Process(Win32HotkeyNative.WmKeyUp, OtherKey));
        Assert.False(gesture.Process(Win32HotkeyNative.WmKeyUp, TargetKey));
    }

    [Fact]
    public void NewSoloPressAfterCancelledGesture_TriggersNormally()
    {
        var gesture = new HotkeyPressGesture(TargetKey);

        gesture.Process(Win32HotkeyNative.WmKeyDown, TargetKey);
        gesture.Process(Win32HotkeyNative.WmKeyDown, OtherKey);
        gesture.Process(Win32HotkeyNative.WmKeyUp, OtherKey);
        Assert.False(gesture.Process(Win32HotkeyNative.WmKeyUp, TargetKey));

        Assert.False(gesture.Process(Win32HotkeyNative.WmKeyDown, TargetKey));
        Assert.True(gesture.Process(Win32HotkeyNative.WmKeyUp, TargetKey));
    }

    [Fact]
    public void SystemKeyMessages_AreTreatedAsACompletePress()
    {
        var gesture = new HotkeyPressGesture(TargetKey);

        Assert.False(gesture.Process(Win32HotkeyNative.WmSysKeyDown, TargetKey));
        Assert.True(gesture.Process(Win32HotkeyNative.WmSysKeyUp, TargetKey));
    }

    [Fact]
    public void Reset_DiscardsAnIncompletePress()
    {
        var gesture = new HotkeyPressGesture(TargetKey);

        gesture.Process(Win32HotkeyNative.WmKeyDown, TargetKey);
        gesture.Reset();

        Assert.False(gesture.Process(Win32HotkeyNative.WmKeyUp, TargetKey));
    }

    [Fact]
    public void SuppressedSoloGesture_SuppressesBothTargetEdges()
    {
        var gesture = new HotkeyPressGesture(TargetKey, suppressSoloPress: true);

        gesture.Process(Win32HotkeyNative.WmKeyDown, TargetKey);
        Assert.True(gesture.ShouldSuppressCurrentEvent);

        Assert.True(gesture.Process(Win32HotkeyNative.WmKeyUp, TargetKey));
        Assert.True(gesture.ShouldSuppressCurrentEvent);
    }

    [Fact]
    public void SuppressedGesture_WithExistingOtherKey_PassesTargetEdgesThrough()
    {
        var gesture = new HotkeyPressGesture(TargetKey, suppressSoloPress: true);

        gesture.Process(Win32HotkeyNative.WmKeyDown, OtherKey);
        gesture.Process(Win32HotkeyNative.WmKeyDown, TargetKey);
        Assert.False(gesture.ShouldSuppressCurrentEvent);

        Assert.False(gesture.Process(Win32HotkeyNative.WmKeyUp, TargetKey));
        Assert.False(gesture.ShouldSuppressCurrentEvent);
    }

    [Fact]
    public void RightControl_IsModifierButF9IsNot()
    {
        Assert.True(Win32HotkeyNative.IsModifierKey(Win32HotkeyNative.VkRControl));
        Assert.False(Win32HotkeyNative.IsModifierKey(Win32HotkeyNative.VkF9));
    }
}
