using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Asr;
using LocalAsrClient.Core.Dictation;
using LocalAsrClient.Core.Persistence;

namespace LocalAsrClient.Core.Tests.Dictation;

public sealed class TranscriptionScriptPostProcessorTests
{
    [Theory]
    [InlineData("zh-Hant", "使用U盘，查看K线", "使用U盤，查看K線")]
    [InlineData("zh-Hans", "使用U盤，查看K線", "使用U盘，查看K线")]
    public async Task ProcessAsync_ProtectsWordsAfterScriptConversion(string languageId, string input, string expected)
    {
        var processor = new TranscriptionScriptPostProcessor(new StubSettingsStore(languageId));

        Assert.Equal(expected, await processor.ProcessAsync(input, CancellationToken.None));
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("zh-Hans")]
    [InlineData("zh-Hant")]
    [InlineData("en")]
    [InlineData("ja")]
    [InlineData("ko")]
    public async Task ProcessAsync_AddsMixedScriptSpacing_RegardlessOfPreferredLanguage(string languageId)
    {
        var processor = new TranscriptionScriptPostProcessor(new StubSettingsStore(languageId));

        var result = await processor.ProcessAsync("使用Windows（EMM）", CancellationToken.None);

        Assert.Equal("使用 Windows （ EMM ）", result);
    }

    [Fact]
    public async Task ProcessAsync_ConvertsToSimplified_WhenSettingsPreferZhHans()
    {
        var processor = new TranscriptionScriptPostProcessor(new StubSettingsStore("zh-Hans"));

        var result = await processor.ProcessAsync("漢字", CancellationToken.None);

        Assert.Equal("汉字", result);
    }

    [Fact]
    public async Task ProcessAsync_NormalizesChinesePunctuation_WhenSettingsPreferZhHans()
    {
        var processor = new TranscriptionScriptPostProcessor(new StubSettingsStore("zh-Hans"));

        var result = await processor.ProcessAsync("首先,然后", CancellationToken.None);

        Assert.Equal("首先，然后", result);
    }

    [Fact]
    public async Task ProcessAsync_SkipsChinesePunctuation_WhenSettingsPreferEnglish()
    {
        var processor = new TranscriptionScriptPostProcessor(new StubSettingsStore("en"));

        var result = await processor.ProcessAsync("首先,然后", CancellationToken.None);

        Assert.Equal("首先,然后", result);
    }

    private sealed class StubSettingsStore(string languageId) : ISettingsStore
    {
        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(AppSettings.CreateDefault() with
            {
                PreferredTranscriptionLanguageId = languageId
            });
        }

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
