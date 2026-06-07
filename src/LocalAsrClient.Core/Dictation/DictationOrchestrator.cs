using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Asr;
using LocalAsrClient.Core.Persistence;
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
    private readonly IClock _clock;
    private readonly ITextPostProcessor _postProcessor;
    private DictationState _state = DictationState.Idle;

    public DictationOrchestrator(
        IAudioRecorder recorder,
        IAsrBackend asrBackend,
        ITextInjector textInjector,
        IStatsRepository statsRepository,
        ITextHistoryRepository historyRepository,
        ISettingsStore settingsStore,
        IClock clock)
        : this(recorder, asrBackend, textInjector, statsRepository, historyRepository, settingsStore, clock, new NoOpTextPostProcessor())
    {
    }

    public DictationOrchestrator(
        IAudioRecorder recorder,
        IAsrBackend asrBackend,
        ITextInjector textInjector,
        IStatsRepository statsRepository,
        ITextHistoryRepository historyRepository,
        ISettingsStore settingsStore,
        IClock clock,
        ITextPostProcessor postProcessor)
    {
        _recorder = recorder;
        _asrBackend = asrBackend;
        _textInjector = textInjector;
        _statsRepository = statsRepository;
        _historyRepository = historyRepository;
        _settingsStore = settingsStore;
        _clock = clock;
        _postProcessor = postProcessor;
    }

    public event Action<DictationStatus>? StatusChanged;

    public async Task ToggleAsync(CancellationToken cancellationToken)
    {
        if (_state == DictationState.Idle || _state == DictationState.Ready)
        {
            await StartRecordingAsync(cancellationToken);
            return;
        }

        if (_state == DictationState.Recording)
        {
            await StopAndTranscribeAsync(cancellationToken);
        }
    }

    private async Task StartRecordingAsync(CancellationToken cancellationToken)
    {
        if (_asrBackend.Status != AsrBackendStatus.Ready)
        {
            _state = DictationState.EnsuringModelReady;
            Publish("模型加载中");
            await _asrBackend.EnsureReadyAsync(cancellationToken);
            _state = DictationState.Ready;
            Publish("可录音");
            return;
        }

        _state = DictationState.Recording;
        Publish("正在聆听");
        await _recorder.StartAsync(cancellationToken);
    }

    private async Task StopAndTranscribeAsync(CancellationToken cancellationToken)
    {
        try
        {
            _state = DictationState.Transcribing;
            Publish("识别中");
            var recording = await _recorder.StopAsync(cancellationToken);
            var asrResult = await _asrBackend.TranscribeAsync(new AsrRequest(
                new InMemoryAudioInput(recording.WavData, "wav", recording.SampleRate, recording.Channels),
                Language: "zh",
                Prompt: null,
                Options: new Dictionary<string, string>()), cancellationToken);

            var finalText = await _postProcessor.ProcessAsync(asrResult.Text, cancellationToken);

            _state = DictationState.Injecting;
            Publish("正在输入", finalText);
            var injection = await _textInjector.TryInjectAsync(finalText, cancellationToken);

            await PersistResultAsync(finalText, recording.Duration, asrResult.ProcessingDuration ?? TimeSpan.Zero, injection.Succeeded, cancellationToken);

            if (injection.Succeeded)
            {
                _state = DictationState.Idle;
                Publish("已输入");
                return;
            }

            _state = DictationState.ResultNeedsAction;
            Publish("未找到可输入位置", finalText);
        }
        catch (Exception ex)
        {
            _state = DictationState.Error;
            Publish("输入失败", ErrorMessage: ex.Message);
        }
    }

    private async Task PersistResultAsync(
        string text,
        TimeSpan recordingDuration,
        TimeSpan processingDuration,
        bool succeeded,
        CancellationToken cancellationToken)
    {
        var characterCount = TextMetrics.CountCharacters(text);
        var wordCount = TextMetrics.CountWords(text);

        await _statsRepository.RecordAsync(new DailyStatsDelta(
            _clock.Today,
            succeeded,
            recordingDuration,
            processingDuration,
            characterCount,
            wordCount), cancellationToken);

        var settings = await _settingsStore.LoadAsync(cancellationToken);
        if (settings.TranscriptRetentionPolicy != TranscriptRetentionPolicy.Disabled)
        {
            await _historyRepository.AddAsync(new TextHistoryEntry(
                Guid.NewGuid(),
                _clock.Now,
                text,
                characterCount,
                wordCount,
                recordingDuration,
                processingDuration,
                "whisper-server",
                Path.GetFileNameWithoutExtension(settings.ModelPath)), cancellationToken);
            await _historyRepository.PruneAsync(_clock.Now, settings.TranscriptRetentionPolicy, cancellationToken);
        }

        await _statsRepository.PruneAsync(_clock.Today, cancellationToken);
    }

    private void Publish(string message, string? resultText = null, string? ErrorMessage = null)
    {
        StatusChanged?.Invoke(new DictationStatus(_state, message, resultText, ErrorMessage));
    }
}
