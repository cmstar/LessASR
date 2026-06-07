namespace LocalAsrClient.Core.Persistence;

public sealed record AppSettings(
    string ModelPath,
    string WhisperServerPath,
    string DataDirectory,
    TranscriptRetentionPolicy TranscriptRetentionPolicy,
    bool StartModelOnAppStartup)
{
    public static AppSettings CreateDefault(string localAppData)
    {
        var dataDirectory = Path.Combine(localAppData, "LocalAsrClient", "data");
        return new AppSettings(
            ModelPath: string.Empty,
            WhisperServerPath: string.Empty,
            DataDirectory: dataDirectory,
            TranscriptRetentionPolicy: TranscriptRetentionPolicy.SevenDays,
            StartModelOnAppStartup: false);
    }
}