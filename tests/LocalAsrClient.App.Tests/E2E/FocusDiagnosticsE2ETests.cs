using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace LocalAsrClient.App.Tests.E2E;

public sealed class FocusDiagnosticsE2ETests
{
    private const string ExpectedText = "LessASR 自动化测试文本";

    [Fact]
    [Trait("Category", "UiE2E")]
    public async Task F10DictationInjectsFakeAsrTextIntoNativeTarget()
    {
        await using var runner = new ProcessRunner();
        var repo = FindRepoRoot();
        var targetExe = Path.Combine(repo, "tests", "LocalAsrClient.TestTarget", "bin", "Debug", "net8.0-windows", "LocalAsrClient.TestTarget.exe");
        var appExe = Path.Combine(repo, "src", "LocalAsrClient.App", "bin", "Debug", "net8.0-windows", "LocalAsrClient.App.exe");
        Assert.True(File.Exists(targetExe), $"Build TestTarget first: {targetExe}");
        Assert.True(File.Exists(appExe), $"Build LessASR App first: {appExe}");

        using var automation = new UIA3Automation();
        var targetArguments = ShouldPauseAfterRun() ? "--pause" : string.Empty;
        var targetProcess = runner.Start(targetExe, arguments: targetArguments);
        var targetWindow = await WaitForWindowAsync(automation, targetProcess.Id, "LessASR TestTarget");

        var clearButton = targetWindow.FindFirstDescendant(cf => cf.ByAutomationId("ClearButton"))!.AsButton();
        clearButton.Invoke();

        runner.Start(appExe, arguments: "--test-mode");
        await Task.Delay(1000);

        targetWindow.Focus();
        var focusButton = targetWindow.FindFirstDescendant(cf => cf.ByAutomationId("FocusNativeButton"))!.AsButton();
        focusButton.Invoke();

        KeyboardInput.PressF10();
        await WaitForDiagnosticsEventAsync("Dictation.StateChanged", "Recording", TimeSpan.FromSeconds(5));

        KeyboardInput.PressF10();
        await WaitForDiagnosticsEventAsync("TextInjection.After", null, TimeSpan.FromSeconds(10));

        await WaitUntilAsync(() =>
        {
            targetWindow.Focus();
            var screenLog = targetWindow.FindFirstDescendant(cf => cf.ByAutomationId("ScreenLogTextBox"))!.AsTextBox();
            return screenLog.Text.Contains("TextChanged", StringComparison.Ordinal)
                || screenLog.Text.Contains("WM_0x0102", StringComparison.Ordinal);
        }, TimeSpan.FromSeconds(5));

        var diagnosticsPath = DiagnosticLogReader.GetNewestDiagnosticsFile();
        var diagnostics = DiagnosticLogReader.ReadAll(diagnosticsPath);
        Assert.Contains("InjectionTargetCapture.After", diagnostics);
        Assert.Contains("Overlay.Show.After", diagnostics);
        Assert.Contains("TextInjection.After", diagnostics);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LocalAsrClient.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }

    private static async Task<Window> WaitForWindowAsync(UIA3Automation automation, int processId, string title)
    {
        Window? window = null;
        await WaitUntilAsync(() =>
        {
            var desktop = automation.GetDesktop();
            window = desktop.FindFirstChild(cf => cf.ByProcessId(processId).And(cf.ByName(title)))?.AsWindow();
            return window is not null;
        }, TimeSpan.FromSeconds(10));

        return window!;
    }

    private static async Task WaitForDiagnosticsEventAsync(string eventName, string? state, TimeSpan timeout)
    {
        await WaitUntilAsync(() =>
        {
            try
            {
                var path = DiagnosticLogReader.GetNewestDiagnosticsFile();
                var text = DiagnosticLogReader.ReadAll(path);
                return text.Contains(eventName, StringComparison.Ordinal)
                    && (state is null || text.Contains(state, StringComparison.Ordinal));
            }
            catch
            {
                return false;
            }
        }, timeout);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.Now + timeout;
        while (DateTimeOffset.Now < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"Condition was not met within {timeout}.");
    }

    private static bool ShouldPauseAfterRun()
    {
        return Environment.GetCommandLineArgs().Any(arg =>
            string.Equals(arg, "PauseAfterRun", StringComparison.OrdinalIgnoreCase));
    }
}
