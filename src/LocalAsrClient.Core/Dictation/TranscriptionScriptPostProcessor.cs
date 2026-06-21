using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Asr;

namespace LocalAsrClient.Core.Dictation;

public sealed class TranscriptionScriptPostProcessor : ITextPostProcessor
{
    private readonly ISettingsStore _settingsStore;

    public TranscriptionScriptPostProcessor(ISettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
    }

    public async Task<string> ProcessAsync(string text, CancellationToken cancellationToken)
    {
        var settings = await _settingsStore.LoadAsync(cancellationToken);
        var languageId = TranscriptionLanguageCatalog.NormalizeId(settings.PreferredTranscriptionLanguageId);
        return OpenCcScriptConverter.Convert(text, languageId);
    }
}
