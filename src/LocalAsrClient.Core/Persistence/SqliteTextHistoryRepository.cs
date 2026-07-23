using LocalAsrClient.Core.Abstractions;

namespace LocalAsrClient.Core.Persistence;

public sealed class SqliteTextHistoryRepository : ITextHistoryRepository
{
    private readonly SqliteDatabase _database;

    public SqliteTextHistoryRepository(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task AddAsync(TextHistoryEntry entry, CancellationToken cancellationToken)
    {
        var command = _database.Connection.CreateCommand();
        command.CommandText = """
            INSERT INTO transcript_history(
                id, created_at, text, character_count, word_count,
                recording_seconds, processing_seconds, backend_id, model_id)
            VALUES($id, $created_at, $text, $characters, $words, $recording, $processing, $backend, $model)
            """;
        command.Parameters.AddWithValue("$id", entry.Id.ToString());
        command.Parameters.AddWithValue("$created_at", entry.CreatedAt.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$text", entry.Text);
        command.Parameters.AddWithValue("$characters", entry.CharacterCount);
        command.Parameters.AddWithValue("$words", entry.WordCount);
        command.Parameters.AddWithValue("$recording", entry.RecordingDuration.TotalSeconds);
        command.Parameters.AddWithValue("$processing", entry.ProcessingDuration.TotalSeconds);
        command.Parameters.AddWithValue("$backend", entry.BackendId);
        command.Parameters.AddWithValue("$model", entry.ModelId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TextHistoryEntry>> GetRecentAsync(int limit, CancellationToken cancellationToken)
    {
        var command = _database.Connection.CreateCommand();
        command.CommandText = """
            SELECT id, created_at, text, character_count, word_count,
                   recording_seconds, processing_seconds, backend_id, model_id
            FROM transcript_history
            ORDER BY created_at DESC
            LIMIT $limit
            """;
        command.Parameters.AddWithValue("$limit", limit);

        var result = new List<TextHistoryEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new TextHistoryEntry(
                Guid.Parse(reader.GetString(0)),
                DateTimeOffset.Parse(reader.GetString(1)),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                TimeSpan.FromSeconds(reader.GetDouble(5)),
                TimeSpan.FromSeconds(reader.GetDouble(6)),
                reader.GetString(7),
                reader.GetString(8)));
        }

        return result;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var command = _database.Connection.CreateCommand();
        command.CommandText = "DELETE FROM transcript_history WHERE id = $id";
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task PruneAsync(DateTimeOffset now, TranscriptRetentionPolicy policy, CancellationToken cancellationToken)
    {
        if (policy == TranscriptRetentionPolicy.Disabled)
        {
            var deleteAll = _database.Connection.CreateCommand();
            deleteAll.CommandText = "DELETE FROM transcript_history";
            await deleteAll.ExecuteNonQueryAsync(cancellationToken);
            return;
        }

        var retention = policy.ToTimeSpan() ?? TimeSpan.Zero;
        var cutoff = now.Subtract(retention).ToUniversalTime().ToString("O");
        var command = _database.Connection.CreateCommand();
        command.CommandText = "DELETE FROM transcript_history WHERE created_at < $cutoff";
        command.Parameters.AddWithValue("$cutoff", cutoff);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
