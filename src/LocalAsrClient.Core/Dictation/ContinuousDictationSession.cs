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
    private readonly object _queueLock = new();
    private readonly Queue<(Guid SegmentId, RecordingResult Recording)> _pendingTranscriptions = new();
    private CancellationTokenSource? _workerCts;
    private Task? _workerTask;
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

    public async Task CancelCurrentSegmentAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_isRecordingActive)
            {
                return;
            }

            await _recorder.StopAsync(cancellationToken);
            var waitingIndex = _segments.FindLastIndex(s => s.State == ContinuousSegmentState.WaitingInput);
            if (waitingIndex >= 0)
            {
                _segments.RemoveAt(waitingIndex);
            }

            _isRecordingActive = false;
            Publish();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task TerminateAsync(CancellationToken cancellationToken)
    {
        _workerCts?.Cancel();
        if (_isRecordingActive)
        {
            try
            {
                await _recorder.StopAsync(cancellationToken);
            }
            catch
            {
                // 忽略停止录音时的异常。
            }
        }

        _segments.Clear();
        lock (_queueLock)
        {
            _pendingTranscriptions.Clear();
        }

        _isRecordingActive = false;
        _workerCts = null;
        _workerTask = null;
        Publish();
    }

    public void UpdateSegmentText(Guid segmentId, string text)
    {
        var index = _segments.FindIndex(s => s.Id == segmentId);
        if (index >= 0 && _segments[index].State == ContinuousSegmentState.Completed)
        {
            _segments[index] = _segments[index] with { Text = text };
            Publish();
        }
    }

    public string BuildHistoryText() => ContinuousDictationTextMerge.MergeCompletedSegments(_segments);

    public async Task CommitSegmentBoundaryAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_isRecordingActive)
            {
                return;
            }

            var hasNext = await CommitCurrentSegmentInternalAsync(startNext: true, cancellationToken);
            if (!hasNext)
            {
                _isRecordingActive = false;
                var banner = GetPendingTranscriptionCount() >= MaxQueueDepth
                    ? "已达识别上限（50 段），已停止录制"
                    : null;
                Publish(banner);
                return;
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
            if (startNext)
            {
                await _recorder.StartAsync(cancellationToken);
                _segments.Add(new ContinuousDictationSegment(Guid.NewGuid(), ContinuousSegmentState.WaitingInput, "", null));
                return true;
            }

            return false;
        }

        var segmentId = _segments[waitingIndex].Id;
        _segments[waitingIndex] = _segments[waitingIndex] with { State = ContinuousSegmentState.Transcribing };
        EnqueueTranscription(segmentId, recording);

        if (startNext)
        {
            if (GetPendingTranscriptionCount() >= MaxQueueDepth)
            {
                return false;
            }

            await _recorder.StartAsync(cancellationToken);
            _segments.Add(new ContinuousDictationSegment(Guid.NewGuid(), ContinuousSegmentState.WaitingInput, "", null));
            return true;
        }

        return false;
    }

    private int GetPendingTranscriptionCount() =>
        _segments.Count(s => s.State == ContinuousSegmentState.Transcribing);

    private void EnqueueTranscription(Guid segmentId, RecordingResult recording)
    {
        lock (_queueLock)
        {
            _pendingTranscriptions.Enqueue((segmentId, recording));
        }

        EnsureWorkerRunning();
    }

    private void EnsureWorkerRunning()
    {
        _workerCts ??= new CancellationTokenSource();
        _workerTask ??= Task.Run(() => ProcessQueueAsync(_workerCts.Token));
    }

    private async Task ProcessQueueAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            (Guid SegmentId, RecordingResult Recording)? job = null;
            lock (_queueLock)
            {
                if (_pendingTranscriptions.Count > 0)
                {
                    job = _pendingTranscriptions.Dequeue();
                }
            }

            if (job is null)
            {
                try
                {
                    await Task.Delay(20, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                continue;
            }

            var result = await _pipeline.TranscribeAsync(job.Value.Recording, cancellationToken);
            await _gate.WaitAsync(cancellationToken);
            try
            {
                var index = _segments.FindIndex(s => s.Id == job.Value.SegmentId);
                if (index >= 0)
                {
                    _segments[index] = result.Succeeded
                        ? _segments[index] with { State = ContinuousSegmentState.Completed, Text = result.Text }
                        : _segments[index] with
                        {
                            State = ContinuousSegmentState.Failed,
                            ErrorMessage = result.ErrorMessage
                        };
                }

                Publish();
            }
            finally
            {
                _gate.Release();
            }
        }
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
