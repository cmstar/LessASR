using LocalAsrClient.App.Hotkeys;

namespace LocalAsrClient.App.Tests.Hotkeys;

public sealed class DictationHotkeyRouterTests
{
    [Fact]
    public void RightAlt_IsIgnoredWhileIndependentDictationWindowIsOpen()
    {
        var calls = new List<string>();
        var router = CreateRouter(calls, independentOpen: true);

        router.HandleRightAlt();

        Assert.Empty(calls);
    }

    [Fact]
    public void F9_IsIgnoredForTheFullLifetimeOfAnInPlaceSession()
    {
        var calls = new List<string>();
        var router = CreateRouter(calls, inPlaceOpen: true);

        router.HandleF9();

        Assert.Empty(calls);
    }

    [Theory]
    [InlineData(true, true, "in-place-boundary")]
    [InlineData(false, true, "independent-boundary")]
    [InlineData(false, false, null)]
    public void RightControl_RoutesOnlyToTheActiveRecordingMode(
        bool inPlaceRecording,
        bool independentOpen,
        string? expectedCall)
    {
        var calls = new List<string>();
        var router = CreateRouter(
            calls,
            inPlaceRecording: inPlaceRecording,
            independentOpen: independentOpen);

        router.HandleRightControl();

        if (expectedCall is null)
        {
            Assert.Empty(calls);
        }
        else
        {
            Assert.Equal(expectedCall, Assert.Single(calls));
        }
    }

    private static DictationHotkeyRouter CreateRouter(
        List<string> calls,
        bool inPlaceOpen = false,
        bool inPlaceRecording = false,
        bool independentOpen = false) => new(
            () => inPlaceOpen,
            () => inPlaceRecording,
            () => independentOpen,
            () => calls.Add("right-alt"),
            () => calls.Add("in-place-boundary"),
            () => calls.Add("independent-boundary"),
            () => calls.Add("f9"));
}
