using LocalAsrClient.Core.Dictation;

namespace LocalAsrClient.Core.Tests.Dictation;

public sealed class OpenCcScriptConverterTests
{
    [Fact]
    public void Convert_ToSimplified_WhenPreferredLanguageIsZhHans()
    {
        var result = OpenCcScriptConverter.Convert("漢字轉換測試", "zh-Hans");

        Assert.Equal("汉字转换测试", result);
    }

    [Fact]
    public void Convert_ToTraditional_WhenPreferredLanguageIsZhHant()
    {
        var result = OpenCcScriptConverter.Convert("汉字转换测试", "zh-Hant");

        Assert.Equal("漢字轉換測試", result);
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("en")]
    public void Convert_LeavesTextUnchanged_ForNonChineseScriptPreferences(string languageId)
    {
        const string text = "漢字 mixed English";

        Assert.Equal(text, OpenCcScriptConverter.Convert(text, languageId));
    }
}
