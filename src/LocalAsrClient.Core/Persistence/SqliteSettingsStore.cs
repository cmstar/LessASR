using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Asr;

namespace LocalAsrClient.Core.Persistence;

public sealed class SqliteSettingsStore : ISettingsStore
{
    private readonly SqliteDatabase _database;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SqliteSettingsStore(SqliteDatabase database)
    {
        _database = database;
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await LoadCoreAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<AppSettings> LoadCoreAsync(CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var command = _database.Connection.CreateCommand();
        command.CommandText = "SELECT key, value FROM settings";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            values[reader.GetString(0)] = reader.GetString(1);
        }

        var defaults = AppSettings.CreateDefault();
        return defaults with
        {
            ModelPath = values.GetValueOrDefault("ModelPath", defaults.ModelPath),
            WhisperServerPath = values.GetValueOrDefault("WhisperServerPath", defaults.WhisperServerPath),
            WhisperServerPort = int.TryParse(values.GetValueOrDefault("WhisperServerPort"), out var port)
                ? port
                : defaults.WhisperServerPort,
            WhisperServerThreadCount = int.TryParse(values.GetValueOrDefault("WhisperServerThreadCount"), out var threads)
                && threads > 0
                ? threads
                : null,
            TranscriptRetentionPolicy = Enum.TryParse<TranscriptRetentionPolicy>(
                values.GetValueOrDefault("TranscriptRetentionPolicy"),
                out var policy) ? policy : defaults.TranscriptRetentionPolicy,
            StartModelOnAppStartup = bool.TryParse(values.GetValueOrDefault("StartModelOnAppStartup"), out var start) && start,
            MinimizeToTrayOnClose = !bool.TryParse(values.GetValueOrDefault("MinimizeToTrayOnClose"), out var minimize) || minimize,
            PreferredTranscriptionLanguageId = TranscriptionLanguageCatalog.NormalizeId(
                values.GetValueOrDefault("PreferredTranscriptionLanguageId")),
            ActiveRemoteApiProfileId = Guid.TryParse(
                values.GetValueOrDefault("ActiveRemoteApiProfileId"),
                out var activeRemoteApiProfileId)
                ? activeRemoteApiProfileId
                : null
        };
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await SaveCoreAsync(settings, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateAsync(
        Func<AppSettings, AppSettings> update,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var settings = await LoadCoreAsync(cancellationToken);
            await SaveCoreAsync(update(settings), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SaveCoreAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, string>
        {
            ["ModelPath"] = settings.ModelPath,
            ["WhisperServerPath"] = settings.WhisperServerPath,
            ["WhisperServerPort"] = settings.WhisperServerPort.ToString(),
            ["WhisperServerThreadCount"] = settings.WhisperServerThreadCount?.ToString() ?? string.Empty,
            ["TranscriptRetentionPolicy"] = settings.TranscriptRetentionPolicy.ToString(),
            ["StartModelOnAppStartup"] = settings.StartModelOnAppStartup.ToString(),
            ["MinimizeToTrayOnClose"] = settings.MinimizeToTrayOnClose.ToString(),
            ["PreferredTranscriptionLanguageId"] = settings.PreferredTranscriptionLanguageId,
            ["ActiveRemoteApiProfileId"] = settings.ActiveRemoteApiProfileId?.ToString() ?? string.Empty
        };

        await using var transaction = await _database.Connection.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var pair in values)
            {
                var command = _database.Connection.CreateCommand();
                command.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
                command.CommandText = """
                    INSERT INTO settings(key, value)
                    VALUES($key, $value)
                    ON CONFLICT(key) DO UPDATE SET value = excluded.value
                    """;
                command.Parameters.AddWithValue("$key", pair.Key);
                command.Parameters.AddWithValue("$value", pair.Value);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
