using System.Globalization;
using LocalAsrClient.Core.Abstractions;
using Microsoft.Data.Sqlite;

namespace LocalAsrClient.Core.Persistence;

public sealed class SqliteRemoteApiProfileRepository : IRemoteApiProfileRepository
{
    private readonly SqliteDatabase _database;

    public SqliteRemoteApiProfileRepository(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task<IReadOnlyList<RemoteApiProfile>> GetAllAsync(CancellationToken cancellationToken)
    {
        var profiles = new List<RemoteApiProfile>();
        var command = _database.Connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, endpoint, model, protected_api_key, use_vocabulary, proxy_url, created_at, updated_at
            FROM remote_api_profiles
            ORDER BY created_at, rowid
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            profiles.Add(ReadProfile(reader));
        }

        return profiles;
    }

    public async Task<RemoteApiProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = _database.Connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, endpoint, model, protected_api_key, use_vocabulary, proxy_url, created_at, updated_at
            FROM remote_api_profiles
            WHERE id = $id
            LIMIT 1
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadProfile(reader) : null;
    }

    public async Task<RemoteApiProfile> CreateAsync(
        string name,
        string endpoint,
        string model,
        string? protectedApiKey,
        bool useVocabulary,
        CancellationToken cancellationToken,
        string? proxyUrl = null)
    {
        var normalizedName = RequireName(name);
        var now = DateTimeOffset.UtcNow;
        var profile = new RemoteApiProfile(
            Guid.NewGuid(),
            normalizedName,
            endpoint.Trim(),
            model.Trim(),
            protectedApiKey,
            useVocabulary,
            now,
            now,
            NormalizeOptional(proxyUrl));

        try
        {
            var command = _database.Connection.CreateCommand();
            command.CommandText = """
                INSERT INTO remote_api_profiles(
                    id, name, endpoint, model, protected_api_key, use_vocabulary, proxy_url, created_at, updated_at)
                VALUES($id, $name, $endpoint, $model, $protectedApiKey, $useVocabulary, $proxyUrl, $createdAt, $updatedAt)
                """;
            BindProfile(command, profile);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException("远程 API 配置名称不能重复。", ex);
        }

        return profile;
    }

    public async Task UpdateAsync(
        Guid id,
        string name,
        string endpoint,
        string model,
        string? protectedApiKey,
        bool useVocabulary,
        CancellationToken cancellationToken,
        string? proxyUrl = null)
    {
        var normalizedName = RequireName(name);
        try
        {
            var command = _database.Connection.CreateCommand();
            command.CommandText = """
                UPDATE remote_api_profiles
                SET name = $name,
                    endpoint = $endpoint,
                    model = $model,
                    protected_api_key = $protectedApiKey,
                    use_vocabulary = $useVocabulary,
                    proxy_url = $proxyUrl,
                    updated_at = $updatedAt
                WHERE id = $id
                """;
            command.Parameters.AddWithValue("$id", id.ToString());
            command.Parameters.AddWithValue("$name", normalizedName);
            command.Parameters.AddWithValue("$endpoint", endpoint.Trim());
            command.Parameters.AddWithValue("$model", model.Trim());
            command.Parameters.AddWithValue("$protectedApiKey", (object?)protectedApiKey ?? DBNull.Value);
            command.Parameters.AddWithValue("$useVocabulary", useVocabulary ? 1 : 0);
            command.Parameters.AddWithValue("$proxyUrl", (object?)NormalizeOptional(proxyUrl) ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "$updatedAt",
                DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            var affected = await command.ExecuteNonQueryAsync(cancellationToken);
            if (affected == 0)
            {
                throw new KeyNotFoundException("找不到要更新的远程 API 配置。");
            }
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException("远程 API 配置名称不能重复。", ex);
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = _database.Connection.CreateCommand();
        command.CommandText = "DELETE FROM remote_api_profiles WHERE id = $id";
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void BindProfile(SqliteCommand command, RemoteApiProfile profile)
    {
        command.Parameters.AddWithValue("$id", profile.Id.ToString());
        command.Parameters.AddWithValue("$name", profile.Name);
        command.Parameters.AddWithValue("$endpoint", profile.Endpoint);
        command.Parameters.AddWithValue("$model", profile.Model);
        command.Parameters.AddWithValue("$protectedApiKey", (object?)profile.ProtectedApiKey ?? DBNull.Value);
        command.Parameters.AddWithValue("$useVocabulary", profile.UseVocabulary ? 1 : 0);
        command.Parameters.AddWithValue("$proxyUrl", (object?)profile.ProxyUrl ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$createdAt",
            profile.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue(
            "$updatedAt",
            profile.UpdatedAt.ToString("O", CultureInfo.InvariantCulture));
    }

    private static RemoteApiProfile ReadProfile(SqliteDataReader reader)
    {
        return new RemoteApiProfile(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetInt32(5) == 1,
            DateTimeOffset.Parse(reader.GetString(7), CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(reader.GetString(8), CultureInfo.InvariantCulture),
            reader.IsDBNull(6) ? null : reader.GetString(6));
    }

    private static string RequireName(string name)
    {
        var normalizedName = name.Trim();
        if (normalizedName.Length == 0)
        {
            throw new InvalidOperationException("远程 API 配置名称不能为空。");
        }

        return normalizedName;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
