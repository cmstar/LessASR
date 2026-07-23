using System.Globalization;
using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Asr;
using Microsoft.Data.Sqlite;

namespace LocalAsrClient.Core.Persistence;

public sealed class SqliteVocabularyRepository : IVocabularyRepository
{
    private readonly SqliteDatabase _database;
    private readonly IClock _clock;

    public SqliteVocabularyRepository(SqliteDatabase database, IClock clock)
    {
        _database = database;
        _clock = clock;
    }

    public async Task<IReadOnlyList<VocabularyProfile>> GetAllAsync(CancellationToken cancellationToken)
    {
        var profiles = new List<VocabularyProfile>();
        var command = _database.Connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, entries_text, is_active, created_at, updated_at
            FROM vocabulary_profiles
            ORDER BY created_at, rowid
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            profiles.Add(ReadProfile(reader));
        }

        return profiles;
    }

    public async Task<VocabularyProfile?> GetActiveAsync(CancellationToken cancellationToken)
    {
        var command = _database.Connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, entries_text, is_active, created_at, updated_at
            FROM vocabulary_profiles
            WHERE is_active = 1
            LIMIT 1
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadProfile(reader) : null;
    }

    public async Task<VocabularyProfile> CreateAsync(string name, CancellationToken cancellationToken)
    {
        var normalizedName = RequireValidName(name);
        await EnsureUniqueNameAsync(normalizedName, excludedId: null, cancellationToken);
        var now = _clock.Now;
        var id = Guid.NewGuid();
        using var transaction = _database.Connection.BeginTransaction();

        var countCommand = _database.Connection.CreateCommand();
        countCommand.Transaction = transaction;
        countCommand.CommandText = "SELECT COUNT(*) FROM vocabulary_profiles";
        var count = Convert.ToInt32(
            await countCommand.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);
        var isActive = count == 0;

        try
        {
            var command = _database.Connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO vocabulary_profiles(
                    id, name, entries_text, is_active, created_at, updated_at)
                VALUES($id, $name, '', $isActive, $createdAt, $updatedAt)
                """;
            command.Parameters.AddWithValue("$id", id.ToString());
            command.Parameters.AddWithValue("$name", normalizedName);
            command.Parameters.AddWithValue("$isActive", isActive ? 1 : 0);
            command.Parameters.AddWithValue("$createdAt", now.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$updatedAt", now.ToString("O", CultureInfo.InvariantCulture));
            await command.ExecuteNonQueryAsync(cancellationToken);
            transaction.Commit();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException("词汇表名称不能重复。", ex);
        }

        return new VocabularyProfile(id, normalizedName, string.Empty, isActive, now, now);
    }

    public async Task UpdateAsync(
        Guid id,
        string name,
        string entriesText,
        CancellationToken cancellationToken)
    {
        var normalizedName = RequireValidName(name);
        await EnsureUniqueNameAsync(normalizedName, id, cancellationToken);
        var parsedEntries = WhisperVocabulary.Parse(entriesText);
        if (!parsedEntries.IsValid)
        {
            throw new InvalidOperationException(parsedEntries.ErrorMessage);
        }

        try
        {
            var command = _database.Connection.CreateCommand();
            command.CommandText = """
                UPDATE vocabulary_profiles
                SET name = $name,
                    entries_text = $entriesText,
                    updated_at = $updatedAt
                WHERE id = $id
                """;
            command.Parameters.AddWithValue("$id", id.ToString());
            command.Parameters.AddWithValue("$name", normalizedName);
            command.Parameters.AddWithValue("$entriesText", parsedEntries.NormalizedText);
            command.Parameters.AddWithValue(
                "$updatedAt",
                _clock.Now.ToString("O", CultureInfo.InvariantCulture));
            var affected = await command.ExecuteNonQueryAsync(cancellationToken);
            if (affected == 0)
            {
                throw new KeyNotFoundException("找不到要更新的词汇表。");
            }
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException("词汇表名称不能重复。", ex);
        }
    }

    public async Task SetActiveAsync(Guid? id, CancellationToken cancellationToken)
    {
        using var transaction = _database.Connection.BeginTransaction();
        if (id is not null)
        {
            var existsCommand = _database.Connection.CreateCommand();
            existsCommand.Transaction = transaction;
            existsCommand.CommandText = "SELECT COUNT(*) FROM vocabulary_profiles WHERE id = $id";
            existsCommand.Parameters.AddWithValue("$id", id.Value.ToString());
            var exists = Convert.ToInt32(
                await existsCommand.ExecuteScalarAsync(cancellationToken),
                CultureInfo.InvariantCulture) > 0;
            if (!exists)
            {
                throw new KeyNotFoundException("找不到要启用的词汇表。");
            }
        }

        var clearCommand = _database.Connection.CreateCommand();
        clearCommand.Transaction = transaction;
        clearCommand.CommandText = "UPDATE vocabulary_profiles SET is_active = 0 WHERE is_active = 1";
        await clearCommand.ExecuteNonQueryAsync(cancellationToken);

        if (id is not null)
        {
            var activateCommand = _database.Connection.CreateCommand();
            activateCommand.Transaction = transaction;
            activateCommand.CommandText = "UPDATE vocabulary_profiles SET is_active = 1 WHERE id = $id";
            activateCommand.Parameters.AddWithValue("$id", id.Value.ToString());
            await activateCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = _database.Connection.CreateCommand();
        command.CommandText = "DELETE FROM vocabulary_profiles WHERE id = $id";
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static VocabularyProfile ReadProfile(SqliteDataReader reader)
    {
        return new VocabularyProfile(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt32(3) == 1,
            DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture));
    }

    private static string RequireValidName(string name)
    {
        var result = VocabularyProfileName.Validate(name);
        if (!result.IsValid)
        {
            throw new InvalidOperationException(result.ErrorMessage);
        }

        return result.NormalizedName;
    }

    private async Task EnsureUniqueNameAsync(
        string name,
        Guid? excludedId,
        CancellationToken cancellationToken)
    {
        var profiles = await GetAllAsync(cancellationToken);
        if (profiles.Any(profile =>
                profile.Id != excludedId
                && string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("词汇表名称不能重复。");
        }
    }
}
