namespace LocalAsrClient.Core;

public sealed record LessAsrPathLayout(string AppDataRoot)
{
    public string DataDirectory => Path.Combine(AppDataRoot, LessAsrPaths.DataDirectoryName);

    public string LogsDirectory => Path.Combine(AppDataRoot, LessAsrPaths.LogsDirectoryName);

    public string DiagnosticsDirectory => Path.Combine(AppDataRoot, LessAsrPaths.DiagnosticsDirectoryName);

    public string DatabasePath => Path.Combine(DataDirectory, LessAsrPaths.DatabaseFileName);
}

/// <summary>
/// LessASR 固定数据目录布局；路径不可通过设置修改。
/// </summary>
public static class LessAsrPaths
{
    public const string ProductName = "LessASR";

    public const string ProfileDirectoryName = ".lessasr";

    public const string DataDirectoryName = "data";

    public const string LogsDirectoryName = "logs";

    public const string DiagnosticsDirectoryName = "diagnostics";

    public const string DatabaseFileName = "client.db";

    public static LessAsrPathLayout Production { get; } = new(
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ProfileDirectoryName));

    public static LessAsrPathLayout Demo { get; } = new(
        Path.Combine(
            Path.GetTempPath(),
            ProductName,
            "demo"));

    public static string AppDataRoot => Production.AppDataRoot;

    public static string DataDirectory => Production.DataDirectory;

    public static string LogsDirectory => Production.LogsDirectory;

    public static string DiagnosticsDirectory => Production.DiagnosticsDirectory;

    public static string DatabasePath => Production.DatabasePath;
}
