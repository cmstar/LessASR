namespace LocalAsrClient.App.TestMode;

public sealed record TestModeOptions(bool Enabled, bool DiagnosticsEnabled, string AsrText)
{
    public const string DefaultAsrText = "LessASR 自动化测试文本";

    public static TestModeOptions Resolve(string[]? startupArgs)
    {
        var enabled = ContainsFlag(startupArgs, "--test-mode");
        var diagnostics = enabled || ContainsFlag(startupArgs, "--diagnostics");
        return new TestModeOptions(enabled, diagnostics, DefaultAsrText);
    }

    private static bool ContainsFlag(string[]? startupArgs, string flag)
    {
        if (startupArgs is null || startupArgs.Length == 0)
        {
            return false;
        }

        return startupArgs.Any(arg => string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase));
    }
}
