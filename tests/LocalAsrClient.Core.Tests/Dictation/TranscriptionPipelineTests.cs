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
        var pipeline = new TranscriptionPipeline(
            backend,
            settings,
            new StubVocabularyRepository(),
            new NoOpTextPostProcessor(),
            stats,
            new StubClock());
        var recording = new RecordingResult(new byte[16], TimeSpan.FromSeconds(1), 16000, 1);

        var result = await pipeline.TranscribeAsync(recording, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("你好", result.Text);
        Assert.Single(stats.Recorded);
        Assert.True(stats.Recorded[0].Succeeded);
    }

    [Fact]
    public async Task TranscribeAsync_ReturnsBackendSnapshotUsedForRequest()
    {
        var backend = new StubBackend
        {
            Status = AsrBackendStatus.Ready,
            ModelId = "local-model",
            AfterTranscribe = () => { }
        };
        backend.AfterTranscribe = () => backend.ModelId = "remote-model";
        var pipeline = new TranscriptionPipeline(
            backend,
            new StubSettingsStore(),
            new StubVocabularyRepository(),
            new NoOpTextPostProcessor(),
            new StubStatsRepository(),
            new StubClock());

        var result = await pipeline.TranscribeAsync(
            new RecordingResult(new byte[16], TimeSpan.FromSeconds(1), 16000, 1),
            CancellationToken.None);

        Assert.Equal("Whisper Server", result.BackendId);
        Assert.Equal("local-model", result.ModelId);
    }

    [Fact]
    public async Task TranscribeAsync_OnEmptyText_RecordsFailedStats()
    {
        var backend = new StubBackend { Status = AsrBackendStatus.Ready, TranscribeText = "  " };
        var stats = new StubStatsRepository();
        var pipeline = new TranscriptionPipeline(
            backend,
            new StubSettingsStore(),
            new StubVocabularyRepository(),
            new NoOpTextPostProcessor(),
            stats,
            new StubClock());
        var recording = new RecordingResult(new byte[16], TimeSpan.FromSeconds(1), 16000, 1);

        var result = await pipeline.TranscribeAsync(recording, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Single(stats.Recorded);
        Assert.False(stats.Recorded[0].Succeeded);
    }

    [Fact]
    public async Task TranscribeAsync_IncludesPromptFromCurrentlyActiveVocabulary()
    {
        var backend = new StubBackend { Status = AsrBackendStatus.Ready, TranscribeText = "LessASR" };
        var settings = new StubSettingsStore();
        var vocabularies = new StubVocabularyRepository();
        var first = Profile("编程", "LessASR\n大语言模型\nKubernetes\n初音ミク");
        vocabularies.ActiveProfile = first;
        var pipeline = new TranscriptionPipeline(
            backend,
            settings,
            vocabularies,
            new NoOpTextPostProcessor(),
            new StubStatsRepository(),
            new StubClock());

        await pipeline.TranscribeAsync(
            new RecordingResult(new byte[16], TimeSpan.FromSeconds(1), 16000, 1),
            CancellationToken.None);

        Assert.Equal(
            "初音ミク, Kubernetes, 大语言模型, LessASR",
            backend.LastRequest?.InitialPrompt);

        vocabularies.ActiveProfile = Profile("新场景", "新的词条");

        await pipeline.TranscribeAsync(
            new RecordingResult(new byte[16], TimeSpan.FromSeconds(1), 16000, 1),
            CancellationToken.None);

        Assert.Equal("新的词条", backend.LastRequest?.InitialPrompt);
    }

    [Fact]
    public async Task TranscribeAsync_OmitsPromptWhenNoVocabularyIsActive()
    {
        var backend = new StubBackend { Status = AsrBackendStatus.Ready, TranscribeText = "你好" };
        var pipeline = new TranscriptionPipeline(
            backend,
            new StubSettingsStore(),
            new StubVocabularyRepository(),
            new NoOpTextPostProcessor(),
            new StubStatsRepository(),
            new StubClock());

        await pipeline.TranscribeAsync(
            new RecordingResult(new byte[16], TimeSpan.FromSeconds(1), 16000, 1),
            CancellationToken.None);

        Assert.Null(backend.LastRequest?.InitialPrompt);
    }

    private static VocabularyProfile Profile(string name, string entriesText)
    {
        var now = new DateTimeOffset(2026, 7, 24, 9, 0, 0, TimeSpan.Zero);
        return new VocabularyProfile(Guid.NewGuid(), name, entriesText, true, now, now);
    }
}
