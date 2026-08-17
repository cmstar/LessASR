using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Asr;
using LocalAsrClient.Core.Persistence;
using LocalAsrClient.Core.Text;
using LocalAsrClient.Core.Utilities;

namespace LocalAsrClient.Core.Dictation;

public sealed class InPlaceDictationOrchestrator : IDisposable
{
    private readonly ContinuousDictationSession _session;
    private readonly IAsrBackend _asrBackend;
    private readonly ITextInjector _textInjector;
    private readonly ITextHistoryRepository _historyRepository;
    private readonly ISettingsStore _settingsStore;
    private readonly IClock _clock;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private ContinuousDictationSnapshot _sessionSnapshot = new([], false, 0, 0, null);
    private InPlaceDictationState _state = InPlaceDictationState.Idle;
    private bool _hasSegmented;
    private string _message = "空闲";
    private CancellationTokenSource? _finishingCts;

    public InPlaceDictationOrchestrator(
        ContinuousDictationSession session,
        IAsrBackend asrBackend,
        ITextInjector textInjector,
        ITextHistoryRepository historyRepository,
        ISettingsStore settingsStore,
        IClock clock)
    {
        _session = session;
        _asrBackend = asrBackend;
        _textInjector = textInjector;
        _historyRepository = historyRepository;
        _settingsStore = settingsStore;
        _clock = clock;
        _session.Changed += OnSessionChanged;
    }

    public event Action<InPlaceDictationStatus>? StatusChanged;

    public InPlaceDictationState State => _state;

    public bool IsBusy => _state is InPlaceDictationState.EnsuringModelReady
        or InPlaceDictationState.Recording
        or InPlaceDictationState.Finishing
        or InPlaceDictationState.Injecting
        || _session.IsBusy;

    public bool IsSessionOpen => _state != InPlaceDictationState.Idle;

    public async Task ToggleAsync(CancellationToken cancellationToken)
    {
        if (!await _operationLock.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            if (_state == InPlaceDictationState.Idle)
            {
                await StartAsync(cancellationToken);
            }
            else if (_state == InPlaceDictationState.Recording)
            {
                await FinishAsync(commitCurrentSegment: true, cancellationToken);
            }
            else if (_state == InPlaceDictationState.Reviewing)
            {
                await FinishAsync(commitCurrentSegment: false, cancellationToken);
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task CommitSegmentBoundaryAsync(CancellationToken cancellationToken)
    {
        if (!await _operationLock.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            if (_state != InPlaceDictationState.Recording)
            {
                return;
            }

            _hasSegmented = true;
            await _session.CommitSegmentBoundaryAsync(cancellationToken);
            if (_session.IsRecordingActive)
            {
                Publish("聆听中");
            }
            else
            {
                _state = InPlaceDictationState.Reviewing;
                Publish(_sessionSnapshot.BannerMessage ?? "已停止录音，请检查识别内容");
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task CancelOrDismissAsync(CancellationToken cancellationToken)
    {
        if (_state == InPlaceDictationState.Finishing)
        {
            _finishingCts?.Cancel();
            return;
        }

        if (!await _operationLock.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            if (_state == InPlaceDictationState.Recording)
            {
                await _session.CancelCurrentSegmentAsync(cancellationToken);
                _state = InPlaceDictationState.Reviewing;
                Publish("检查已识别内容，再按一次 Esc 取消");
                return;
            }

            if (_state is InPlaceDictationState.Reviewing
                or InPlaceDictationState.ResultNeedsAction
                or InPlaceDictationState.Error)
            {
                await _session.TerminateAsync(cancellationToken);
                _state = InPlaceDictationState.Idle;
                _hasSegmented = false;
                Publish("已取消");
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public void UpdateSegmentText(Guid segmentId, string text)
    {
        if (_state != InPlaceDictationState.Reviewing)
        {
            return;
        }

        _session.UpdateSegmentText(segmentId, text);
        Publish("检查已识别内容，再按右 Alt 完成");
    }

    public void Dispose()
    {
        _finishingCts?.Cancel();
        _finishingCts?.Dispose();
        _session.Changed -= OnSessionChanged;
        _operationLock.Dispose();
    }

    private async Task StartAsync(CancellationToken cancellationToken)
    {
        _hasSegmented = false;
        _sessionSnapshot = new ContinuousDictationSnapshot([], false, 0, 0, null);
        if (_asrBackend.Status != AsrBackendStatus.Ready)
        {
            _state = InPlaceDictationState.EnsuringModelReady;
            Publish("模型加载中...");
            try
            {
                await _asrBackend.EnsureReadyAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _state = InPlaceDictationState.Error;
                Publish("模型加载失败", errorMessage: ex.Message);
                return;
            }
        }

        await _session.ToggleRecordingAsync(cancellationToken);
        _state = InPlaceDictationState.Recording;
        Publish("聆听中");
    }

    private async Task FinishAsync(bool commitCurrentSegment, CancellationToken cancellationToken)
    {
        using var finishingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _finishingCts = finishingCts;
        var finishingToken = finishingCts.Token;
        _state = InPlaceDictationState.Finishing;
        Publish("正在完成听写...");

        try
        {
            if (commitCurrentSegment)
            {
                await _session.ToggleRecordingAsync(finishingToken);
            }

            await _session.WaitForPendingTranscriptionsAsync(finishingToken);
            var history = _session.BuildHistory();
            if (string.IsNullOrWhiteSpace(history.Text))
            {
                _state = InPlaceDictationState.Reviewing;
                Publish("没有可写入的识别结果");
                return;
            }

            _state = InPlaceDictationState.Injecting;
            Publish("正在写入...");
            var injectionResult = await _textInjector.TryInjectAsync(history.Text, finishingToken);
            await PersistHistoryAsync(history, finishingToken);
            if (!injectionResult.Succeeded)
            {
                _state = InPlaceDictationState.ResultNeedsAction;
                Publish(injectionResult.Message ?? "无法写入当前光标位置", history.Text);
                return;
            }

            await _session.TerminateAsync(finishingToken);
            _state = InPlaceDictationState.Idle;
            _hasSegmented = false;
            Publish("已写入");
        }
        catch (OperationCanceledException) when (finishingCts.IsCancellationRequested)
        {
            await _session.TerminateAsync(CancellationToken.None);
            _state = InPlaceDictationState.Idle;
            _hasSegmented = false;
            Publish("已取消");
        }
        catch (Exception ex)
        {
            _state = InPlaceDictationState.Error;
            Publish("完成听写失败", errorMessage: ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_finishingCts, finishingCts))
            {
                _finishingCts = null;
            }
        }
    }

    private async Task PersistHistoryAsync(
        ContinuousDictationHistory history,
        CancellationToken cancellationToken)
    {
        var settings = await _settingsStore.LoadAsync(cancellationToken);
        if (settings.TranscriptRetentionPolicy == TranscriptRetentionPolicy.Disabled)
        {
            return;
        }

        await _historyRepository.AddAsync(new TextHistoryEntry(
            Guid.NewGuid(),
            _clock.Now,
            history.Text,
            TextMetrics.CountCharacters(history.Text),
            TextMetrics.CountWords(history.Text),
            TimeSpan.Zero,
            TimeSpan.Zero,
            history.BackendId,
            history.ModelId), cancellationToken);
        await _historyRepository.PruneAsync(
            _clock.Now,
            settings.TranscriptRetentionPolicy,
            cancellationToken);
    }

    private void OnSessionChanged(ContinuousDictationSnapshot snapshot)
    {
        _sessionSnapshot = snapshot;
        if (_state != InPlaceDictationState.Idle)
        {
            Publish(_message);
        }
    }

    private void Publish(string message, string? resultText = null, string? errorMessage = null)
    {
        _message = message;
        StatusChanged?.Invoke(new InPlaceDictationStatus(
            _state,
            _sessionSnapshot.Segments,
            _sessionSnapshot.IsRecordingActive,
            _hasSegmented,
            message,
            resultText,
            errorMessage));
    }
}
