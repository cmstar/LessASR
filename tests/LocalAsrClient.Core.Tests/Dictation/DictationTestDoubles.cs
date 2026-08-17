using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Asr;
using LocalAsrClient.Core.Dictation;
using LocalAsrClient.Core.Persistence;

namespace LocalAsrClient.Core.Tests.Dictation;

public sealed class StubRecorder : IAudioRecorder
{
    public bool Started { get; private set; }
    public TimeSpan DurationOverride { get; set; } = TimeSpan.FromSeconds(2);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Started = true;
        return Task.CompletedTask;
    }

    public Task<RecordingResult> StopAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(new RecordingResult(new byte[1000], DurationOverride, 16000, 1));
    }
}

public sealed class StubBackend : IAsrBackend
{
    public string Name => "Whisper Server";
    public string ModelId { get; set; } = "ggml-large-v3-turbo";
    public AsrBackendStatus Status { get; set; } = AsrBackendStatus.Ready;
    public bool EnsureReadyCalled { get; private set; }
    public int TranscribeCallCount { get; private set; }
    public AsrRequest? LastRequest { get; private set; }
    public string TranscribeText { get; set; } = "测试文本";
    public Exception? TranscribeThrows { get; set; }
    public TimeSpan TranscribeDelay { get; set; }
    public Action? AfterTranscribe { get; set; }
    public Queue<Func<AsrResult>> TranscribeOutcomes { get; } = new();

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

        var result = TranscribeOutcomes.Count > 0
            ? TranscribeOutcomes.Dequeue().Invoke()
            : new AsrResult(TranscribeText, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), null);
        AfterTranscribe?.Invoke();
        return result;
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

public sealed class StubVocabularyRepository : IVocabularyRepository
{
    public List<VocabularyProfile> Profiles { get; } = [];

    public VocabularyProfile? ActiveProfile
    {
        get => Profiles.SingleOrDefault(profile => profile.IsActive);
        set
        {
            for (var index = 0; index < Profiles.Count; index++)
            {
                Profiles[index] = Profiles[index] with { IsActive = false };
            }

            if (value is not null)
            {
                var existingIndex = Profiles.FindIndex(profile => profile.Id == value.Id);
                var active = value with { IsActive = true };
                if (existingIndex >= 0)
                {
                    Profiles[existingIndex] = active;
                }
                else
                {
                    Profiles.Add(active);
                }
            }
        }
    }

    public Task<IReadOnlyList<VocabularyProfile>> GetAllAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<VocabularyProfile>>(Profiles.ToArray());
    }

    public Task<VocabularyProfile?> GetActiveAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(ActiveProfile);
    }

    public Task<VocabularyProfile> CreateAsync(string name, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var profile = new VocabularyProfile(
            Guid.NewGuid(),
            name,
            string.Empty,
            Profiles.Count == 0,
            now,
            now);
        Profiles.Add(profile);
        return Task.FromResult(profile);
    }

    public Task UpdateAsync(
        Guid id,
        string name,
        string entriesText,
        CancellationToken cancellationToken)
    {
        var index = Profiles.FindIndex(profile => profile.Id == id);
        Profiles[index] = Profiles[index] with { Name = name, EntriesText = entriesText };
        return Task.CompletedTask;
    }

    public Task SetActiveAsync(Guid? id, CancellationToken cancellationToken)
    {
        ActiveProfile = id is null ? null : Profiles.Single(profile => profile.Id == id);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        Profiles.RemoveAll(profile => profile.Id == id);
        return Task.CompletedTask;
    }
}

public sealed class StubClock : IClock
{
    public DateTimeOffset Now => new(2026, 6, 7, 12, 0, 0, TimeSpan.Zero);
    public DateOnly Today => new(2026, 6, 7);
}
