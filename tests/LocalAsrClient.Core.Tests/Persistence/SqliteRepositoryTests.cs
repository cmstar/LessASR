using LocalAsrClient.Core.Persistence;
using LocalAsrClient.Core.Asr;

namespace LocalAsrClient.Core.Tests.Persistence;

public sealed class SqliteRepositoryTests
{
    [Fact]
    public async Task SettingsStore_RoundTripsSettings()
    {
        await using var database = await SqliteDatabase.CreateInMemoryAsync();
        var store = new SqliteSettingsStore(database);

        var settings = new AppSettings(
            ModelPath: @"D:\models\ggml-large-v3-turbo-q5_0.bin",
            WhisperServerPath: @"D:\tools\whisper-server.exe",
            WhisperServerPort: 8081,
            TranscriptRetentionPolicy: TranscriptRetentionPolicy.OneMonth,
            StartModelOnAppStartup: true,
            MinimizeToTrayOnClose: false,
            WhisperServerThreadCount: 8,
            PreferredTranscriptionLanguageId: "zh-Hans",
            VocabularyText: "LessASR\n大语言模型\nKubernetes\n初音ミク");

        await store.SaveAsync(settings, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(settings, loaded);
    }

    [Fact]
    public async Task SettingsStore_DefaultsMinimizeToTrayOnCloseWhenMissing()
    {
        await using var database = await SqliteDatabase.CreateInMemoryAsync();
        var store = new SqliteSettingsStore(database);

        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.True(loaded.MinimizeToTrayOnClose);
    }

    [Fact]
    public async Task SettingsStore_DefaultsWhisperServerPortWhenMissing()
    {
        await using var database = await SqliteDatabase.CreateInMemoryAsync();
        var store = new SqliteSettingsStore(database);

        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(AppSettings.DefaultWhisperServerPort, loaded.WhisperServerPort);
    }

    [Fact]
    public async Task SettingsStore_DefaultsPreferredTranscriptionLanguageWhenMissing()
    {
        await using var database = await SqliteDatabase.CreateInMemoryAsync();
        var store = new SqliteSettingsStore(database);

        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(TranscriptionLanguageCatalog.DefaultId, loaded.PreferredTranscriptionLanguageId);
    }

    [Fact]
    public async Task SettingsStore_DefaultsWhisperServerThreadCountWhenMissing()
    {
        await using var database = await SqliteDatabase.CreateInMemoryAsync();
        var store = new SqliteSettingsStore(database);

        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Null(loaded.WhisperServerThreadCount);
    }

    [Fact]
    public async Task SettingsStore_DefaultsVocabularyTextWhenMissing()
    {
        await using var database = await SqliteDatabase.CreateInMemoryAsync();
        var store = new SqliteSettingsStore(database);

        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(string.Empty, loaded.VocabularyText);
    }

    [Fact]
    public async Task StatsRepository_AccumulatesDailyStats()
    {
        await using var database = await SqliteDatabase.CreateInMemoryAsync();
        var repository = new SqliteStatsRepository(database);

        var date = new DateOnly(2026, 6, 7);
        await repository.RecordAsync(new DailyStatsDelta(
            Date: date,
            Succeeded: true,
            RecordingDuration: TimeSpan.FromSeconds(3),
            ProcessingDuration: TimeSpan.FromSeconds(2),
            CharacterCount: 12,
            WordCount: 5), CancellationToken.None);

        await repository.RecordAsync(new DailyStatsDelta(
            Date: date,
            Succeeded: false,
            RecordingDuration: TimeSpan.FromSeconds(1),
            ProcessingDuration: TimeSpan.Zero,
            CharacterCount: 0,
            WordCount: 0), CancellationToken.None);

        var stats = await repository.GetRangeAsync(date, date, CancellationToken.None);

        var day = Assert.Single(stats);
        Assert.Equal(2, day.InputCount);
        Assert.Equal(1, day.SuccessCount);
        Assert.Equal(1, day.FailedCount);
        Assert.Equal(4, day.RecordingSeconds);
        Assert.Equal(2, day.ProcessingSeconds);
        Assert.Equal(12, day.CharacterCount);
        Assert.Equal(5, day.WordCount);
    }

    [Fact]
    public async Task TextHistoryRepository_RespectsRetentionAndDisabledPolicy()
    {
        await using var database = await SqliteDatabase.CreateInMemoryAsync();
        var repository = new SqliteTextHistoryRepository(database);

        var now = new DateTimeOffset(2026, 6, 7, 12, 0, 0, TimeSpan.Zero);

        await repository.AddAsync(new TextHistoryEntry(
            Id: Guid.NewGuid(),
            CreatedAt: now.AddDays(-8),
            Text: "旧记录",
            CharacterCount: 3,
            WordCount: 3,
            RecordingDuration: TimeSpan.FromSeconds(1),
            ProcessingDuration: TimeSpan.FromSeconds(1),
            BackendId: "whisper-server",
            ModelId: "large-v3-turbo"), CancellationToken.None);

        await repository.AddAsync(new TextHistoryEntry(
            Id: Guid.NewGuid(),
            CreatedAt: now,
            Text: "新记录",
            CharacterCount: 3,
            WordCount: 3,
            RecordingDuration: TimeSpan.FromSeconds(1),
            ProcessingDuration: TimeSpan.FromSeconds(1),
            BackendId: "whisper-server",
            ModelId: "large-v3-turbo"), CancellationToken.None);

        await repository.PruneAsync(now, TranscriptRetentionPolicy.SevenDays, CancellationToken.None);
        var retained = await repository.GetRecentAsync(10, CancellationToken.None);

        var entry = Assert.Single(retained);
        Assert.Equal("新记录", entry.Text);

        await repository.PruneAsync(now, TranscriptRetentionPolicy.Disabled, CancellationToken.None);
        Assert.Empty(await repository.GetRecentAsync(10, CancellationToken.None));
    }

    [Fact]
    public async Task TextHistoryRepository_OneMonthPolicy_KeepsRecordsFromLastSevenDays()
    {
        await using var database = await SqliteDatabase.CreateInMemoryAsync();
        var repository = new SqliteTextHistoryRepository(database);
        var now = new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);
        var entry = new TextHistoryEntry(
            Guid.NewGuid(),
            now.AddDays(-7),
            "七天前记录",
            6,
            0,
            TimeSpan.Zero,
            TimeSpan.Zero,
            "whisper-server",
            "test-model");

        await repository.AddAsync(entry, CancellationToken.None);
        await repository.PruneAsync(now, TranscriptRetentionPolicy.OneMonth, CancellationToken.None);

        Assert.Equal(entry, Assert.Single(await repository.GetRecentAsync(10, CancellationToken.None)));
    }
}
