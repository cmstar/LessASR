using LocalAsrClient.Core.Persistence;

namespace LocalAsrClient.Core.Abstractions;

public interface ISettingsStore
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken);

    async Task UpdateAsync(
        Func<AppSettings, AppSettings> update,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        var settings = await LoadAsync(cancellationToken);
        await SaveAsync(update(settings), cancellationToken);
    }
}
