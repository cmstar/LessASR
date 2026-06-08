namespace LocalAsrClient.Core;

/// <summary>
/// LessASR 固定数据目录布局；路径不可通过设置修改。
/// </summary>
public static class LessAsrPaths
{
    public const string ProductName = "LessASR";

    public const string ProfileDirectoryName = ".lessasr";

    public const string DataDirectoryName = "data";

    public const string LogsDirectoryName = "logs";

    public const string DatabaseFileName = "client.db";

    public static string AppDataRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ProfileDirectoryName);

    public static string DataDirectory => Path.Combine(AppDataRoot, DataDirectoryName);

    public static string LogsDirectory => Path.Combine(AppDataRoot, LogsDirectoryName);

    public static string DatabasePath => Path.Combine(DataDirectory, DatabaseFileName);
}
