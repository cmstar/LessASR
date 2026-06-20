namespace LocalAsrClient.TestTarget;

public sealed class TestTargetStartupOptions
{
    public static TestTargetStartupOptions Current { get; private set; } = new();

    public bool PauseAfterRun { get; private init; }

    public static TestTargetStartupOptions Parse(string[]? startupArgs)
    {
        var pause = ContainsFlag(startupArgs, "--pause");
        Current = new TestTargetStartupOptions { PauseAfterRun = pause };
        return Current;
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
