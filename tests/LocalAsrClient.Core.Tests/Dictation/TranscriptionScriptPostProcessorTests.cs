using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Asr;
using LocalAsrClient.Core.Dictation;
using LocalAsrClient.Core.Persistence;

namespace LocalAsrClient.Core.Tests.Dictation;

public sealed class TranscriptionScriptPostProcessorTests
{
    [Fact]
    public async Task ProcessAsync_ConvertsToSimplified_WhenSettingsPreferZhHans()
    {
        var processor = new TranscriptionScriptPostProcessor(new StubSettingsStore("zh-Hans"));

        var result = await processor.ProcessAsync("漢字", CancellationToken.None);

        Assert.Equal("汉字", result);
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
