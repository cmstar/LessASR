using LocalAsrClient.Core.Abstractions;

namespace LocalAsrClient.Core.Dictation;

public sealed class ContinuousDictationSession
{
    public const int MaxQueueDepth = 50;
    private static readonly TimeSpan MinRecordingDuration = TimeSpan.FromMilliseconds(300);

    private readonly List<ContinuousDictationSegment> _segments = new();
    private readonly IAudioRecorder _recorder;
    private readonly TranscriptionPipeline _pipeline;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Queue<(Guid SegmentId, RecordingResult Recording)> _pendingTranscriptions = new();
    private bool _isRecordingActive;

    public event Action<ContinuousDictationSnapshot>? Changed;

    public bool IsRecordingActive => _isRecordingActive;

    public ContinuousDictationSession(IAudioRecorder recorder, TranscriptionPipeline pipeline)
    {
        _recorder = recorder;
        _pipeline = pipeline;
    }

    public async Task ToggleRecordingAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_isRecordingActive)
            {
                await StartRecordingInternalAsync(cancellationToken);
            }
            else
            {
                await CommitCurrentSegmentInternalAsync(startNext: false, cancellationToken);
                _isRecordingActive = false;
            }

            Publish();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task StartRecordingInternalAsync(CancellationToken cancellationToken)
    {
        _segments.Add(new ContinuousDictationSegment(Guid.NewGuid(), ContinuousSegmentState.WaitingInput, "", null));
        _isRecordingActive = true;
        await _recorder.StartAsync(cancellationToken);
    }

    private async Task<bool> CommitCurrentSegmentInternalAsync(bool startNext, CancellationToken cancellationToken)
    {
        var waitingIndex = _segments.FindLastIndex(s => s.State == ContinuousSegmentState.WaitingInput);
        if (waitingIndex < 0)
        {
            return false;
        }

        var recording = await _recorder.StopAsync(cancellationToken);
        if (recording.Duration < MinRecordingDuration)
        {
            _segments.RemoveAt(waitingIndex);
            return false;
        }

        var segmentId = _segments[waitingIndex].Id;
        _segments[waitingIndex] = _segments[waitingIndex] with { State = ContinuousSegmentState.Transcribing };
        _pendingTranscriptions.Enqueue((segmentId, recording));

        return false;
    }

    private void Publish(string? banner = null)
    {
        Changed?.Invoke(new ContinuousDictationSnapshot(
            _segments.ToList(),
            _isRecordingActive,
            _segments.Count(s => s.State == ContinuousSegmentState.Completed),
            _segments.Count,
            banner));
    }
}
