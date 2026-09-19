using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Persistence;

namespace LocalAsrClient.App.Persistence;

public sealed class CachedStatsRepository : IStatsRepository
{
    private readonly IStatsRepository _inner;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<(DateOnly Date, string Provider), DailyStatsSnapshot> _days;

    private CachedStatsRepository(IStatsRepository inner, IEnumerable<DailyStatsSnapshot> days)
    {
        _inner = inner;
        _days = days.ToDictionary(day => (day.Date, day.ProviderName));
    }

    public static async Task<CachedStatsRepository> CreateAsync(
        IStatsRepository inner, CancellationToken cancellationToken)
    {
        var days = await inner.GetRangeAsync(DateOnly.MinValue, DateOnly.MaxValue, cancellationToken);
        return new CachedStatsRepository(inner, days);
    }

    public async Task RecordAsync(DailyStatsDelta delta, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await _inner.RecordAsync(delta, cancellationToken);
            // 写入成功后同步更新内存；此时不再检查取消，避免已落库的数据漏计。
            var key = (delta.Date, delta.ProviderName);
            var previous = _days.GetValueOrDefault(key)
                ?? new DailyStatsSnapshot(delta.Date, delta.ProviderName, 0, 0, 0, 0, 0, 0, 0);
            _days[key] = previous with
            {
                InputCount = previous.InputCount + 1,
                SuccessCount = previous.SuccessCount + (delta.Succeeded ? 1 : 0),
                FailedCount = previous.FailedCount + (delta.Succeeded ? 0 : 1),
                RecordingSeconds = previous.RecordingSeconds + delta.RecordingDuration.TotalSeconds,
                ProcessingSeconds = previous.ProcessingSeconds + delta.ProcessingDuration.TotalSeconds,
                CharacterCount = previous.CharacterCount + delta.CharacterCount,
                WordCount = previous.WordCount + delta.WordCount
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<DailyStatsSnapshot>> GetRangeAsync(
        DateOnly start, DateOnly end, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return _days.Values
                .Where(day => day.Date >= start && day.Date <= end)
                .OrderBy(day => day.Date)
                .ThenBy(day => day.ProviderName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task PruneAsync(DateOnly today, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await _inner.PruneAsync(today, cancellationToken);
            var cutoff = today.AddDays(-62);
            foreach (var key in _days.Keys.Where(key => key.Date < cutoff).ToArray())
            {
                _days.Remove(key);
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
