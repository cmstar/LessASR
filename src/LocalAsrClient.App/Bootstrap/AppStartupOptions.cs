using System.IO;
using LocalAsrClient.Core;

namespace LocalAsrClient.App.Bootstrap;

public enum AppRuntimeMode
{
    Standard,
    Test,
    Demo
}

public sealed record AppStartupOptions(
    AppRuntimeMode RuntimeMode,
    bool DiagnosticsEnabled,
    string? DemoScreenshotOutputDirectory)
{
    private const string TestModeFlag = "--test-mode";
    private const string DemoModeFlag = "--demo-mode";
    private const string DiagnosticsFlag = "--diagnostics";
    private const string ExportScreenshotsOption = "--export-demo-screenshots";

    public bool IsTestMode => RuntimeMode == AppRuntimeMode.Test;

    public bool IsDemoMode => RuntimeMode == AppRuntimeMode.Demo;

    public LessAsrPathLayout Paths => IsDemoMode
        ? LessAsrPaths.Demo
        : LessAsrPaths.Production;

    public static AppStartupOptions Resolve(string[]? startupArgs)
    {
        var args = startupArgs ?? [];
        var testMode = ContainsFlag(args, TestModeFlag);
        var demoMode = ContainsFlag(args, DemoModeFlag);
        if (testMode && demoMode)
        {
            throw new ArgumentException("测试模式与演示模式不能同时启用。");
        }

        var screenshotOutputDirectory = ReadOptionValue(args, ExportScreenshotsOption);
        if (screenshotOutputDirectory is not null && !demoMode)
        {
            throw new ArgumentException("导出演示截图时必须同时启用 --demo-mode。");
        }

        var runtimeMode = testMode
            ? AppRuntimeMode.Test
            : demoMode
                ? AppRuntimeMode.Demo
                : AppRuntimeMode.Standard;
        var diagnostics = testMode || ContainsFlag(args, DiagnosticsFlag);

        return new AppStartupOptions(
            runtimeMode,
            diagnostics,
            screenshotOutputDirectory is null
                ? null
                : Path.GetFullPath(screenshotOutputDirectory));
    }

    private static bool ContainsFlag(IEnumerable<string> args, string flag) =>
        args.Any(arg => string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase));

    private static string? ReadOptionValue(IReadOnlyList<string> args, string option)
    {
        for (var index = 0; index < args.Count; index++)
        {
            if (!string.Equals(args[index], option, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (index + 1 >= args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"{option} 缺少目录参数。");
            }

            return args[index + 1];
        }

        return null;
    }
}
