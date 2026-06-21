using LocalAsrClient.Core.Dictation;

namespace LocalAsrClient.Core.Tests.Dictation;

public sealed class CjkPunctuationNormalizerTests
{
    [Fact]
    public void Normalize_ConvertsCommaAdjacentToHan()
    {
        var result = CjkPunctuationNormalizer.Normalize("首先加载模型,然后启动服务");

        Assert.Equal("首先加载模型，然后启动服务", result);
    }

    [Fact]
    public void Normalize_KeepsEnglishCommaBetweenEnglishWords()
    {
        const string text = "load the model, then start the server";

        Assert.Equal(text, CjkPunctuationNormalizer.Normalize(text));
    }

    [Fact]
    public void Normalize_ConvertsCommaAfterHanBeforeEnglish()
    {
        var result = CjkPunctuationNormalizer.Normalize("首先加载模型, then deploy");

        Assert.Equal("首先加载模型， then deploy", result);
    }

    [Fact]
    public void Normalize_KeepsDecimalPoint()
    {
        const string text = "价格是 3.14 元";

        Assert.Equal(text, CjkPunctuationNormalizer.Normalize(text));
    }

    [Fact]
    public void Normalize_KeepsFileExtensionDot()
    {
        const string text = "编辑 config.json 文件";

        Assert.Equal(text, CjkPunctuationNormalizer.Normalize(text));
    }

    [Fact]
    public void Normalize_ConvertsKanjiAdjacentComma()
    {
        var result = CjkPunctuationNormalizer.Normalize("漢字, test");

        Assert.Equal("漢字， test", result);
    }

    [Fact]
    public void Normalize_DoesNotConvertCommaAdjacentToKanaOnly()
    {
        const string text = "こんにちは, test";

        Assert.Equal(text, CjkPunctuationNormalizer.Normalize(text));
    }

    [Fact]
    public void Normalize_ConvertsPeriodAfterHanAtSentenceEnd()
    {
        var result = CjkPunctuationNormalizer.Normalize("这是测试.");

        Assert.Equal("这是测试。", result);
    }
}
