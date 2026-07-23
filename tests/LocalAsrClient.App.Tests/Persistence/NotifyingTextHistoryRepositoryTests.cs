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

    [Fact]
    public async Task DeleteAsync_RemovesEntryAndPublishesChanged()
    {
        var inner = new StubHistoryRepository();
        var repository = new NotifyingTextHistoryRepository(inner);
        var selected = Entry("待删除");
        var retained = Entry("保留");
        inner.Entries.AddRange([selected, retained]);
        var changedCount = 0;
        repository.Changed += () => changedCount++;

        await repository.DeleteAsync(selected.Id, CancellationToken.None);

        Assert.Equal([retained], inner.Entries);
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

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
        {
            Entries.RemoveAll(entry => entry.Id == id);
            return Task.CompletedTask;
        }

        public Task<int> CountPrunableAsync(
            DateTimeOffset now,
            TranscriptRetentionPolicy policy,
            CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task PruneAsync(
            DateTimeOffset now,
            TranscriptRetentionPolicy policy,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
