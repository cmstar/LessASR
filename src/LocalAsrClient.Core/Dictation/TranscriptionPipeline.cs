using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Asr;
using LocalAsrClient.Core.Persistence;
using LocalAsrClient.Core.Text;
using LocalAsrClient.Core.Utilities;

namespace LocalAsrClient.Core.Dictation;

public sealed class TranscriptionPipeline
{
    private readonly IAsrBackend _asrBackend;
    private readonly ISettingsStore _settingsStore;
    private readonly IVocabularyRepository _vocabularyRepository;
    private readonly ITextPostProcessor _postProcessor;
    private readonly IStatsRepository _statsRepository;
    private readonly IClock _clock;

    public TranscriptionPipeline(
        IAsrBackend asrBackend,
        ISettingsStore settingsStore,
        IVocabularyRepository vocabularyRepository,
        ITextPostProcessor postProcessor,
        IStatsRepository statsRepository,
        IClock clock)
    {
        _asrBackend = asrBackend;
        _settingsStore = settingsStore;
        _vocabularyRepository = vocabularyRepository;
        _postProcessor = postProcessor;
        _statsRepository = statsRepository;
        _clock = clock;
    }

    public async Task<TranscriptionPipelineResult> TranscribeAsync(
        RecordingResult recording,
        CancellationToken cancellationToken)
    {
        var backendId = _asrBackend.Name;
        var modelId = _asrBackend.ModelId;
        try
        {
            var settings = await _settingsStore.LoadAsync(cancellationToken);
            var language = TranscriptionLanguageCatalog.ResolveLanguage(settings.PreferredTranscriptionLanguageId);
            var activeVocabulary = await _vocabularyRepository.GetActiveAsync(cancellationToken);
            var initialPrompt = WhisperVocabulary.CreateInitialPrompt(activeVocabulary?.EntriesText);
            var asrResult = await _asrBackend.TranscribeAsync(
                new AsrRequest(
                    new InMemoryAudioInput(recording.WavData, "wav", recording.SampleRate, recording.Channels),
                    Language: language,
                    Options: new Dictionary<string, string>(),
                    InitialPrompt: initialPrompt),
                cancellationToken);

            var finalText = await _postProcessor.ProcessAsync(asrResult.Text, cancellationToken);
            var processingDuration = asrResult.ProcessingDuration ?? TimeSpan.Zero;
            var succeeded = !string.IsNullOrWhiteSpace(finalText);

            await RecordStatsAsync(finalText, recording.Duration, processingDuration, succeeded, cancellationToken);

            return succeeded
                ? new TranscriptionPipelineResult(
                    true, finalText, null, recording.Duration, processingDuration, backendId, modelId)
                : new TranscriptionPipelineResult(
                    false, string.Empty, "识别文本为空", recording.Duration, processingDuration, backendId, modelId);
        }
        catch (Exception ex)
        {
            await RecordStatsAsync(string.Empty, recording.Duration, TimeSpan.Zero, succeeded: false, cancellationToken);
            return new TranscriptionPipelineResult(
                false, string.Empty, ex.Message, recording.Duration, TimeSpan.Zero, backendId, modelId);
        }
    }

    private async Task RecordStatsAsync(
        string text,
        TimeSpan recordingDuration,
        TimeSpan processingDuration,
        bool succeeded,
        CancellationToken cancellationToken)
    {
        await _statsRepository.RecordAsync(
            new DailyStatsDelta(
                _clock.Today,
                succeeded,
                recordingDuration,
                processingDuration,
                TextMetrics.CountCharacters(text),
                TextMetrics.CountWords(text)),
            cancellationToken);
    }
}
