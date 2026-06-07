using LocalAsrClient.Core.Persistence;

namespace LocalAsrClient.Core.Abstractions;

public interface IStatsRepository
{
    Task RecordAsync(DailyStatsDelta delta, CancellationToken cancellationToken);
    Task<IReadOnlyList<DailyStatsSnapshot>> GetRangeAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken);
    Task PruneAsync(DateOnly today, CancellationToken cancellationToken);
}