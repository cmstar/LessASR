using LocalAsrClient.Core.Utilities;

namespace LocalAsrClient.Core.Tests.Utilities;

public sealed class TextMetricsTests
{
    [Fact]
    public void CountCharacters_ExcludesWhitespace()
    {
        Assert.Equal(7, TextMetrics.CountCharacters("你好 world"));
    }

    [Fact]
    public void CountWords_CountsEnglishWordsAndChineseCharacters()
    {
        Assert.Equal(4, TextMetrics.CountWords("你好 world test"));
    }

    [Fact]
    public void CountWords_ReturnsZeroForBlankText()
    {
        Assert.Equal(0, TextMetrics.CountWords("   "));
    }
}