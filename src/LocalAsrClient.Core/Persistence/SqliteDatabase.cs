using Microsoft.Data.Sqlite;

namespace LocalAsrClient.Core.Persistence;

public sealed class SqliteDatabase : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    private SqliteDatabase(SqliteConnection connection)
    {
        _connection = connection;
    }

    public SqliteConnection Connection => _connection;

    public static async Task<SqliteDatabase> OpenAsync(string databasePath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync(cancellationToken);
        var database = new SqliteDatabase(connection);
        await database.InitializeAsync(cancellationToken);
        return database;
    }

    public static async Task<SqliteDatabase> CreateInMemoryAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var database = new SqliteDatabase(connection);
        await database.InitializeAsync(CancellationToken.None);
        return database;
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var command = _connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS daily_stats (
                date TEXT PRIMARY KEY,
                input_count INTEGER NOT NULL,
                success_count INTEGER NOT NULL,
                failed_count INTEGER NOT NULL,
                recording_seconds REAL NOT NULL,
                processing_seconds REAL NOT NULL,
                character_count INTEGER NOT NULL,
                word_count INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS transcript_history (
                id TEXT PRIMARY KEY,
                created_at TEXT NOT NULL,
                text TEXT NOT NULL,
                character_count INTEGER NOT NULL,
                word_count INTEGER NOT NULL,
                recording_seconds REAL NOT NULL,
                processing_seconds REAL NOT NULL,
                backend_id TEXT NOT NULL,
                model_id TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        return _connection.DisposeAsync();
    }
}

public sealed record DailyStatsDelta(
    DateOnly Date,
    bool Succeeded,
    TimeSpan RecordingDuration,
    TimeSpan ProcessingDuration,
    int CharacterCount,
    int WordCount);

public sealed record DailyStatsSnapshot(
    DateOnly Date,
    int InputCount,
    int SuccessCount,
    int FailedCount,
    double RecordingSeconds,
    double ProcessingSeconds,
    int CharacterCount,
    int WordCount);

public sealed record TextHistoryEntry(
    Guid Id,
    DateTimeOffset CreatedAt,
    string Text,
    int CharacterCount,
    int WordCount,
    TimeSpan RecordingDuration,
    TimeSpan ProcessingDuration,
    string BackendId,
    string ModelId);