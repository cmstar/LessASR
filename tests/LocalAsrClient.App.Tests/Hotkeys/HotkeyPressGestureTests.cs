using LocalAsrClient.App.Diagnostics;
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
    public void SuppressedSoloNonModifierGesture_SuppressesBothTargetEdges()
    {
        var gesture = new HotkeyPressGesture(Win32HotkeyNative.VkF9, suppressSoloPress: true);

        gesture.Process(Win32HotkeyNative.WmKeyDown, Win32HotkeyNative.VkF9);
        Assert.True(gesture.ShouldSuppressCurrentEvent);

        Assert.True(gesture.Process(Win32HotkeyNative.WmKeyUp, Win32HotkeyNative.VkF9));
        Assert.True(gesture.ShouldSuppressCurrentEvent);
    }

    [Fact]
    public void SuppressedGesture_WithExistingOtherKey_PassesTargetEdgesThrough()
    {
        var gesture = new HotkeyPressGesture(Win32HotkeyNative.VkF9, suppressSoloPress: true);

        gesture.Process(Win32HotkeyNative.WmKeyDown, OtherKey);
        gesture.Process(Win32HotkeyNative.WmKeyDown, Win32HotkeyNative.VkF9);
        Assert.False(gesture.ShouldSuppressCurrentEvent);

        Assert.False(gesture.Process(Win32HotkeyNative.WmKeyUp, Win32HotkeyNative.VkF9));
        Assert.False(gesture.ShouldSuppressCurrentEvent);
    }

    [Fact]
    public void SuppressedRightAltGesture_LeavesAltGrChordUntouched()
    {
        var gesture = new HotkeyPressGesture(Win32HotkeyNative.VkRMenu, suppressSoloPress: true);

        gesture.Process(Win32HotkeyNative.WmKeyDown, Win32HotkeyNative.VkLControl);
        gesture.Process(Win32HotkeyNative.WmSysKeyDown, Win32HotkeyNative.VkRMenu);
        Assert.False(gesture.ShouldSuppressCurrentEvent);

        Assert.False(gesture.Process(Win32HotkeyNative.WmSysKeyUp, Win32HotkeyNative.VkRMenu));
        Assert.False(gesture.ShouldSuppressCurrentEvent);
    }

    [Fact]
    public void RightAltListener_NeverSuppressesModifierEdges()
    {
        using var listener = new GlobalHotkeyListener(
            Win32HotkeyNative.VkRMenu,
            suppressSoloPress: true);

        Assert.False(listener.SuppressesSoloPress);
    }

    [Fact]
    public void ModifierGesture_PassesBothEdgesEvenWhenSuppressionIsRequested()
    {
        var gesture = new HotkeyPressGesture(
            Win32HotkeyNative.VkRMenu,
            suppressSoloPress: true);

        gesture.Process(Win32HotkeyNative.WmSysKeyDown, Win32HotkeyNative.VkRMenu);
        Assert.False(gesture.ShouldSuppressCurrentEvent);
        Assert.True(gesture.Process(Win32HotkeyNative.WmSysKeyUp, Win32HotkeyNative.VkRMenu));
        Assert.False(gesture.ShouldSuppressCurrentEvent);
    }

    [Fact]
    public void ExclusiveModifierGesture_TriggersOnInitialDownAndSuppressesEveryTargetEdge()
    {
        var gesture = new HotkeyPressGesture(
            Win32HotkeyNative.VkRMenu,
            captureTargetKeyExclusively: true);

        Assert.True(gesture.Process(Win32HotkeyNative.WmSysKeyDown, Win32HotkeyNative.VkRMenu));
        Assert.True(gesture.ShouldSuppressCurrentEvent);

        Assert.False(gesture.Process(Win32HotkeyNative.WmSysKeyDown, Win32HotkeyNative.VkRMenu));
        Assert.True(gesture.ShouldSuppressCurrentEvent);

        Assert.False(gesture.Process(Win32HotkeyNative.WmSysKeyUp, Win32HotkeyNative.VkRMenu));
        Assert.True(gesture.ShouldSuppressCurrentEvent);
    }

    [Fact]
    public void ExclusiveRightAltListener_DeclaresDedicatedKeyCapture()
    {
        using var listener = GlobalHotkeyListener.CreateExclusive(
            Win32HotkeyNative.VkRMenu,
            NullDiagnosticEventSink.Instance);

        Assert.True(listener.CapturesTargetKeyExclusively);
        Assert.False(listener.SuppressesSoloPress);
    }

    [Fact]
    public void DictationHotkeys_UseRightAltForToggleAndRightControlForSegmentBoundary()
    {
        Assert.Equal(Win32HotkeyNative.VkRMenu, DictationHotkey.ToggleVirtualKey);
        Assert.Equal("右 Alt", DictationHotkey.ToggleDisplayName);
        Assert.Equal(Win32HotkeyNative.VkRControl, InPlaceSegmentHotkey.VirtualKey);
        Assert.True(Win32HotkeyNative.IsModifierKey(Win32HotkeyNative.VkRControl));
        Assert.True(Win32HotkeyNative.IsModifierKey(Win32HotkeyNative.VkRMenu));
        Assert.False(Win32HotkeyNative.IsModifierKey(Win32HotkeyNative.VkF9));
    }
}
