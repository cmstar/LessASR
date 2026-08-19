using LocalAsrClient.App.Bootstrap;
using LocalAsrClient.App.DemoMode;
using LocalAsrClient.App.ViewModels;
using LocalAsrClient.Core.Asr;
using LocalAsrClient.Core.Persistence;
using LocalAsrClient.Core.Utilities;

namespace LocalAsrClient.App.Tests.DemoMode;

public sealed class DemoModeTests
{
    [Theory]
    [InlineData(new string[0], AppRuntimeMode.Standard)]
    [InlineData(new[] { "--test-mode" }, AppRuntimeMode.Test)]
    [InlineData(new[] { "--demo-mode" }, AppRuntimeMode.Demo)]
    [InlineData(new[] { "--DEMO-MODE" }, AppRuntimeMode.Demo)]
    public void StartupOptions_ResolveRuntimeMode(string[] args, AppRuntimeMode expected)
    {
        var options = AppStartupOptions.Resolve(args);

        Assert.Equal(expected, options.RuntimeMode);
    }

    [Fact]
    public void StartupOptions_RejectTestAndDemoModesTogether()
    {
        Assert.Throws<ArgumentException>(
            () => AppStartupOptions.Resolve(["--test-mode", "--demo-mode"]));
    }

    [Fact]
    public void StartupOptions_ReadScreenshotOutputDirectory()
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), "lessasr-docs");

        var options = AppStartupOptions.Resolve(
            ["--demo-mode", "--export-demo-screenshots", outputDirectory]);

        Assert.Equal(Path.GetFullPath(outputDirectory), options.DemoScreenshotOutputDirectory);
    }

    [Fact]
    public void StartupOptions_RequireDemoModeForScreenshotExport()
    {
        Assert.Throws<ArgumentException>(
            () => AppStartupOptions.Resolve(
                ["--export-demo-screenshots", Path.GetTempPath()]));
    }

    [Fact]
    public async Task DemoAsrBackend_ReturnsScenarioSegmentsInOrder()
    {
        var backend = new DemoAsrBackend(["第一段。", "第二段。"]);
        var request = new AsrRequest(
            new InMemoryAudioInput([1, 2, 3], "wav", 16000, 1),
            "zh",
            new Dictionary<string, string>());

        var first = await backend.TranscribeAsync(request, CancellationToken.None);
        var second = await backend.TranscribeAsync(request, CancellationToken.None);

        Assert.Equal("第一段。", first.Text);
        Assert.Equal("第二段。", second.Text);
    }

    [Fact]
    public async Task DemoDataSeeder_CoversCurrentPresentationRangesAndHistoryViewport()
    {
        var now = new DateTimeOffset(2026, 7, 25, 10, 30, 0, TimeSpan.FromHours(8));
        await using var database = await SqliteDatabase.CreateInMemoryAsync();

        await DemoDataSeeder.SeedAsync(database, now, CancellationToken.None);

        var statsRepository = new SqliteStatsRepository(database);
        var stats = await statsRepository.GetRangeAsync(
            DateOnly.FromDateTime(now.Date).AddDays(-(StatsViewModel.SummaryDayCount - 1)),
            DateOnly.FromDateTime(now.Date),
            CancellationToken.None);
        var historyRepository = new SqliteTextHistoryRepository(database);
        var history = await historyRepository.GetRecentAsync(50, CancellationToken.None);
        var settings = await new SqliteSettingsStore(database).LoadAsync(CancellationToken.None);
        var remoteProfiles = await new SqliteRemoteApiProfileRepository(database)
            .GetAllAsync(CancellationToken.None);
        var vocabularyProfiles = await new SqliteVocabularyRepository(database, new SystemClock())
            .GetAllAsync(CancellationToken.None);

        Assert.Equal(
            StatsViewModel.SummaryDayCount,
            stats.Select(snapshot => snapshot.Date).Distinct().Count());
        Assert.All(
            stats.Where(snapshot => snapshot.Date >= DateOnly.FromDateTime(now.Date).AddDays(-(StatsViewModel.TrendDayCount - 1))),
            snapshot => Assert.True(snapshot.InputCount > 0));
        Assert.Contains(stats, snapshot => snapshot.ProviderName == "本地 Whisper");
        Assert.Contains(stats, snapshot => snapshot.ProviderName == "局域网 Whisper");
        Assert.True(history.Count >= DemoDataSeeder.MinimumHistoryEntryCount);
        Assert.True(history.Count > 12);
        Assert.Equal(TranscriptRetentionPolicy.OneMonth, settings.TranscriptRetentionPolicy);
        Assert.Equal("zh-Hans", settings.PreferredTranscriptionLanguageId);
        var remote = Assert.Single(remoteProfiles);
        Assert.Equal("局域网 Whisper", remote.Name);
        Assert.False(remote.UseVocabulary);
        Assert.Null(remote.ProtectedApiKey);
        Assert.Equal(DemoDataScenario.VocabularyProfiles.Count, vocabularyProfiles.Count);
        var activeVocabulary = Assert.Single(vocabularyProfiles, profile => profile.IsActive);
        Assert.Equal(DemoDataScenario.VocabularyProfiles[0].Name, activeVocabulary.Name);
        Assert.Equal(
            WhisperVocabulary.Parse(DemoDataScenario.VocabularyProfiles[0].EntriesText).NormalizedText,
            activeVocabulary.EntriesText);
    }
}
