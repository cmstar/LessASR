using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Asr;
using LocalAsrClient.Core.Dictation;
using LocalAsrClient.Core.Persistence;
using LocalAsrClient.Core.Text;

namespace LocalAsrClient.Core.Tests.Dictation;

public sealed class InPlaceDictationOrchestratorTests
{
    [Fact]
    public async Task ToggleAsync_WhenIdle_StartsRecordingWithoutInjectingOrWritingHistory()
    {
        var fixture = new Fixture();

        await fixture.Orchestrator.ToggleAsync(CancellationToken.None);

        Assert.Equal(InPlaceDictationState.Recording, fixture.LastStatus.State);
        Assert.True(fixture.Session.IsRecordingActive);
        Assert.Equal(0, fixture.Injector.CallCount);
        Assert.Empty(fixture.History.Entries);
        Assert.Equal(ContinuousSegmentState.WaitingInput, Assert.Single(fixture.LastStatus.Segments).State);
    }

    [Fact]
    public async Task ToggleAsync_WhenRecorderCannotStart_PublishesDismissibleError()
    {
        var fixture = new Fixture();
        fixture.Recorder.StartThrows = new InvalidOperationException("找不到麦克风");

        await fixture.Orchestrator.ToggleAsync(CancellationToken.None);

        Assert.Equal(InPlaceDictationState.Error, fixture.LastStatus.State);
        Assert.Equal("找不到麦克风", fixture.LastStatus.ErrorMessage);
        Assert.True(fixture.Orchestrator.IsSessionOpen);
    }

    [Fact]
    public async Task CommitSegmentBoundaryAsync_WhileRecording_QueuesRecognitionAndImmediatelyRecordsNextSegment()
    {
        var fixture = new Fixture();
        fixture.Backend.TranscribeDelay = TimeSpan.FromSeconds(1);
        await fixture.Orchestrator.ToggleAsync(CancellationToken.None);

        await fixture.Orchestrator.CommitSegmentBoundaryAsync(CancellationToken.None);

        Assert.Equal(InPlaceDictationState.Recording, fixture.LastStatus.State);
        Assert.True(fixture.LastStatus.HasSegmented);
        Assert.True(fixture.Session.IsRecordingActive);
        Assert.Contains(fixture.LastStatus.Segments, segment => segment.State == ContinuousSegmentState.Transcribing);
        Assert.Equal(ContinuousSegmentState.WaitingInput, fixture.LastStatus.Segments[^1].State);
        Assert.Equal(0, fixture.Injector.CallCount);
        Assert.Empty(fixture.History.Entries);
    }

    [Fact]
    public async Task ToggleAsync_WhenRecording_DrainsSegmentsThenInjectsAndWritesOneHistoryEntry()
    {
        var fixture = new Fixture();
        await fixture.Orchestrator.ToggleAsync(CancellationToken.None);
        await fixture.Orchestrator.CommitSegmentBoundaryAsync(CancellationToken.None);

        await fixture.Orchestrator.ToggleAsync(CancellationToken.None);

        Assert.Equal(InPlaceDictationState.Idle, fixture.LastStatus.State);
        Assert.Equal(1, fixture.Injector.CallCount);
        Assert.Equal("测试文本\n测试文本", fixture.Injector.Text);
        var history = Assert.Single(fixture.History.Entries);
        Assert.Equal(fixture.Injector.Text, history.Text);
    }

    [Fact]
    public async Task CancelOrDismissAsync_UsesRecordingThenReviewingTwoStageCancellation()
    {
        var fixture = new Fixture();
        await fixture.Orchestrator.ToggleAsync(CancellationToken.None);
        await fixture.Orchestrator.CommitSegmentBoundaryAsync(CancellationToken.None);
        await fixture.Session.WaitForPendingTranscriptionsAsync(CancellationToken.None);

        await fixture.Orchestrator.CancelOrDismissAsync(CancellationToken.None);

        Assert.Equal(InPlaceDictationState.Reviewing, fixture.LastStatus.State);
        Assert.False(fixture.Session.IsRecordingActive);
        Assert.Single(fixture.LastStatus.Segments);
        Assert.Equal(0, fixture.Injector.CallCount);
        Assert.Empty(fixture.History.Entries);

        await fixture.Orchestrator.CancelOrDismissAsync(CancellationToken.None);

        Assert.Equal(InPlaceDictationState.Idle, fixture.LastStatus.State);
        Assert.Empty(fixture.LastStatus.Segments);
        Assert.Equal(0, fixture.Injector.CallCount);
        Assert.Empty(fixture.History.Entries);
    }

    [Fact]
    public async Task ToggleAsync_FromReviewing_InjectsEditedCompletedSegments()
    {
        var fixture = new Fixture();
        await fixture.Orchestrator.ToggleAsync(CancellationToken.None);
        await fixture.Orchestrator.CommitSegmentBoundaryAsync(CancellationToken.None);
        await fixture.Session.WaitForPendingTranscriptionsAsync(CancellationToken.None);
        await fixture.Orchestrator.CancelOrDismissAsync(CancellationToken.None);
        var completed = Assert.Single(
            fixture.LastStatus.Segments,
            segment => segment.State == ContinuousSegmentState.Completed);

        fixture.Orchestrator.UpdateSegmentText(completed.Id, "修订后的内容");
        await fixture.Orchestrator.ToggleAsync(CancellationToken.None);

        Assert.Equal(InPlaceDictationState.Idle, fixture.LastStatus.State);
        Assert.Equal("修订后的内容", fixture.Injector.Text);
        Assert.Equal("修订后的内容", Assert.Single(fixture.History.Entries).Text);
    }

    [Fact]
    public async Task RecognitionFailure_IsPublishedAsPlaceholderAndExcludedFromFinalText()
    {
        var fixture = new Fixture();
        fixture.Backend.TranscribeOutcomes.Enqueue(() => throw new InvalidOperationException("服务异常"));
        fixture.Backend.TranscribeOutcomes.Enqueue(() =>
            new AsrResult("保留内容", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), null));
        await fixture.Orchestrator.ToggleAsync(CancellationToken.None);
        await fixture.Orchestrator.CommitSegmentBoundaryAsync(CancellationToken.None);
        await fixture.Orchestrator.CommitSegmentBoundaryAsync(CancellationToken.None);

        await fixture.Session.WaitForPendingTranscriptionsAsync(CancellationToken.None);

        Assert.Contains(fixture.LastStatus.Segments, segment =>
            segment.State == ContinuousSegmentState.Failed && segment.ErrorMessage == "服务异常");
        Assert.Contains(fixture.LastStatus.Segments, segment =>
            segment.State == ContinuousSegmentState.Completed && segment.Text == "保留内容");

        fixture.Recorder.DurationOverride = TimeSpan.FromMilliseconds(100);
        await fixture.Orchestrator.ToggleAsync(CancellationToken.None);

        Assert.Equal("保留内容", fixture.Injector.Text);
        Assert.Equal("保留内容", Assert.Single(fixture.History.Entries).Text);
    }

    [Fact]
    public async Task ToggleAsync_WhenInjectionTargetIsUnavailable_KeepsCopyableResultAndOneHistoryEntry()
    {
        var fixture = new Fixture();
        fixture.Injector.Result = new(TextInjectionStatus.NoEditableTarget, "没有可写入的光标位置");
        await fixture.Orchestrator.ToggleAsync(CancellationToken.None);

        await fixture.Orchestrator.ToggleAsync(CancellationToken.None);

        Assert.Equal(InPlaceDictationState.ResultNeedsAction, fixture.LastStatus.State);
        Assert.Equal("测试文本", fixture.LastStatus.ResultText);
        Assert.Equal("测试文本", Assert.Single(fixture.History.Entries).Text);
        Assert.True(fixture.Orchestrator.IsSessionOpen);
    }

    [Fact]
    public async Task CancelOrDismissAsync_WhileFinishing_CancelsBeforeInjection()
    {
        var fixture = new Fixture();
        fixture.Backend.TranscribeDelay = TimeSpan.FromSeconds(1);
        await fixture.Orchestrator.ToggleAsync(CancellationToken.None);

        var finishing = fixture.Orchestrator.ToggleAsync(CancellationToken.None);
        Assert.Equal(InPlaceDictationState.Finishing, fixture.LastStatus.State);
        await fixture.Orchestrator.CancelOrDismissAsync(CancellationToken.None);
        await finishing;

        Assert.Equal(InPlaceDictationState.Idle, fixture.LastStatus.State);
        Assert.Equal(0, fixture.Injector.CallCount);
        Assert.Empty(fixture.History.Entries);
    }

    [Fact]
    public async Task CommitSegmentBoundaryAsync_WhenQueueLimitIsReached_StopsInReviewingState()
    {
        var fixture = new Fixture();
        fixture.Backend.TranscribeDelay = TimeSpan.FromSeconds(10);
        await fixture.Orchestrator.ToggleAsync(CancellationToken.None);

        try
        {
            for (var index = 0; index < ContinuousDictationSession.MaxQueueDepth; index++)
            {
                await fixture.Orchestrator.CommitSegmentBoundaryAsync(CancellationToken.None);
            }

            Assert.Equal(InPlaceDictationState.Reviewing, fixture.LastStatus.State);
            Assert.False(fixture.Session.IsRecordingActive);
            Assert.True(fixture.LastStatus.HasSegmented);
        }
        finally
        {
            await fixture.Session.TerminateAsync(CancellationToken.None);
        }
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Recorder = new StubRecorder();
            Backend = new StubBackend();
            Stats = new StubStatsRepository();
            Settings = new StubSettingsStore();
            History = new StubHistoryRepository();
            Injector = new StubInjector();
            var pipeline = new TranscriptionPipeline(
                Backend,
                Settings,
                new StubVocabularyRepository(),
                new NoOpTextPostProcessor(),
                Stats,
                new StubClock());
            Session = new ContinuousDictationSession(Recorder, pipeline);
            Orchestrator = new InPlaceDictationOrchestrator(
                Session,
                Backend,
                Injector,
                History,
                Settings,
                new StubClock());
            Orchestrator.StatusChanged += status => LastStatus = status;
        }

        public StubRecorder Recorder { get; }
        public StubBackend Backend { get; }
        public StubStatsRepository Stats { get; }
        public StubSettingsStore Settings { get; }
        public StubHistoryRepository History { get; }
        public StubInjector Injector { get; }
        public ContinuousDictationSession Session { get; }
        public InPlaceDictationOrchestrator Orchestrator { get; }
        public InPlaceDictationStatus LastStatus { get; private set; } = InPlaceDictationStatus.Idle;
    }

    private sealed class StubInjector : ITextInjector
    {
        public int CallCount { get; private set; }
        public string? Text { get; private set; }
        public TextInjectionResult Result { get; set; } = new(TextInjectionStatus.Success, null);

        public Task<TextInjectionResult> TryInjectAsync(string text, CancellationToken cancellationToken)
        {
            CallCount++;
            Text = text;
            return Task.FromResult(Result);
        }
    }

    private sealed class StubHistoryRepository : ITextHistoryRepository
    {
        public List<TextHistoryEntry> Entries { get; } = [];

        public Task AddAsync(TextHistoryEntry entry, CancellationToken cancellationToken)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TextHistoryEntry>> GetRecentAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TextHistoryEntry>>(Entries);

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
        {
            Entries.RemoveAll(entry => entry.Id == id);
            return Task.CompletedTask;
        }

        public Task<int> CountPrunableAsync(
            DateTimeOffset now,
            TranscriptRetentionPolicy policy,
            CancellationToken cancellationToken) => Task.FromResult(0);

        public Task PruneAsync(
            DateTimeOffset now,
            TranscriptRetentionPolicy policy,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
