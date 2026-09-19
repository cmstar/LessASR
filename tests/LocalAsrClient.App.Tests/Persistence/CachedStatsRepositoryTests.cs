using LocalAsrClient.App.Persistence;
using LocalAsrClient.App.ViewModels;
using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Persistence;

namespace LocalAsrClient.App.Tests.Persistence;

public sealed class CachedStatsRepositoryTests
{
    private static readonly DateOnly Today = new(2026, 9, 19);

    [Fact]
    public async Task WritesUpdateMemoryButDisplayedSnapshotChangesOnlyWhenReloaded()
    {
        await using var database = await SqliteDatabase.CreateInMemoryAsync();
        var inner = new CountingRepository(new SqliteStatsRepository(database));
        for (var i = 0; i < 6; i++)
            await inner.RecordAsync(Delta(), CancellationToken.None);
        var cache = await CachedStatsRepository.CreateAsync(inner, CancellationToken.None);
        var view = new StatsViewModel();
        view.Load(await cache.GetRangeAsync(Today, Today, CancellationToken.None), Today);
        var notifications = 0;
        view.PropertyChanged += (_, _) => notifications++;

        for (var i = 0; i < 37; i++)
            await cache.RecordAsync(Delta() with { ProviderName = "远程模型" }, CancellationToken.None);

        Assert.Equal(6, view.TodayInputCount);
        Assert.Equal(0, notifications);
        view.Load(await cache.GetRangeAsync(Today, Today, CancellationToken.None), Today);
        Assert.Equal(43, view.TodayInputCount);
        Assert.Equal(430, view.TodayCharacterCount);
        Assert.Equal(430, view.LastSevenDays[^1].CharacterCount);
        Assert.Equal(2, view.Days.Count);
        Assert.Equal(1, inner.ReadCount);
        Assert.Equal(await inner.Inner.GetRangeAsync(Today, Today, CancellationToken.None),
            await cache.GetRangeAsync(Today, Today, CancellationToken.None));
    }

    [Fact]
    public async Task FailedWriteLeavesMemoryUnchanged()
    {
        await using var database = await SqliteDatabase.CreateInMemoryAsync();
        var inner = new CountingRepository(new SqliteStatsRepository(database));
        var cache = await CachedStatsRepository.CreateAsync(inner, CancellationToken.None);
        await cache.RecordAsync(Delta(), CancellationToken.None);
        inner.FailWrites = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.RecordAsync(Delta(), CancellationToken.None));

        Assert.Equal(1, Assert.Single(await cache.GetRangeAsync(Today, Today, CancellationToken.None)).InputCount);
        Assert.Equal(1, inner.ReadCount);
    }

    [Fact]
    public async Task ConcurrentWritesKeepAllCountersConsistentWithDatabase()
    {
        await using var database = await SqliteDatabase.CreateInMemoryAsync();
        var inner = new CountingRepository(new SqliteStatsRepository(database));
        var cache = await CachedStatsRepository.CreateAsync(inner, CancellationToken.None);
        await Task.WhenAll(Enumerable.Range(0, 40).Select(i => Task.Run(() =>
            cache.RecordAsync(Delta() with { Succeeded = i % 2 == 0 }, CancellationToken.None))));

        var rows = await cache.GetRangeAsync(Today, Today, CancellationToken.None);
        var row = Assert.Single(rows);
        Assert.Equal(40, row.InputCount);
        Assert.Equal(20, row.SuccessCount);
        Assert.Equal(20, row.FailedCount);
        Assert.Equal(await inner.Inner.GetRangeAsync(Today, Today, CancellationToken.None), rows);
        Assert.Equal(1, inner.ReadCount);
    }

    [Fact]
    public async Task ReopeningAfterMidnightUsesNewDateAndPruningKeepsMemoryConsistent()
    {
        await using var database = await SqliteDatabase.CreateInMemoryAsync();
        var inner = new CountingRepository(new SqliteStatsRepository(database));
        await inner.RecordAsync(Delta() with { Date = Today.AddDays(-63) }, CancellationToken.None);
        var cache = await CachedStatsRepository.CreateAsync(inner, CancellationToken.None);
        await cache.RecordAsync(Delta(), CancellationToken.None);
        await cache.RecordAsync(Delta() with { Date = Today.AddDays(1), CharacterCount = 25 }, CancellationToken.None);
        await cache.PruneAsync(Today.AddDays(1), CancellationToken.None);

        var view = new StatsViewModel();
        view.Load(await cache.GetRangeAsync(Today.AddDays(-28), Today.AddDays(1), CancellationToken.None), Today.AddDays(1));
        Assert.Equal(1, view.TodayInputCount);
        Assert.Equal(25, view.TodayCharacterCount);
        Assert.Equal(2, view.ThirtyDayInputCount);
        Assert.Equal(await inner.Inner.GetRangeAsync(DateOnly.MinValue, DateOnly.MaxValue, CancellationToken.None),
            await cache.GetRangeAsync(DateOnly.MinValue, DateOnly.MaxValue, CancellationToken.None));
        Assert.Equal(1, inner.ReadCount);
    }

    private static DailyStatsDelta Delta() => new(Today, "本地 Whisper", true,
        TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(0.5), 10, 1);

    private sealed class CountingRepository(IStatsRepository inner) : IStatsRepository
    {
        public IStatsRepository Inner => inner;
        public int ReadCount { get; private set; }
        public bool FailWrites { get; set; }

        public Task RecordAsync(DailyStatsDelta delta, CancellationToken cancellationToken) =>
            FailWrites ? Task.FromException(new InvalidOperationException("Write failed"))
                : inner.RecordAsync(delta, cancellationToken);

        public Task<IReadOnlyList<DailyStatsSnapshot>> GetRangeAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken)
        {
            ReadCount++;
            return inner.GetRangeAsync(start, end, cancellationToken);
        }

        public Task PruneAsync(DateOnly today, CancellationToken cancellationToken) => inner.PruneAsync(today, cancellationToken);
    }
}
