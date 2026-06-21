using LocalAsrClient.Core.Asr;

namespace LocalAsrClient.Core.Tests.Asr;

public sealed class TranscriptionLanguageCatalogTests
{
    [Fact]
    public void All_ContainsFifteenOptionsInDisplayOrder()
    {
        var all = TranscriptionLanguageCatalog.All;

        Assert.Equal(15, all.Count);
        Assert.Equal("auto", all[0].Id);
        Assert.Equal("zh-Hans", all[1].Id);
        Assert.Equal("zh-Hant", all[2].Id);
        Assert.Equal("en", all[3].Id);
        Assert.Equal("ar", all[4].Id);
        Assert.Equal("vi", all[14].Id);
    }

    [Theory]
    [InlineData("auto", null)]
    [InlineData("zh-Hans", "zh")]
    [InlineData("zh-Hant", "zh")]
    [InlineData("en", "en")]
    public void ResolveLanguage_MapsKnownIds(string id, string? language)
    {
        Assert.Equal(language, TranscriptionLanguageCatalog.ResolveLanguage(id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    public void NormalizeId_UnknownValuesFallbackToAuto(string? id)
    {
        Assert.Equal("auto", TranscriptionLanguageCatalog.NormalizeId(id));
        Assert.Null(TranscriptionLanguageCatalog.ResolveLanguage(id));
    }
}
