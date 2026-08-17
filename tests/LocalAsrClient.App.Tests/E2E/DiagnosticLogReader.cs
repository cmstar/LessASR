namespace LocalAsrClient.App.Tests.E2E;

public static class DiagnosticLogReader
{
    public static string GetNewestDiagnosticsFile(DateTimeOffset? notBefore = null)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".lessasr",
            "diagnostics");

        var file = Directory
            .EnumerateFiles(directory, "diagnostics-*.jsonl")
            .Where(path => notBefore is null
                || File.GetLastWriteTimeUtc(path) >= notBefore.Value.UtcDateTime)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        return file ?? throw new FileNotFoundException($"No LessASR diagnostic log found in {directory}.");
    }

    public static string ReadAll(string path) => File.Exists(path) ? File.ReadAllText(path) : string.Empty;
}
