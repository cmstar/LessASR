using LocalAsrClient.Core.Asr;
using LocalAsrClient.Core.Dictation;
using LocalAsrClient.Core.Persistence;
using LocalAsrClient.Core.Text;

namespace LocalAsrClient.Core.Tests.Dictation;

public sealed class TranscriptionPipelineTests
{
    [Fact]
    public async Task TranscribeAsync_OnSuccess_RecordsSucceededStats()
    {
        var backend = new StubBackend { Status = AsrBackendStatus.Ready, TranscribeText = "你好" };
        var stats = new StubStatsRepository();
        var settings = new StubSettingsStore();
        var pipeline = new TranscriptionPipeline(backend, settings, new NoOpTextPostProcessor(), stats, new StubClock());
        var recording = new RecordingResult(new byte[16], TimeSpan.FromSeconds(1), 16000, 1);

        var result = await pipeline.TranscribeAsync(recording, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("你好", result.Text);
        Assert.Single(stats.Recorded);
        Assert.True(stats.Recorded[0].Succeeded);
    }

    [Fact]
    public async Task TranscribeAsync_OnEmptyText_RecordsFailedStats()
    {
        var backend = new StubBackend { Status = AsrBackendStatus.Ready, TranscribeText = "  " };
        var stats = new StubStatsRepository();
        var pipeline = new TranscriptionPipeline(backend, new StubSettingsStore(), new NoOpTextPostProcessor(), stats, new StubClock());
        var recording = new RecordingResult(new byte[16], TimeSpan.FromSeconds(1), 16000, 1);

        var result = await pipeline.TranscribeAsync(recording, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Single(stats.Recorded);
        Assert.False(stats.Recorded[0].Succeeded);
    }

    [Fact]
    public async Task TranscribeAsync_IncludesVocabularyPromptFromLatestSettings()
    {
        var backend = new StubBackend { Status = AsrBackendStatus.Ready, TranscribeText = "LessASR" };
        var settings = new StubSettingsStore
        {
            Settings = AppSettings.CreateDefault() with
            {
                VocabularyText = "LessASR\n大语言模型\nKubernetes\n初音ミク"
            }
        };
        var pipeline = new TranscriptionPipeline(
            backend,
            settings,
            new NoOpTextPostProcessor(),
            new StubStatsRepository(),
            new StubClock());

        await pipeline.TranscribeAsync(
            new RecordingResult(new byte[16], TimeSpan.FromSeconds(1), 16000, 1),
            CancellationToken.None);

        Assert.Equal(
            "初音ミク, Kubernetes, 大语言模型, LessASR",
            backend.LastRequest?.InitialPrompt);

        settings.Settings = settings.Settings with { VocabularyText = "新的词条" };

        await pipeline.TranscribeAsync(
            new RecordingResult(new byte[16], TimeSpan.FromSeconds(1), 16000, 1),
            CancellationToken.None);

        Assert.Equal("新的词条", backend.LastRequest?.InitialPrompt);
    }
}
