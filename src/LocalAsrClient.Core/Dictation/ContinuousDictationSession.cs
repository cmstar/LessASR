using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Asr;

namespace LocalAsrClient.Core.Dictation;

public sealed class ContinuousDictationSession
{
    public const int MaxQueueDepth = 50;
    private static readonly TimeSpan MinRecordingDuration = TimeSpan.FromMilliseconds(300);

    private readonly List<ContinuousDictationSegment> _segments = new();
    private readonly IAudioRecorder _recorder;
    private readonly TranscriptionPipeline _pipeline;
    private readonly AsrActivityGate _activityGate;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _queueLock = new();
    private readonly Queue<(Guid SegmentId, RecordingResult Recording)> _pendingTranscriptions = new();
    private CancellationTokenSource? _workerCts;
    private Task? _workerTask;
    private TaskCompletionSource<bool>? _drainCompletion;
    private volatile bool _isRecordingActive;
    private int _transcribingCount;
    private AsrActivityLease? _activityLease;

    public event Action<ContinuousDictationSnapshot>? Changed;

    public bool IsRecordingActive => _isRecordingActive;
    public bool IsBusy => _isRecordingActive || Volatile.Read(ref _transcribingCount) > 0;

    public ContinuousDictationSession(
        IAudioRecorder recorder,
        TranscriptionPipeline pipeline,
        AsrActivityGate? activityGate = null)
    {
        _recorder = recorder;
        _pipeline = pipeline;
        _activityGate = activityGate ?? new AsrActivityGate();
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
                await ReleaseActivityLeaseIfIdleAsync();
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
            await ReleaseActivityLeaseIfIdleAsync();
            Publish();
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task WaitForPendingTranscriptionsAsync(CancellationToken cancellationToken)
    {
        Task drainTask;
        lock (_queueLock)
        {
            drainTask = _transcribingCount == 0
                ? Task.CompletedTask
                : _drainCompletion?.Task ?? Task.CompletedTask;
        }

        return drainTask.WaitAsync(cancellationToken);
    }

    public async Task TerminateAsync(CancellationToken cancellationToken)
    {
        var workerCts = _workerCts;
        var workerTask = _workerTask;
        workerCts?.Cancel();
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

        if (workerTask is not null)
        {
            try
            {
                await workerTask;
            }
            catch (OperationCanceledException)
            {
                // 正常的会话终止。
            }
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _segments.Clear();
            lock (_queueLock)
            {
                _pendingTranscriptions.Clear();
                _transcribingCount = 0;
                _drainCompletion?.TrySetResult(true);
            }

            _isRecordingActive = false;
            _workerCts = null;
            _workerTask = null;
            workerCts?.Dispose();
            await ReleaseActivityLeaseIfIdleAsync();
            Publish();
        }
        finally
        {
            _gate.Release();
        }
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

    public string BuildHistoryText() => BuildHistory().Text;

    public ContinuousDictationHistory BuildHistory()
    {
        _gate.Wait();
        try
        {
            var completed = _segments
                .Where(segment => segment.State == ContinuousSegmentState.Completed)
                .ToArray();
            var sources = completed
                .Where(segment => !string.IsNullOrWhiteSpace(segment.BackendId)
                    && !string.IsNullOrWhiteSpace(segment.ModelId))
                .Select(segment => (segment.BackendId!, segment.ModelId!))
                .Distinct()
                .ToArray();
            var source = sources.Length == 1
                ? sources[0]
                : ("多个服务", "mixed");
            return new ContinuousDictationHistory(
                ContinuousDictationTextMerge.MergeCompletedSegments(completed),
                source.Item1,
                source.Item2);
        }
        finally
        {
            _gate.Release();
        }
    }

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
        if (_activityLease is null)
        {
            _activityLease = await _activityGate.TryEnterAsync(cancellationToken);
            if (_activityLease is null)
            {
                throw new InvalidOperationException("服务配置更新中，请稍后再试。");
            }
        }

        try
        {
            _segments.Add(new ContinuousDictationSegment(Guid.NewGuid(), ContinuousSegmentState.WaitingInput, "", null));
            _isRecordingActive = true;
            await _recorder.StartAsync(cancellationToken);
        }
        catch
        {
            _isRecordingActive = false;
            await ReleaseActivityLeaseIfIdleAsync();
            throw;
        }
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

    private int GetPendingTranscriptionCount() => Volatile.Read(ref _transcribingCount);

    private void EnqueueTranscription(Guid segmentId, RecordingResult recording)
    {
        lock (_queueLock)
        {
            if (_transcribingCount == 0)
            {
                _drainCompletion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }

            _transcribingCount++;
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

            try
            {
                var result = await _pipeline.TranscribeAsync(job.Value.Recording, cancellationToken);
                await _gate.WaitAsync(cancellationToken);
                try
                {
                    var index = _segments.FindIndex(s => s.Id == job.Value.SegmentId);
                    if (index >= 0)
                    {
                        _segments[index] = result.Succeeded
                            ? _segments[index] with
                            {
                                State = ContinuousSegmentState.Completed,
                                Text = result.Text,
                                BackendId = result.BackendId,
                                ModelId = result.ModelId
                            }
                            : _segments[index] with
                            {
                                State = ContinuousSegmentState.Failed,
                                ErrorMessage = result.ErrorMessage
                            };
                    }

                }
                finally
                {
                    _gate.Release();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                await _gate.WaitAsync(CancellationToken.None);
                try
                {
                    var index = _segments.FindIndex(s => s.Id == job.Value.SegmentId);
                    if (index >= 0)
                    {
                        _segments[index] = _segments[index] with
                        {
                            State = ContinuousSegmentState.Failed,
                            ErrorMessage = ex.Message
                        };
                    }
                }
                finally
                {
                    _gate.Release();
                }
            }
            finally
            {
                TaskCompletionSource<bool>? completedDrain = null;
                lock (_queueLock)
                {
                    _transcribingCount--;
                    if (_transcribingCount == 0)
                    {
                        completedDrain = _drainCompletion;
                    }
                }

                completedDrain?.TrySetResult(true);
                await _gate.WaitAsync(CancellationToken.None);
                try
                {
                    Publish();
                }
                finally
                {
                    _gate.Release();
                }

                await ReleaseActivityLeaseIfIdleAsync();
            }
        }
    }

    private async ValueTask ReleaseActivityLeaseIfIdleAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var lease = Interlocked.Exchange(ref _activityLease, null);
        if (lease is not null)
        {
            await lease.DisposeAsync();
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
