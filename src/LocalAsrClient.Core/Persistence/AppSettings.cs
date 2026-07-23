using LocalAsrClient.Core.Asr;

namespace LocalAsrClient.Core.Persistence;

public sealed record AppSettings(
    string ModelPath,
    string WhisperServerPath,
    int WhisperServerPort,
    TranscriptRetentionPolicy TranscriptRetentionPolicy,
    bool StartModelOnAppStartup,
    bool MinimizeToTrayOnClose = true,
    int? WhisperServerThreadCount = null,
    string PreferredTranscriptionLanguageId = TranscriptionLanguageCatalog.DefaultId,
    string VocabularyText = "")
{
    public const int DefaultWhisperServerPort = 8080;

    public static AppSettings CreateDefault() => new(
        ModelPath: string.Empty,
        WhisperServerPath: string.Empty,
        WhisperServerPort: DefaultWhisperServerPort,
        TranscriptRetentionPolicy: TranscriptRetentionPolicy.SevenDays,
        StartModelOnAppStartup: false,
        MinimizeToTrayOnClose: true,
        PreferredTranscriptionLanguageId: TranscriptionLanguageCatalog.DefaultId,
        VocabularyText: string.Empty);
}
