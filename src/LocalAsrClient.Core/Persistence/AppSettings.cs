namespace LocalAsrClient.Core.Persistence;

public sealed record AppSettings(
    string ModelPath,
    string WhisperServerPath,
    TranscriptRetentionPolicy TranscriptRetentionPolicy,
    bool StartModelOnAppStartup,
    bool MinimizeToTrayOnClose = true)
{
    public static AppSettings CreateDefault() => new(
        ModelPath: string.Empty,
        WhisperServerPath: string.Empty,
        TranscriptRetentionPolicy: TranscriptRetentionPolicy.SevenDays,
        StartModelOnAppStartup: false,
        MinimizeToTrayOnClose: true);
}
