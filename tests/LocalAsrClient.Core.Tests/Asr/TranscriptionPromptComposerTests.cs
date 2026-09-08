using LocalAsrClient.Core.Asr;

namespace LocalAsrClient.Core.Tests.Asr;

public sealed class TranscriptionPromptComposerTests
{
    [Theory]
    [InlineData("zh-Hans", "以下是普通话的句子。")]
    [InlineData("zh-Hant", "以下是普通話的句子。")]
    [InlineData("en", "This is a sentence in English.")]
    [InlineData("auto", null)]
    [InlineData("ja", null)]
    [InlineData("unknown", null)]
    [InlineData(null, null)]
    public void GetLanguageStylePrompt_UsesPreferredWritingStyle(string? languageId, string? expected)
    {
        Assert.Equal(expected, TranscriptionPromptComposer.GetLanguageStylePrompt(languageId));
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("术语B, Term A", null, "术语B, Term A")]
    [InlineData(null, "以下是普通话的句子。", "以下是普通话的句子。")]
    [InlineData(" ", "以下是普通话的句子。", "以下是普通话的句子。")]
    [InlineData("术语B, Term A", "以下是普通话的句子。", "术语B, Term A\n以下是普通话的句子。")]
    [InlineData("以下是普通话的句子。", "以下是普通话的句子。", "以下是普通话的句子。")]
    public void Compose_PreservesVocabularyAndAppendsStyleSample(
        string? vocabulary, string? style, string? expected)
    {
        Assert.Equal(expected, TranscriptionPromptComposer.Compose(vocabulary, style));
    }
}
