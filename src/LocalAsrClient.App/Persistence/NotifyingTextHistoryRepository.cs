using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Persistence;

namespace LocalAsrClient.App.Persistence;

public sealed class NotifyingTextHistoryRepository : ITextHistoryRepository
{
    private readonly ITextHistoryRepository _inner;

    public NotifyingTextHistoryRepository(ITextHistoryRepository inner)
    {
        _inner = inner;
    }

    public event Action? Changed;

    public Task AddAsync(TextHistoryEntry entry, CancellationToken cancellationToken) =>
        _inner.AddAsync(entry, cancellationToken);

    public Task<IReadOnlyList<TextHistoryEntry>> GetRecentAsync(int limit, CancellationToken cancellationToken) =>
        _inner.GetRecentAsync(limit, cancellationToken);

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await _inner.DeleteAsync(id, cancellationToken);
        PublishChanged();
    }

    public async Task PruneAsync(
        DateTimeOffset now,
        TranscriptRetentionPolicy policy,
        CancellationToken cancellationToken)
    {
        await _inner.PruneAsync(now, policy, cancellationToken);
        PublishChanged();
    }

    private void PublishChanged()
    {
        var handlers = Changed;
        if (handlers is null)
        {
            return;
        }

        foreach (Action handler in handlers.GetInvocationList())
        {
            try
            {
                handler();
            }
            catch
            {
                // History persistence must not fail because a UI observer failed.
            }
        }
    }
}
