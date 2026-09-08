namespace LocalAsrClient.Core.Asr;

public static class TranscriptionPromptComposer
{
    public static string? GetLanguageStylePrompt(string? preferredLanguageId) =>
        TranscriptionLanguageCatalog.NormalizeId(preferredLanguageId) switch
        {
            "zh-Hans" => "以下是普通话的句子。",
            "zh-Hant" => "以下是普通話的句子。",
            "en" => "This is a sentence in English.",
            _ => null
        };

    public static string? Compose(string? vocabularyPrompt, string? languageStylePrompt)
    {
        if (string.IsNullOrWhiteSpace(languageStylePrompt))
        {
            return string.IsNullOrWhiteSpace(vocabularyPrompt) ? null : vocabularyPrompt;
        }

        if (string.IsNullOrWhiteSpace(vocabularyPrompt)
            || string.Equals(vocabularyPrompt, languageStylePrompt, StringComparison.Ordinal))
        {
            return languageStylePrompt;
        }

        // 保留词条优先级顺序，末尾附加完整句子作为行文示例。
        return $"{vocabularyPrompt}\n{languageStylePrompt}";
    }
}
