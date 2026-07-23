using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Persistence;

namespace LocalAsrClient.Core.Tests.Persistence;

public sealed class VocabularyRepositoryTests
{
    [Fact]
    public async Task CreateAsync_FirstProfileIsActiveAndLaterProfilesAreInactive()
    {
        await using var database = await SqliteDatabase.CreateInMemoryAsync();
        var clock = new TestClock();
        var repository = new SqliteVocabularyRepository(database, clock);

        var first = await repository.CreateAsync("编程开发", CancellationToken.None);
        var second = await repository.CreateAsync("医疗记录", CancellationToken.None);

        Assert.True(first.IsActive);
        Assert.False(second.IsActive);
        Assert.Equal(first.Id, (await repository.GetActiveAsync(CancellationToken.None))?.Id);
        Assert.Equal([first.Id, second.Id], (await repository.GetAllAsync(CancellationToken.None)).Select(item => item.Id));
    }

    [Fact]
    public async Task UpdateAsync_NormalizesNameAndEntries()
    {
        await using var database = await SqliteDatabase.CreateInMemoryAsync();
        var clock = new TestClock();
        var repository = new SqliteVocabularyRepository(database, clock);
        var profile = await repository.CreateAsync("旧名称", CancellationToken.None);
        clock.Advance();

        await repository.UpdateAsync(
            profile.Id,
            "  编程开发  ",
            "  LessASR \r\n\r\nKubernetes\nLessASR ",
            CancellationToken.None);

        var updated = Assert.Single(await repository.GetAllAsync(CancellationToken.None));
        Assert.Equal("编程开发", updated.Name);
        Assert.Equal("LessASR\nKubernetes", updated.EntriesText);
        Assert.Equal(clock.Now, updated.UpdatedAt);
    }

    [Fact]
    public async Task CreateAndUpdate_RejectDuplicateNamesIgnoringCase()
    {
        await using var database = await SqliteDatabase.CreateInMemoryAsync();
        var repository = new SqliteVocabularyRepository(database, new TestClock());
        await repository.CreateAsync("Coding", CancellationToken.None);
        var second = await repository.CreateAsync("医疗", CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.CreateAsync("coding", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.UpdateAsync(second.Id, "CODING", string.Empty, CancellationToken.None));

        await repository.CreateAsync("Ärztlich", CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.CreateAsync("ärztlich", CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_RejectsInvalidName()
    {
        await using var database = await SqliteDatabase.CreateInMemoryAsync();
        var repository = new SqliteVocabularyRepository(database, new TestClock());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.CreateAsync("   ", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.CreateAsync(new string('词', 31), CancellationToken.None));
    }

    [Fact]
    public async Task SetActiveAsync_KeepsAtMostOneActiveAndCanClearSelection()
    {
        await using var database = await SqliteDatabase.CreateInMemoryAsync();
        var repository = new SqliteVocabularyRepository(database, new TestClock());
        var first = await repository.CreateAsync("编程", CancellationToken.None);
        var second = await repository.CreateAsync("日语", CancellationToken.None);

        await repository.SetActiveAsync(second.Id, CancellationToken.None);

        Assert.Equal(second.Id, (await repository.GetActiveAsync(CancellationToken.None))?.Id);
        var switched = await repository.GetAllAsync(CancellationToken.None);
        Assert.False(switched.Single(item => item.Id == first.Id).IsActive);
        Assert.True(switched.Single(item => item.Id == second.Id).IsActive);

        await repository.SetActiveAsync(null, CancellationToken.None);

        Assert.Null(await repository.GetActiveAsync(CancellationToken.None));
        Assert.DoesNotContain(await repository.GetAllAsync(CancellationToken.None), item => item.IsActive);
    }

    [Fact]
    public async Task DeleteAsync_ActiveProfileLeavesNoActiveProfile()
    {
        await using var database = await SqliteDatabase.CreateInMemoryAsync();
        var repository = new SqliteVocabularyRepository(database, new TestClock());
        var active = await repository.CreateAsync("编程", CancellationToken.None);
        var remaining = await repository.CreateAsync("日语", CancellationToken.None);

        await repository.DeleteAsync(active.Id, CancellationToken.None);

        Assert.Null(await repository.GetActiveAsync(CancellationToken.None));
        Assert.Equal(remaining.Id, Assert.Single(await repository.GetAllAsync(CancellationToken.None)).Id);
    }

    [Fact]
    public async Task LegacyVocabularySetting_IsIgnored()
    {
        await using var database = await SqliteDatabase.CreateInMemoryAsync();
        var command = database.Connection.CreateCommand();
        command.CommandText = """
            INSERT INTO settings(key, value)
            VALUES('VocabularyText', '旧版词汇')
            """;
        await command.ExecuteNonQueryAsync();
        var repository = new SqliteVocabularyRepository(database, new TestClock());

        Assert.Empty(await repository.GetAllAsync(CancellationToken.None));
        Assert.Null(await repository.GetActiveAsync(CancellationToken.None));
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset Now { get; private set; } =
            new(2026, 7, 24, 9, 0, 0, TimeSpan.Zero);

        public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);

        public void Advance()
        {
            Now = Now.AddMinutes(1);
        }
    }
}
