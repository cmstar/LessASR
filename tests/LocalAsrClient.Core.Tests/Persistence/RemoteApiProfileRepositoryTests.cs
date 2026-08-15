using LocalAsrClient.Core.Persistence;
using Microsoft.Data.Sqlite;

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
            CancellationToken.None,
            proxyUrl: "socks5://127.0.0.1:1080/");
        var second = await repository.CreateAsync(
            "局域网服务",
            "http://192.168.1.8:8080/v1/audio/transcriptions",
            "large-v3-turbo",
            protectedApiKey: null,
            useVocabulary: false,
            CancellationToken.None,
            proxyUrl: null);

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
            CancellationToken.None,
            proxyUrl: null);

        await repository.UpdateAsync(
            profile.Id,
            "新名称",
            "https://new.example/v1/audio/transcriptions",
            "new-model",
            protectedApiKey: null,
            useVocabulary: true,
            CancellationToken.None,
            proxyUrl: "https://proxy.example.com:8443/");

        var loaded = Assert.Single(await repository.GetAllAsync(CancellationToken.None));
        Assert.Equal("新名称", loaded.Name);
        Assert.Equal("https://new.example/v1/audio/transcriptions", loaded.Endpoint);
        Assert.Equal("new-model", loaded.Model);
        Assert.Null(loaded.ProtectedApiKey);
        Assert.True(loaded.UseVocabulary);
        Assert.Equal("https://proxy.example.com:8443/", loaded.ProxyUrl);
        Assert.Equal(profile.CreatedAt, loaded.CreatedAt);
        Assert.True(loaded.UpdatedAt >= profile.UpdatedAt);
    }

    [Fact]
    public async Task Database_UpgradesExistingRemoteProfilesWithOptionalProxyColumn()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"lessasr-proxy-migration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "client.db");
        try
        {
            await using (var legacyConnection = new SqliteConnection($"Data Source={databasePath}"))
            {
                await legacyConnection.OpenAsync();
                var command = legacyConnection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE remote_api_profiles (
                        id TEXT PRIMARY KEY,
                        name TEXT NOT NULL COLLATE NOCASE UNIQUE,
                        endpoint TEXT NOT NULL,
                        model TEXT NOT NULL,
                        protected_api_key TEXT NULL,
                        use_vocabulary INTEGER NOT NULL DEFAULT 0 CHECK(use_vocabulary IN (0, 1)),
                        created_at TEXT NOT NULL,
                        updated_at TEXT NOT NULL
                    );
                    """;
                await command.ExecuteNonQueryAsync();
            }

            await using (var database = await SqliteDatabase.OpenAsync(
                             databasePath,
                             CancellationToken.None))
            {
                var repository = new SqliteRemoteApiProfileRepository(database);
                var created = await repository.CreateAsync(
                    "迁移后配置",
                    "https://api.example.com/v1/audio/transcriptions",
                    "whisper-1",
                    protectedApiKey: null,
                    useVocabulary: false,
                    CancellationToken.None,
                    proxyUrl: "http://127.0.0.1:7890/");

                Assert.Equal("http://127.0.0.1:7890/", created.ProxyUrl);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
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
