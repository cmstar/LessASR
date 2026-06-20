using System.IO;

namespace LocalAsrClient.App.TestMode;

public sealed record TestModeOptions(bool Enabled, bool DiagnosticsEnabled, string AudioPath, string AsrText)
{
    public const string DefaultAsrText = "LessASR 自动化测试文本";

    public static TestModeOptions FromEnvironment()
    {
        var enabled = string.Equals(Environment.GetEnvironmentVariable("LESSASR_TEST_MODE"), "1", StringComparison.Ordinal);
        var diagnosticsEnabled = string.Equals(Environment.GetEnvironmentVariable("LESSASR_DIAGNOSTICS"), "1", StringComparison.Ordinal);
        var audioPath = Environment.GetEnvironmentVariable("LESSASR_TEST_AUDIO")
            ?? Path.Combine(AppContext.BaseDirectory, "test-sound.wav");
        var asrText = Environment.GetEnvironmentVariable("LESSASR_FAKE_ASR_TEXT")
            ?? DefaultAsrText;

        return new TestModeOptions(enabled, diagnosticsEnabled, audioPath, asrText);
    }
}
