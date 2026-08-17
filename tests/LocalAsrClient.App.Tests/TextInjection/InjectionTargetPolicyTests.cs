using LocalAsrClient.App.TextInjection;

namespace LocalAsrClient.App.Tests.TextInjection;

public sealed class InjectionTargetPolicyTests
{
    [Theory]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    [InlineData("Shell_TrayWnd")]
    [InlineData("Shell_SecondaryTrayWnd")]
    public void IsDesktopShellClassName_RejectsWindowsShellSurfaces(string className)
    {
        Assert.True(InjectionTargetPolicy.IsDesktopShellClassName(className));
    }

    [Theory]
    [InlineData("Notepad")]
    [InlineData("Chrome_WidgetWin_1")]
    [InlineData("CabinetWClass")]
    [InlineData("")]
    public void IsDesktopShellClassName_DoesNotRejectApplicationWindows(string className)
    {
        Assert.False(InjectionTargetPolicy.IsDesktopShellClassName(className));
    }

    [Fact]
    public void CanUseCapturedRoot_RequiresOriginalValidNonShellWindow()
    {
        Assert.False(InjectionTargetPolicy.CanUseCapturedRoot(
            hasCapturedWindow: false,
            capturedWindowExists: false,
            belongsToCurrentProcess: false,
            rootClassName: ""));
        Assert.False(InjectionTargetPolicy.CanUseCapturedRoot(
            hasCapturedWindow: true,
            capturedWindowExists: true,
            belongsToCurrentProcess: false,
            rootClassName: "Progman"));
        Assert.False(InjectionTargetPolicy.CanUseCapturedRoot(
            hasCapturedWindow: true,
            capturedWindowExists: false,
            belongsToCurrentProcess: false,
            rootClassName: "Notepad"));
        Assert.True(InjectionTargetPolicy.CanUseCapturedRoot(
            hasCapturedWindow: true,
            capturedWindowExists: true,
            belongsToCurrentProcess: false,
            rootClassName: "Notepad"));
    }
}
