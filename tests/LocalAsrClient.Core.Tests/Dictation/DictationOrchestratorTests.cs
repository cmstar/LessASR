using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Asr;
using LocalAsrClient.Core.Dictation;
using LocalAsrClient.Core.Persistence;
using LocalAsrClient.Core.Text;

namespace LocalAsrClient.Core.Tests.Dictation;

public sealed class DictationOrchestratorTests
{
    [Fact]
    public async Task ToggleAsync_FirstPressEnsuresModel_WhenModelIsStopped()
    {
        var fixture = new Fixture();
        fixture.Backend.Status = AsrBackendStatus.Stopped;

        await fixture.Orchestrator.ToggleAsync(CancellationToken.None);

        Assert.True(fixture.Backend.EnsureReadyCalled);
        Assert.Equal(DictationState.Ready, fixture.LastStatus.State);
        Assert.False(fixture.Recorder.Started);
    }

    [Fact]
    public async Task ToggleAsync_FirstPressStartsRecording_WhenModelReady()
    {
        var fixture = new Fixture();
        fixture.Backend.Status = AsrBackendStatus.Ready;

        await fixture.Orchestrator.ToggleAsync(CancellationToken.None);

        Assert.Equal(DictationState.Recording, fixture.LastStatus.State);
        Assert.True(fixture.Recorder.Started);
    }

    [Fact]
    public async Task ToggleAsync_SecondPressTranscribesAndInjectsText()
    {
        var fixture = new Fixture();
        fixture.Backend.Status = AsrBackendStatus.Ready;

        await fixture.Orchestrator.ToggleAsync(CancellationToken.None);
        await fixture.Orchestrator.ToggleAsync(CancellationToken.None);

        Assert.Equal("测试文本", fixture.Injector.Text);
        Assert.Equal(DictationState.Idle, fixture.LastStatus.State);
        Assert.Equal(1, fixture.Stats.Recorded.Count);
        Assert.Single(fixture.History.Entries);
    }

    [Fact]
    public async Task ToggleAsync_WhenInjectionFailsLeavesResultNeedsAction()
    {
        var fixture = new Fixture();
        fixture.Backend.Status = AsrBackendStatus.Ready;
        fixture.Injector.Result = new TextInjectionResult(TextInjectionStatus.NoEditableTarget, "没有输入框");

        await fixture.Orchestrator.ToggleAsync(CancellationToken.None);
        await fixture.Orchestrator.ToggleAsync(CancellationToken.None);

        Assert.Equal(DictationState.ResultNeedsAction, fixture.LastStatus.State);
        Assert.Equal("测试文本", fixture.LastStatus.ResultText);
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Recorder = new StubRecorder();
            Backend = new StubBackend();
            Injector = new StubInjector();
            Stats = new StubStatsRepository();
            History = new StubHistoryRepository();
            Settings = new StubSettingsStore();
            Clock = new StubClock();
            Orchestrator = new DictationOrchestrator(Recorder, Backend, Injector, Stats, History, Settings, Clock);
            Orchestrator.StatusChanged += status => LastStatus = status;
        }

        public StubRecorder Recorder { get; }
        public StubBackend Backend { get; }
        public StubInjector Injector { get; }
        public StubStatsRepository Stats { get; }
        public StubHistoryRepository History { get; }
        public StubSettingsStore Settings { get; }
        public StubClock Clock { get; }
        public DictationOrchestrator Orchestrator { get; }
        public DictationStatus LastStatus { get; private set; } = new(DictationState.Idle, "空闲");
    }

    private sealed class StubRecorder : IAudioRecorder
    {
        public bool Started { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Started = true;
            return Task.CompletedTask;
        }

        public Task<RecordingResult> StopAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new RecordingResult(new byte[] { 1, 2, 3 }, TimeSpan.FromSeconds(2), 16000, 1));
        }
    }

    private sealed class StubBackend : IAsrBackend
    {
        public string Name => "Whisper Server";
        public AsrBackendStatus Status { get; set; } = AsrBackendStatus.Ready;
        public bool EnsureReadyCalled { get; private set; }

        public Task EnsureReadyAsync(CancellationToken cancellationToken)
        {
            EnsureReadyCalled = true;
            Status = AsrBackendStatus.Ready;
            return Task.CompletedTask;
        }

        public Task<AsrResult> TranscribeAsync(AsrRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new AsrResult("测试文本", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), null));
        }
    }

    private sealed class StubInjector : ITextInjector
    {
        public TextInjectionResult Result { get; set; } = new(TextInjectionStatus.Success, null);
        public string? Text { get; private set; }

        public Task<TextInjectionResult> TryInjectAsync(string text, CancellationToken cancellationToken)
        {
            Text = text;
            return Task.FromResult(Result);
        }
    }

    private sealed class StubStatsRepository : IStatsRepository
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

    private sealed class StubHistoryRepository : ITextHistoryRepository
    {
        public List<TextHistoryEntry> Entries { get; } = new();

        public Task AddAsync(TextHistoryEntry entry, CancellationToken cancellationToken)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TextHistoryEntry>> GetRecentAsync(int limit, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<TextHistoryEntry>>(Entries);
        }

        public Task PruneAsync(DateTimeOffset now, TranscriptRetentionPolicy policy, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class StubSettingsStore : ISettingsStore
    {
        private readonly AppSettings _settings = new(
            ModelPath: "model.bin",
            WhisperServerPath: "whisper-server.exe",
            DataDirectory: "data",
            TranscriptRetentionPolicy: TranscriptRetentionPolicy.SevenDays,
            StartModelOnAppStartup: false);

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(_settings);
        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubClock : IClock
    {
        public DateTimeOffset Now => new(2026, 6, 7, 12, 0, 0, TimeSpan.Zero);
        public DateOnly Today => new(2026, 6, 7);
    }
}
