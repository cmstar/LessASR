namespace LocalAsrClient.Core.Persistence;

public sealed record AppSettings(
    string ModelPath,
    string WhisperServerPath,
    int WhisperServerPort,
    TranscriptRetentionPolicy TranscriptRetentionPolicy,
    bool StartModelOnAppStartup,
    bool MinimizeToTrayOnClose = true)
{
    public const int DefaultWhisperServerPort = 8080;

    public static AppSettings CreateDefault() => new(
        ModelPath: string.Empty,
        WhisperServerPath: string.Empty,
        WhisperServerPort: DefaultWhisperServerPort,
        TranscriptRetentionPolicy: TranscriptRetentionPolicy.SevenDays,
        StartModelOnAppStartup: false,
        MinimizeToTrayOnClose: true);
}
