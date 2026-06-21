using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Asr;

namespace LocalAsrClient.Core.Dictation;

public sealed class TranscriptionScriptPostProcessor : ITextPostProcessor
{
    private readonly ISettingsStore _settingsStore;
    private readonly ITranscriptionPunctuationPolicy _punctuationPolicy;

    public TranscriptionScriptPostProcessor(
        ISettingsStore settingsStore,
        ITranscriptionPunctuationPolicy? punctuationPolicy = null)
    {
        _settingsStore = settingsStore;
        _punctuationPolicy = punctuationPolicy ?? new PreferredLanguagePunctuationPolicy();
    }

    public async Task<string> ProcessAsync(string text, CancellationToken cancellationToken)
    {
        var settings = await _settingsStore.LoadAsync(cancellationToken);
        var languageId = TranscriptionLanguageCatalog.NormalizeId(settings.PreferredTranscriptionLanguageId);
        text = OpenCcScriptConverter.Convert(text, languageId);
        if (_punctuationPolicy.ShouldUseChinesePunctuation(text, languageId))
        {
            text = CjkPunctuationNormalizer.Normalize(text);
        }

        return text;
    }
}
