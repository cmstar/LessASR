using LocalAsrClient.Core.Persistence;

namespace LocalAsrClient.Core.Abstractions;

public interface ITextHistoryRepository
{
    Task AddAsync(TextHistoryEntry entry, CancellationToken cancellationToken);
    Task<IReadOnlyList<TextHistoryEntry>> GetRecentAsync(int limit, CancellationToken cancellationToken);
    Task PruneAsync(DateTimeOffset now, TranscriptRetentionPolicy policy, CancellationToken cancellationToken);
}