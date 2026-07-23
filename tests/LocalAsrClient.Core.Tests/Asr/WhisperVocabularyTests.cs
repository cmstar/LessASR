using LocalAsrClient.Core.Asr;

namespace LocalAsrClient.Core.Tests.Asr;

public sealed class WhisperVocabularyTests
{
    [Fact]
    public void Parse_NormalizesBlankLinesWhitespaceAndExactDuplicates()
    {
        var result = WhisperVocabulary.Parse(
            "  LessASR  \r\n\r\n大语言模型\nLessASR\nlessasr\n初音ミク  ");

        Assert.True(result.IsValid);
        Assert.Equal(["LessASR", "大语言模型", "lessasr", "初音ミク"], result.Entries);
        Assert.Equal("LessASR\n大语言模型\nlessasr\n初音ミク", result.NormalizedText);
    }

    [Fact]
    public void Parse_RejectsMoreThanOneHundredUniqueEntries()
    {
        var text = string.Join('\n', Enumerable.Range(1, 101).Select(index => $"词条{index}"));

        var result = WhisperVocabulary.Parse(text);

        Assert.False(result.IsValid);
        Assert.Equal("最多可以添加 100 个词条。", result.ErrorMessage);
    }

    [Fact]
    public void Parse_AcceptsExactlyOneHundredUniqueEntries()
    {
        var text = string.Join('\n', Enumerable.Range(1, 100).Select(index => $"词条{index}"));

        var result = WhisperVocabulary.Parse(text);

        Assert.True(result.IsValid);
        Assert.Equal(100, result.Entries.Count);
    }

    [Fact]
    public void Parse_RejectsEntryLongerThanThirtyUnicodeTextElements()
    {
        var result = WhisperVocabulary.Parse($"正常词条\n{new string('词', 31)}");

        Assert.False(result.IsValid);
        Assert.Equal("第 2 行超过 30 个字符。", result.ErrorMessage);
    }

    [Fact]
    public void Parse_CountsGraphemeClusterAsOneDisplayedCharacter()
    {
        var familyEmoji = "👨‍👩‍👧‍👦";
        var result = WhisperVocabulary.Parse(string.Concat(Enumerable.Repeat(familyEmoji, 30)));

        Assert.True(result.IsValid);
        Assert.Single(result.Entries);
    }

    [Fact]
    public void BuildInitialPrompt_PreservesMixedLanguagesAndPlacesHighestPriorityLast()
    {
        var prompt = WhisperVocabulary.BuildInitialPrompt(
            ["LessASR", "大语言模型", "Kubernetes", "初音ミク"]);

        Assert.Equal("初音ミク, Kubernetes, 大语言模型, LessASR", prompt);
    }
}
