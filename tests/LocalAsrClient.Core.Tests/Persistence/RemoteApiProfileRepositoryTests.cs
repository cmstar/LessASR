using LocalAsrClient.Core.Persistence;

namespace LocalAsrClient.Core.Tests.Persistence;

public sealed class RemoteApiProfileRepositoryTests
{
    [Fact]
    public async Task Repository_RoundTripsMultipleProfilesInCreationOrder()
    {
        await using var database = await SqliteDatabase.CreateInMemoryAsync();
        var repository = new SqliteRemoteApiProfileRepository(database);

        var first = await repository.CreateAsync(
            "OpenAI",
            "https://api.openai.com/v1/audio/transcriptions",
            "whisper-1",
            protectedApiKey: "encrypted-one",
            useVocabulary: true,
            CancellationToken.None);
        var second = await repository.CreateAsync(
            "局域网服务",
            "http://192.168.1.8:8080/v1/audio/transcriptions",
            "large-v3-turbo",
            protectedApiKey: null,
            useVocabulary: false,
            CancellationToken.None);

        var loaded = await repository.GetAllAsync(CancellationToken.None);

        Assert.Equal([first, second], loaded);
    }

    [Fact]
    public async Task Repository_RejectsDuplicateNamesIgnoringCase()
    {
        await using var database = await SqliteDatabase.CreateInMemoryAsync();
        var repository = new SqliteRemoteApiProfileRepository(database);
        await repository.CreateAsync(
            "OpenAI",
            "https://api.openai.com/v1/audio/transcriptions",
            "whisper-1",
            protectedApiKey: null,
            useVocabulary: false,
            CancellationToken.None);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => repository.CreateAsync(
            "openai",
            "https://example.com/v1/audio/transcriptions",
            "whisper-1",
            protectedApiKey: null,
            useVocabulary: false,
            CancellationToken.None));

        Assert.Equal("远程 API 配置名称不能重复。", error.Message);
    }

    [Fact]
    public async Task Repository_GetsProfileByStableId()
    {
        await using var database = await SqliteDatabase.CreateInMemoryAsync();
        var repository = new SqliteRemoteApiProfileRepository(database);
        var created = await repository.CreateAsync(
            "家庭服务器",
            "http://192.168.1.8:8080/v1/audio/transcriptions",
            "large-v3-turbo",
            protectedApiKey: null,
            useVocabulary: true,
            CancellationToken.None);

        var loaded = await repository.GetByIdAsync(created.Id, CancellationToken.None);

        Assert.Equal(created, loaded);
        Assert.Null(await repository.GetByIdAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Repository_UpdatesConfigurationAndCanClearApiKey()
    {
        await using var database = await SqliteDatabase.CreateInMemoryAsync();
        var repository = new SqliteRemoteApiProfileRepository(database);
        var profile = await repository.CreateAsync(
            "初始名称",
            "https://old.example/v1/audio/transcriptions",
            "old-model",
            protectedApiKey: "encrypted-key",
            useVocabulary: false,
            CancellationToken.None);

        await repository.UpdateAsync(
            profile.Id,
            "新名称",
            "https://new.example/v1/audio/transcriptions",
            "new-model",
            protectedApiKey: null,
            useVocabulary: true,
            CancellationToken.None);

        var loaded = Assert.Single(await repository.GetAllAsync(CancellationToken.None));
        Assert.Equal("新名称", loaded.Name);
        Assert.Equal("https://new.example/v1/audio/transcriptions", loaded.Endpoint);
        Assert.Equal("new-model", loaded.Model);
        Assert.Null(loaded.ProtectedApiKey);
        Assert.True(loaded.UseVocabulary);
        Assert.Equal(profile.CreatedAt, loaded.CreatedAt);
        Assert.True(loaded.UpdatedAt >= profile.UpdatedAt);
    }

    [Fact]
    public async Task SettingsStore_RoundTripsActiveRemoteProfileId()
    {
        await using var database = await SqliteDatabase.CreateInMemoryAsync();
        var store = new SqliteSettingsStore(database);
        var profileId = Guid.NewGuid();
        var settings = AppSettings.CreateDefault() with
        {
            ActiveRemoteApiProfileId = profileId
        };

        await store.SaveAsync(settings, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(profileId, loaded.ActiveRemoteApiProfileId);
    }

    [Fact]
    public async Task SettingsStore_DefaultsToLocalServiceWhenSelectionIsMissing()
    {
        await using var database = await SqliteDatabase.CreateInMemoryAsync();
        var store = new SqliteSettingsStore(database);

        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Null(loaded.ActiveRemoteApiProfileId);
    }
}
