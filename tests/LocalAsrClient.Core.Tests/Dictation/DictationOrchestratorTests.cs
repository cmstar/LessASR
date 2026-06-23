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
        Assert.Equal(DictationState.Recording, fixture.LastStatus.State);
        Assert.Equal("聆听中", fixture.LastStatus.Message);
        Assert.True(fixture.Recorder.Started);
    }

    [Fact]
    public async Task ToggleAsync_FirstPressStartsRecording_WhenModelReady()
    {
        var fixture = new Fixture();
        fixture.Backend.Status = AsrBackendStatus.Ready;

        await fixture.Orchestrator.ToggleAsync(CancellationToken.None);

        Assert.Equal(DictationState.Recording, fixture.LastStatus.State);
        Assert.Equal("聆听中", fixture.LastStatus.Message);
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

    [Fact]
    public async Task ToggleAsync_WhenEnsureReadyFails_SetsErrorState()
    {
        var fixture = new Fixture();
        fixture.Backend.Status = AsrBackendStatus.Stopped;
        fixture.Backend.EnsureReadyThrows = new TimeoutException("等待 whisper-server 启动超时（120 秒）。");

        await fixture.Orchestrator.ToggleAsync(CancellationToken.None);

        Assert.Equal(DictationState.Error, fixture.LastStatus.State);
        Assert.Equal("模型加载失败", fixture.LastStatus.Message);
        Assert.Contains("超时", fixture.LastStatus.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ToggleAsync_FromError_StartsNewRecording()
    {
        var fixture = new Fixture();
        fixture.Backend.Status = AsrBackendStatus.Ready;

        await fixture.Orchestrator.ToggleAsync(CancellationToken.None);
        fixture.Backend.TranscribeThrows = new InvalidOperationException("转写失败");
        await fixture.Orchestrator.ToggleAsync(CancellationToken.None);

        Assert.Equal(DictationState.Error, fixture.LastStatus.State);

        await fixture.Orchestrator.ToggleAsync(CancellationToken.None);

        Assert.Equal(DictationState.Recording, fixture.LastStatus.State);
        Assert.Equal("聆听中", fixture.LastStatus.Message);
        Assert.True(fixture.Recorder.Started);
    }

    [Fact]
    public async Task ToggleAsync_FromResultNeedsAction_StartsNewRecording()
    {
        var fixture = new Fixture();
        fixture.Backend.Status = AsrBackendStatus.Ready;
        fixture.Injector.Result = new TextInjectionResult(TextInjectionStatus.NoEditableTarget, "没有输入框");

        await fixture.Orchestrator.ToggleAsync(CancellationToken.None);
        await fixture.Orchestrator.ToggleAsync(CancellationToken.None);

        Assert.Equal(DictationState.ResultNeedsAction, fixture.LastStatus.State);

        await fixture.Orchestrator.ToggleAsync(CancellationToken.None);

        Assert.Equal(DictationState.Recording, fixture.LastStatus.State);
    }

    [Fact]
    public async Task ToggleAsync_WhenInjectionReturnsFailed_UsesInjectorMessage()
    {
        var fixture = new Fixture();
        fixture.Backend.Status = AsrBackendStatus.Ready;
        fixture.Injector.Result = new TextInjectionResult(TextInjectionStatus.Failed, "SendInput 被拒绝");

        await fixture.Orchestrator.ToggleAsync(CancellationToken.None);
        await fixture.Orchestrator.ToggleAsync(CancellationToken.None);

        Assert.Equal(DictationState.ResultNeedsAction, fixture.LastStatus.State);
        Assert.Equal("SendInput 被拒绝", fixture.LastStatus.Message);
    }

    [Fact]
    public async Task ToggleAsync_WhenTranscriptionEmpty_ShowsEmptyTextMessage()
    {
        var fixture = new Fixture();
        fixture.Backend.Status = AsrBackendStatus.Ready;
        fixture.Backend.TranscribeText = "";

        await fixture.Orchestrator.ToggleAsync(CancellationToken.None);
        await fixture.Orchestrator.ToggleAsync(CancellationToken.None);

        Assert.Equal(DictationState.ResultNeedsAction, fixture.LastStatus.State);
        Assert.Equal("识别文本为空", fixture.LastStatus.Message);
        Assert.Single(fixture.Stats.Recorded);
        Assert.False(fixture.Stats.Recorded[0].Succeeded);
        Assert.Empty(fixture.History.Entries);
    }

    [Fact]
    public async Task ToggleAsync_WhenTranscribeThrows_RecordsFailedStatsWithoutHistory()
    {
        var fixture = new Fixture();
        fixture.Backend.Status = AsrBackendStatus.Ready;

        await fixture.Orchestrator.ToggleAsync(CancellationToken.None);
        fixture.Backend.TranscribeThrows = new HttpRequestException("连接被拒绝");
        await fixture.Orchestrator.ToggleAsync(CancellationToken.None);

        Assert.Equal(DictationState.Error, fixture.LastStatus.State);
        Assert.Single(fixture.Stats.Recorded);
        Assert.False(fixture.Stats.Recorded[0].Succeeded);
        Assert.Empty(fixture.History.Entries);
    }

    [Fact]
    public async Task CancelRecordingAsync_StopsWithoutTranscribing()
    {
        var fixture = new Fixture();
        fixture.Backend.Status = AsrBackendStatus.Ready;

        await fixture.Orchestrator.ToggleAsync(CancellationToken.None);
        await fixture.Orchestrator.CancelRecordingAsync(CancellationToken.None);

        Assert.Equal(DictationState.Idle, fixture.LastStatus.State);
        Assert.Equal("已取消", fixture.LastStatus.Message);
        Assert.Equal(0, fixture.Backend.TranscribeCallCount);
    }

    [Fact]
    public async Task ToggleAsync_WhenRecordingTooShort_SkipsTranscription()
    {
        var fixture = new Fixture();
        fixture.Backend.Status = AsrBackendStatus.Ready;
        fixture.Recorder.DurationOverride = TimeSpan.FromMilliseconds(100);

        await fixture.Orchestrator.ToggleAsync(CancellationToken.None);
        await fixture.Orchestrator.ToggleAsync(CancellationToken.None);

        Assert.Equal(DictationState.Idle, fixture.LastStatus.State);
        Assert.Equal("已取消", fixture.LastStatus.Message);
        Assert.Equal(0, fixture.Backend.TranscribeCallCount);
    }

    [Fact]
    public async Task ToggleAsync_IgnoresPressWhileTranscribing()
    {
        var fixture = new Fixture();
        fixture.Backend.Status = AsrBackendStatus.Ready;
        fixture.Backend.TranscribeDelay = TimeSpan.FromMilliseconds(200);

        await fixture.Orchestrator.ToggleAsync(CancellationToken.None);
        var stopTask = fixture.Orchestrator.ToggleAsync(CancellationToken.None);
        await fixture.Orchestrator.ToggleAsync(CancellationToken.None);
        await stopTask;

        Assert.Equal(1, fixture.Backend.TranscribeCallCount);
    }

    [Fact]
    public async Task ToggleAsync_UsesPreferredLanguageFromSettings()
    {
        var fixture = new Fixture();
        fixture.Backend.Status = AsrBackendStatus.Ready;
        fixture.Settings.Settings = fixture.Settings.Settings with
        {
            PreferredTranscriptionLanguageId = "zh-Hans"
        };

        await fixture.Orchestrator.ToggleAsync(CancellationToken.None);
        await fixture.Orchestrator.ToggleAsync(CancellationToken.None);

        Assert.Equal("zh", fixture.Backend.LastRequest?.Language);
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

}
