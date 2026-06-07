using LocalAsrClient.Core.Abstractions;

namespace LocalAsrClient.Core.Persistence;

public sealed class SqliteSettingsStore : ISettingsStore
{
    private readonly SqliteDatabase _database;

    public SqliteSettingsStore(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var command = _database.Connection.CreateCommand();
        command.CommandText = "SELECT key, value FROM settings";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            values[reader.GetString(0)] = reader.GetString(1);
        }

        var defaults = AppSettings.CreateDefault(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        return defaults with
        {
            ModelPath = values.GetValueOrDefault("ModelPath", defaults.ModelPath),
            WhisperServerPath = values.GetValueOrDefault("WhisperServerPath", defaults.WhisperServerPath),
            DataDirectory = values.GetValueOrDefault("DataDirectory", defaults.DataDirectory),
            TranscriptRetentionPolicy = Enum.TryParse<TranscriptRetentionPolicy>(
                values.GetValueOrDefault("TranscriptRetentionPolicy"),
                out var policy) ? policy : defaults.TranscriptRetentionPolicy,
            StartModelOnAppStartup = bool.TryParse(values.GetValueOrDefault("StartModelOnAppStartup"), out var start) && start
        };
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, string>
        {
            ["ModelPath"] = settings.ModelPath,
            ["WhisperServerPath"] = settings.WhisperServerPath,
            ["DataDirectory"] = settings.DataDirectory,
            ["TranscriptRetentionPolicy"] = settings.TranscriptRetentionPolicy.ToString(),
            ["StartModelOnAppStartup"] = settings.StartModelOnAppStartup.ToString()
        };

        foreach (var pair in values)
        {
            var command = _database.Connection.CreateCommand();
            command.CommandText = """
                INSERT INTO settings(key, value)
                VALUES($key, $value)
                ON CONFLICT(key) DO UPDATE SET value = excluded.value
                """;
            command.Parameters.AddWithValue("$key", pair.Key);
            command.Parameters.AddWithValue("$value", pair.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}