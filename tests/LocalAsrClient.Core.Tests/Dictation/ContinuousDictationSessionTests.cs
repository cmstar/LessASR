using LocalAsrClient.Core.Asr;
using LocalAsrClient.Core.Dictation;

namespace LocalAsrClient.Core.Tests.Dictation;

public sealed class ContinuousDictationSessionTests
{
    [Fact]
    public async Task ToggleRecordingAsync_WhenInactive_StartsFirstWaitingSegment()
    {
        var fixture = new SessionFixture();
        fixture.Backend.Status = AsrBackendStatus.Ready;

        await fixture.Session.ToggleRecordingAsync(CancellationToken.None);

        var snap = fixture.LastSnapshot;
        Assert.True(snap.IsRecordingActive);
        Assert.Single(snap.Segments);
        Assert.Equal(ContinuousSegmentState.WaitingInput, snap.Segments[0].State);
        Assert.True(fixture.Recorder.Started);
    }

    [Fact]
    public async Task ToggleRecordingAsync_WhenActive_StopsRecordingAndQueuesSegment()
    {
        var fixture = new SessionFixture();
        fixture.Backend.Status = AsrBackendStatus.Ready;
        fixture.Backend.TranscribeDelay = TimeSpan.FromSeconds(10);
        await fixture.Session.ToggleRecordingAsync(CancellationToken.None);

        await fixture.Session.ToggleRecordingAsync(CancellationToken.None);

        Assert.False(fixture.LastSnapshot.IsRecordingActive);
        Assert.Equal(ContinuousSegmentState.Transcribing, fixture.LastSnapshot.Segments[0].State);
    }

    [Fact]
    public async Task CommitSegmentBoundaryAsync_EnqueuesAndStartsNextWaitingSegment()
    {
        var fixture = new SessionFixture();
        fixture.Backend.Status = AsrBackendStatus.Ready;
        fixture.Backend.TranscribeDelay = TimeSpan.FromSeconds(10);
        await fixture.Session.ToggleRecordingAsync(CancellationToken.None);

        await fixture.Session.CommitSegmentBoundaryAsync(CancellationToken.None);

        Assert.True(fixture.LastSnapshot.IsRecordingActive);
        Assert.Equal(2, fixture.LastSnapshot.Segments.Count);
        Assert.Equal(ContinuousSegmentState.Transcribing, fixture.LastSnapshot.Segments[0].State);
        Assert.Equal(ContinuousSegmentState.WaitingInput, fixture.LastSnapshot.Segments[1].State);
    }

    [Fact]
    public async Task CommitSegmentBoundaryAsync_WhenTooShort_RemovesWaitingSegment()
    {
        var fixture = new SessionFixture();
        fixture.Backend.Status = AsrBackendStatus.Ready;
        fixture.Recorder.DurationOverride = TimeSpan.FromMilliseconds(100);
        await fixture.Session.ToggleRecordingAsync(CancellationToken.None);

        await fixture.Session.CommitSegmentBoundaryAsync(CancellationToken.None);

        Assert.Single(fixture.LastSnapshot.Segments);
        Assert.Equal(ContinuousSegmentState.WaitingInput, fixture.LastSnapshot.Segments[0].State);
    }

    [Fact]
    public async Task QueueWorker_OnSuccess_MarksSegmentCompleted()
    {
        var fixture = new SessionFixture();
        fixture.Backend.Status = AsrBackendStatus.Ready;
        fixture.Backend.TranscribeText = "段落一";
        await fixture.Session.ToggleRecordingAsync(CancellationToken.None);
        await fixture.Session.ToggleRecordingAsync(CancellationToken.None);

        await fixture.WaitForQueueDrainAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(ContinuousSegmentState.Completed, fixture.LastSnapshot.Segments[0].State);
        Assert.Equal("段落一", fixture.LastSnapshot.Segments[0].Text);
    }

    [Fact]
    public async Task CancelCurrentSegmentAsync_RemovesWaitingInputWithoutQueueing()
    {
        var fixture = new SessionFixture();
        fixture.Backend.Status = AsrBackendStatus.Ready;
        await fixture.Session.ToggleRecordingAsync(CancellationToken.None);

        await fixture.Session.CancelCurrentSegmentAsync(CancellationToken.None);

        Assert.False(fixture.LastSnapshot.IsRecordingActive);
        Assert.Empty(fixture.LastSnapshot.Segments);
        Assert.Equal(0, fixture.Backend.TranscribeCallCount);
    }

    [Fact]
    public async Task TerminateAsync_ClearsSegmentsAndCancelsWorker()
    {
        var fixture = new SessionFixture();
        fixture.Backend.Status = AsrBackendStatus.Ready;
        await fixture.Session.ToggleRecordingAsync(CancellationToken.None);
        await fixture.Session.ToggleRecordingAsync(CancellationToken.None);

        await fixture.Session.TerminateAsync(CancellationToken.None);

        Assert.Empty(fixture.LastSnapshot.Segments);
    }

    [Fact]
    public async Task BuildHistoryText_UsesCompletedSegments()
    {
        var fixture = new SessionFixture();
        fixture.Backend.Status = AsrBackendStatus.Ready;
        fixture.Backend.TranscribeText = "原始文本";
        await fixture.Session.ToggleRecordingAsync(CancellationToken.None);
        await fixture.Session.ToggleRecordingAsync(CancellationToken.None);
        await fixture.WaitForQueueDrainAsync(TimeSpan.FromSeconds(2));

        var segmentId = fixture.LastSnapshot.Segments[0].Id;
        fixture.Session.UpdateSegmentText(segmentId, "用户改过的字");

        var text = fixture.Session.BuildHistoryText();
        Assert.Contains("用户改过的字", text);
    }

    [Fact]
    public async Task QueueWorker_OnFailure_MarksSegmentFailed()
    {
        var fixture = new SessionFixture();
        fixture.Backend.Status = AsrBackendStatus.Ready;
        fixture.Backend.TranscribeThrows = new InvalidOperationException("连接失败");
        await fixture.Session.ToggleRecordingAsync(CancellationToken.None);
        await fixture.Session.ToggleRecordingAsync(CancellationToken.None);

        await fixture.WaitForQueueDrainAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(ContinuousSegmentState.Failed, fixture.LastSnapshot.Segments[0].State);
        Assert.Contains("连接失败", fixture.LastSnapshot.Segments[0].ErrorMessage, StringComparison.Ordinal);
    }

    private sealed class SessionFixture
    {
        public SessionFixture()
        {
            Recorder = new StubRecorder();
            Backend = new StubBackend();
            Stats = new StubStatsRepository();
            Settings = new StubSettingsStore();
            Clock = new StubClock();
            Pipeline = new TranscriptionPipeline(
                Backend,
                Settings,
                new StubVocabularyRepository(),
                new NoOpTextPostProcessor(),
                Stats,
                Clock);
            Session = new ContinuousDictationSession(Recorder, Pipeline);
            Session.Changed += snapshot => LastSnapshot = snapshot;
        }

        public StubRecorder Recorder { get; }
        public StubBackend Backend { get; }
        public StubStatsRepository Stats { get; }
        public StubSettingsStore Settings { get; }
        public StubClock Clock { get; }
        public TranscriptionPipeline Pipeline { get; }
        public ContinuousDictationSession Session { get; }
        public ContinuousDictationSnapshot LastSnapshot { get; private set; } =
            new(Array.Empty<ContinuousDictationSegment>(), false, 0, 0, null);

        public async Task WaitForQueueDrainAsync(TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (!LastSnapshot.Segments.Any(s => s.State == ContinuousSegmentState.Transcribing))
                {
                    return;
                }

                await Task.Delay(20);
            }
        }
    }
}
