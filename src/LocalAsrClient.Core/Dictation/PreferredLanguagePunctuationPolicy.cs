namespace LocalAsrClient.Core.Dictation;

public sealed class PreferredLanguagePunctuationPolicy : ITranscriptionPunctuationPolicy
{
    public bool ShouldUseChinesePunctuation(string text, string preferredLanguageId)
    {
        _ = text;
        return preferredLanguageId is "zh-Hans" or "zh-Hant";
    }
}
