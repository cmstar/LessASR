using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Asr;
using LocalAsrClient.Core.Persistence;
using LocalAsrClient.Core.Text;
using LocalAsrClient.Core.Utilities;

namespace LocalAsrClient.Core.Dictation;

public sealed class DictationOrchestrator
{
    private readonly IAudioRecorder _recorder;
    private readonly IAsrBackend _asrBackend;
    private readonly ITextInjector _textInjector;
    private readonly IStatsRepository _statsRepository;
    private readonly ITextHistoryRepository _historyRepository;
    private readonly ISettingsStore _settingsStore;
    private readonly IVocabularyRepository _vocabularyRepository;
    private readonly IClock _clock;
    private readonly ITextPostProcessor _postProcessor;
    private readonly AsrActivityGate _activityGate;
    private static readonly TimeSpan MinRecordingDuration = TimeSpan.FromMilliseconds(300);
    private readonly SemaphoreSlim _toggleLock = new(1, 1);
    private AsrActivityLease? _activityLease;
    private DictationState _state = DictationState.Idle;

    public DictationState State => _state;

    public DictationOrchestrator(
        IAudioRecorder recorder,
        IAsrBackend asrBackend,
        ITextInjector textInjector,
        IStatsRepository statsRepository,
        ITextHistoryRepository historyRepository,
        ISettingsStore settingsStore,
        IVocabularyRepository vocabularyRepository,
        IClock clock)
        : this(
            recorder,
            asrBackend,
            textInjector,
            statsRepository,
            historyRepository,
            settingsStore,
            vocabularyRepository,
            clock,
            new NoOpTextPostProcessor())
    {
    }

    public DictationOrchestrator(
        IAudioRecorder recorder,
        IAsrBackend asrBackend,
        ITextInjector textInjector,
        IStatsRepository statsRepository,
        ITextHistoryRepository historyRepository,
        ISettingsStore settingsStore,
        IVocabularyRepository vocabularyRepository,
        IClock clock,
        ITextPostProcessor postProcessor,
        AsrActivityGate? activityGate = null)
    {
        _recorder = recorder;
        _asrBackend = asrBackend;
        _textInjector = textInjector;
        _statsRepository = statsRepository;
        _historyRepository = historyRepository;
        _settingsStore = settingsStore;
        _vocabularyRepository = vocabularyRepository;
        _clock = clock;
        _postProcessor = postProcessor;
        _activityGate = activityGate ?? new AsrActivityGate();
    }

    public event Action<DictationStatus>? StatusChanged;

    public void DismissOverlay()
    {
        if (_state is not (DictationState.Error or DictationState.ResultNeedsAction or DictationState.Ready))
        {
            return;
        }

        _state = DictationState.Idle;
    }

    public async Task CancelRecordingAsync(CancellationToken cancellationToken)
    {
        if (!await _toggleLock.WaitAsync(0, cancellationToken))
        {
            return;
        }

        var releaseActivity = false;
        try
        {
            if (_state != DictationState.Recording)
            {
                return;
            }

            releaseActivity = true;
            await _recorder.StopAsync(cancellationToken);
            _state = DictationState.Idle;
            Publish("已取消");
        }
        finally
        {
            if (releaseActivity)
            {
                await ReleaseActivityLeaseAsync();
            }

            _toggleLock.Release();
        }
    }

    public async Task ToggleAsync(CancellationToken cancellationToken)
    {
        if (!await _toggleLock.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            if (_state is DictationState.Transcribing or DictationState.Injecting or DictationState.EnsuringModelReady)
            {
                return;
            }

            if (_state is DictationState.Idle or DictationState.Ready or DictationState.Error or DictationState.ResultNeedsAction)
            {
                await StartRecordingAsync(cancellationToken);
                return;
            }

            if (_state == DictationState.Recording)
            {
                await StopAndTranscribeAsync(cancellationToken);
            }
        }
        finally
        {
            _toggleLock.Release();
        }
    }

    private async Task StartRecordingAsync(CancellationToken cancellationToken)
    {
        _activityLease = await _activityGate.TryEnterAsync(cancellationToken);
        if (_activityLease is null)
        {
            _state = DictationState.Error;
            Publish("服务配置更新中，请稍后再试");
            return;
        }

        if (_asrBackend.Status != AsrBackendStatus.Ready)
        {
            _state = DictationState.EnsuringModelReady;
            Publish("模型加载中...");
            try
            {
                await _asrBackend.EnsureReadyAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _state = DictationState.Error;
                Publish("模型加载失败", ErrorMessage: ex.Message);
                await ReleaseActivityLeaseAsync();
                return;
            }
        }

        _state = DictationState.Recording;
        Publish("聆听中");
        try
        {
            await _recorder.StartAsync(cancellationToken);
        }
        catch
        {
            await ReleaseActivityLeaseAsync();
            throw;
        }
    }

    private async Task StopAndTranscribeAsync(CancellationToken cancellationToken)
    {
        try
        {
            _state = DictationState.Transcribing;
            Publish("识别中");
            var recording = await _recorder.StopAsync(cancellationToken);
            if (ShouldSkipTranscription(recording))
            {
                _state = DictationState.Idle;
                Publish("已取消");
                return;
            }

            var settings = await _settingsStore.LoadAsync(cancellationToken);
            var language = TranscriptionLanguageCatalog.ResolveLanguage(settings.PreferredTranscriptionLanguageId);
            var activeVocabulary = await _vocabularyRepository.GetActiveAsync(cancellationToken);
            var initialPrompt = WhisperVocabulary.CreateInitialPrompt(activeVocabulary?.EntriesText);
            var backendId = _asrBackend.Name;
            var modelId = _asrBackend.ModelId;

            var asrResult = await _asrBackend.TranscribeAsync(new AsrRequest(
                new InMemoryAudioInput(recording.WavData, "wav", recording.SampleRate, recording.Channels),
                Language: language,
                Options: new Dictionary<string, string>(),
                InitialPrompt: initialPrompt), cancellationToken);

            var finalText = await _postProcessor.ProcessAsync(asrResult.Text, cancellationToken);

            if (string.IsNullOrWhiteSpace(finalText))
            {
                await PersistResultAsync(
                    string.Empty,
                    recording.Duration,
                    asrResult.ProcessingDuration ?? TimeSpan.Zero,
                    succeeded: false,
                    cancellationToken,
                    backendId,
                    modelId);
                _state = DictationState.ResultNeedsAction;
                Publish("识别文本为空");
                return;
            }

            _state = DictationState.Injecting;
            var injection = await _textInjector.TryInjectAsync(finalText, cancellationToken);

            await PersistResultAsync(
                finalText,
                recording.Duration,
                asrResult.ProcessingDuration ?? TimeSpan.Zero,
                injection.Succeeded,
                cancellationToken,
                backendId,
                modelId);

            if (injection.Succeeded)
            {
                _state = DictationState.Idle;
                Publish("已输入");
                return;
            }

            _state = DictationState.ResultNeedsAction;
            var message = injection.Status == TextInjectionStatus.NoEditableTarget
                ? "未找到可输入位置"
                : injection.Message ?? "文本注入失败";
            Publish(message, finalText);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await PersistResultAsync(string.Empty, TimeSpan.Zero, TimeSpan.Zero, succeeded: false, cancellationToken);
            }
            catch
            {
                // 统计写入失败不再掩盖原始异常。
            }

            _state = DictationState.Error;
            Publish("输入失败", ErrorMessage: "语音识别超时，请稍后重试或重启 whisper-server。");
        }
        catch (Exception ex)
        {
            try
            {
                await PersistResultAsync(string.Empty, TimeSpan.Zero, TimeSpan.Zero, succeeded: false, cancellationToken);
            }
            catch
            {
                // 统计写入失败不再掩盖原始异常。
            }

            _state = DictationState.Error;
            Publish("输入失败", ErrorMessage: ex.Message);
        }
        finally
        {
            await ReleaseActivityLeaseAsync();
        }
    }

    private async ValueTask ReleaseActivityLeaseAsync()
    {
        var lease = _activityLease;
        _activityLease = null;
        if (lease is not null)
        {
            await lease.DisposeAsync();
        }
    }

    private async Task PersistResultAsync(
        string text,
        TimeSpan recordingDuration,
        TimeSpan processingDuration,
        bool succeeded,
        CancellationToken cancellationToken,
        string? backendId = null,
        string? modelId = null)
    {
        var characterCount = TextMetrics.CountCharacters(text);
        var wordCount = TextMetrics.CountWords(text);

        await _statsRepository.RecordAsync(new DailyStatsDelta(
            _clock.Today,
            backendId ?? _asrBackend.Name,
            succeeded,
            recordingDuration,
            processingDuration,
            characterCount,
            wordCount), cancellationToken);

        var settings = await _settingsStore.LoadAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(text) && settings.TranscriptRetentionPolicy != TranscriptRetentionPolicy.Disabled)
        {
            await _historyRepository.AddAsync(new TextHistoryEntry(
                Guid.NewGuid(),
                _clock.Now,
                text,
                characterCount,
                wordCount,
                recordingDuration,
                processingDuration,
                backendId ?? _asrBackend.Name,
                modelId ?? _asrBackend.ModelId), cancellationToken);
            await _historyRepository.PruneAsync(_clock.Now, settings.TranscriptRetentionPolicy, cancellationToken);
        }

        await _statsRepository.PruneAsync(_clock.Today, cancellationToken);
    }

    private static bool ShouldSkipTranscription(RecordingResult recording)
    {
        return recording.Duration < MinRecordingDuration;
    }

    private void Publish(string message, string? resultText = null, string? ErrorMessage = null)
    {
        StatusChanged?.Invoke(new DictationStatus(_state, message, resultText, ErrorMessage));
    }
}
