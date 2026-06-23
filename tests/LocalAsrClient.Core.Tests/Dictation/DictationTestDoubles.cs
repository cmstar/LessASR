using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Asr;
using LocalAsrClient.Core.Persistence;

namespace LocalAsrClient.Core.Tests.Dictation;

public sealed class StubBackend : IAsrBackend
{
    public string Name => "Whisper Server";
    public AsrBackendStatus Status { get; set; } = AsrBackendStatus.Ready;
    public bool EnsureReadyCalled { get; private set; }
    public int TranscribeCallCount { get; private set; }
    public AsrRequest? LastRequest { get; private set; }
    public string TranscribeText { get; set; } = "测试文本";
    public Exception? TranscribeThrows { get; set; }
    public TimeSpan TranscribeDelay { get; set; }

    public Exception? EnsureReadyThrows { get; set; }

    public Task EnsureReadyAsync(CancellationToken cancellationToken)
    {
        EnsureReadyCalled = true;
        if (EnsureReadyThrows is not null)
        {
            throw EnsureReadyThrows;
        }

        Status = AsrBackendStatus.Ready;
        return Task.CompletedTask;
    }

    public async Task<AsrResult> TranscribeAsync(AsrRequest request, CancellationToken cancellationToken)
    {
        TranscribeCallCount++;
        LastRequest = request;
        if (TranscribeThrows is not null)
        {
            throw TranscribeThrows;
        }

        if (TranscribeDelay > TimeSpan.Zero)
        {
            await Task.Delay(TranscribeDelay, cancellationToken);
        }

        return new AsrResult(TranscribeText, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), null);
    }
}

public sealed class StubStatsRepository : IStatsRepository
{
    public List<DailyStatsDelta> Recorded { get; } = new();

    public Task RecordAsync(DailyStatsDelta delta, CancellationToken cancellationToken)
    {
        Recorded.Add(delta);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DailyStatsSnapshot>> GetRangeAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<DailyStatsSnapshot>>(Array.Empty<DailyStatsSnapshot>());
    }

    public Task PruneAsync(DateOnly today, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

public sealed class StubSettingsStore : ISettingsStore
{
    public AppSettings Settings { get; set; } = new(
        ModelPath: "model.bin",
        WhisperServerPath: "whisper-server.exe",
        WhisperServerPort: AppSettings.DefaultWhisperServerPort,
        TranscriptRetentionPolicy: TranscriptRetentionPolicy.SevenDays,
        StartModelOnAppStartup: false);

    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Settings);
    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class StubClock : IClock
{
    public DateTimeOffset Now => new(2026, 6, 7, 12, 0, 0, TimeSpan.Zero);
    public DateOnly Today => new(2026, 6, 7);
}
