using LocalAsrClient.App.Persistence;
using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Persistence;

namespace LocalAsrClient.App.Tests.Persistence;

public sealed class NotifyingTextHistoryRepositoryTests
{
    [Fact]
    public async Task PruneAsync_AfterHistoryWrite_PublishesChanged()
    {
        var inner = new StubHistoryRepository();
        var repository = new NotifyingTextHistoryRepository(inner);
        var changedCount = 0;
        repository.Changed += () => changedCount++;
        var entry = Entry("新增历史");

        await repository.AddAsync(entry, CancellationToken.None);
        await repository.PruneAsync(
            DateTimeOffset.Now,
            TranscriptRetentionPolicy.SevenDays,
            CancellationToken.None);

        Assert.Equal([entry], inner.Entries);
        Assert.Equal(1, changedCount);
    }

    private static TextHistoryEntry Entry(string text) => new(
        Guid.NewGuid(),
        DateTimeOffset.Now,
        text,
        text.Length,
        0,
        TimeSpan.Zero,
        TimeSpan.Zero,
        "test",
        "test");

    private sealed class StubHistoryRepository : ITextHistoryRepository
    {
        public List<TextHistoryEntry> Entries { get; } = [];

        public Task AddAsync(TextHistoryEntry entry, CancellationToken cancellationToken)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TextHistoryEntry>> GetRecentAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TextHistoryEntry>>(Entries.Take(limit).ToArray());

        public Task PruneAsync(
            DateTimeOffset now,
            TranscriptRetentionPolicy policy,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
