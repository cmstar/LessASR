using LocalAsrClient.Core.Abstractions;

namespace LocalAsrClient.Core.Persistence;

public sealed class SqliteStatsRepository : IStatsRepository
{
    private readonly SqliteDatabase _database;

    public SqliteStatsRepository(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task RecordAsync(DailyStatsDelta delta, CancellationToken cancellationToken)
    {
        var command = _database.Connection.CreateCommand();
        command.CommandText = """
            INSERT INTO daily_stats(
                date, backend_id, input_count, success_count, failed_count, recording_seconds,
                processing_seconds, character_count, word_count)
            VALUES($date, $backendId, 1, $success, $failed, $recording, $processing, $characters, $words)
            ON CONFLICT(date, backend_id) DO UPDATE SET
                input_count = input_count + 1,
                success_count = success_count + $success,
                failed_count = failed_count + $failed,
                recording_seconds = recording_seconds + $recording,
                processing_seconds = processing_seconds + $processing,
                character_count = character_count + $characters,
                word_count = word_count + $words
            """;
        command.Parameters.AddWithValue("$date", delta.Date.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$backendId", delta.ProviderName);
        command.Parameters.AddWithValue("$success", delta.Succeeded ? 1 : 0);
        command.Parameters.AddWithValue("$failed", delta.Succeeded ? 0 : 1);
        command.Parameters.AddWithValue("$recording", delta.RecordingDuration.TotalSeconds);
        command.Parameters.AddWithValue("$processing", delta.ProcessingDuration.TotalSeconds);
        command.Parameters.AddWithValue("$characters", delta.CharacterCount);
        command.Parameters.AddWithValue("$words", delta.WordCount);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DailyStatsSnapshot>> GetRangeAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken)
    {
        var command = _database.Connection.CreateCommand();
        command.CommandText = """
            SELECT date, backend_id, input_count, success_count, failed_count, recording_seconds,
                   processing_seconds, character_count, word_count
            FROM daily_stats
            WHERE date >= $start AND date <= $end
            ORDER BY date, backend_id COLLATE NOCASE
            """;
        command.Parameters.AddWithValue("$start", start.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$end", end.ToString("yyyy-MM-dd"));

        var result = new List<DailyStatsSnapshot>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new DailyStatsSnapshot(
                DateOnly.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetDouble(5),
                reader.GetDouble(6),
                reader.GetInt32(7),
                reader.GetInt32(8)));
        }

        return result;
    }

    public async Task PruneAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var cutoff = today.AddDays(-62);
        var command = _database.Connection.CreateCommand();
        command.CommandText = "DELETE FROM daily_stats WHERE date < $cutoff";
        command.Parameters.AddWithValue("$cutoff", cutoff.ToString("yyyy-MM-dd"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
