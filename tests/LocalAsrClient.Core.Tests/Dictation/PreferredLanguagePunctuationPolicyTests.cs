using LocalAsrClient.Core.Dictation;

namespace LocalAsrClient.Core.Tests.Dictation;

public sealed class PreferredLanguagePunctuationPolicyTests
{
    private readonly PreferredLanguagePunctuationPolicy _policy = new();

    [Theory]
    [InlineData("zh-Hans")]
    [InlineData("zh-Hant")]
    public void ShouldUseChinesePunctuation_WhenPreferredLanguageIsChinese(string languageId)
    {
        Assert.True(_policy.ShouldUseChinesePunctuation("任意文本", languageId));
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("en")]
    [InlineData("ja")]
    public void ShouldNotUseChinesePunctuation_ForOtherLanguages(string languageId)
    {
        Assert.False(_policy.ShouldUseChinesePunctuation("你好, world", languageId));
    }
}
