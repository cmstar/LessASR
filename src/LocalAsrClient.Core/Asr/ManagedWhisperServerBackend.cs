using LocalAsrClient.Core.Abstractions;

namespace LocalAsrClient.Core.Asr;

public sealed class ManagedWhisperServerBackend : IAsrBackend
{
    private readonly IWhisperServerManager _manager;
    private readonly IWhisperServerClient _client;

    public ManagedWhisperServerBackend(IWhisperServerManager manager, IWhisperServerClient client)
    {
        _manager = manager;
        _client = client;
    }

    public string Name => "Whisper Server";
    public AsrBackendStatus Status => _manager.Status switch
    {
        WhisperServerStatus.Stopped => AsrBackendStatus.Stopped,
        WhisperServerStatus.Starting => AsrBackendStatus.Starting,
        WhisperServerStatus.Ready => AsrBackendStatus.Ready,
        WhisperServerStatus.Transcribing => AsrBackendStatus.Transcribing,
        WhisperServerStatus.Failed => AsrBackendStatus.Failed,
        _ => AsrBackendStatus.Failed
    };

    public Task EnsureReadyAsync(CancellationToken cancellationToken)
    {
        return _manager.EnsureStartedAsync(cancellationToken);
    }

    public async Task<AsrResult> TranscribeAsync(AsrRequest request, CancellationToken cancellationToken)
    {
        if (request.Audio is not InMemoryAudioInput audio)
        {
            throw new NotSupportedException("MVP 仅支持内存音频输入。");
        }

        await EnsureReadyAsync(cancellationToken);
        return await _client.TranscribeAsync(audio, request.Language, cancellationToken);
    }
}
