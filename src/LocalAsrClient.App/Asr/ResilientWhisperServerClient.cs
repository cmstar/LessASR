using LocalAsrClient.Core.Asr;

namespace LocalAsrClient.App.Asr;

public sealed class ResilientWhisperServerClient : IWhisperServerClient, IDisposable
{
    private readonly RefreshingWhisperServerClient _client;
    private readonly IWhisperServerManager _manager;

    public ResilientWhisperServerClient(Uri baseUri, IWhisperServerManager manager)
    {
        _client = new RefreshingWhisperServerClient(baseUri);
        _manager = manager;
    }

    public Uri BaseUri => _client.BaseUri;

    public void Refresh(Uri baseUri)
    {
        _client.Refresh(baseUri);
    }

    public async Task<AsrResult> TranscribeAsync(
        InMemoryAudioInput audio,
        string? language,
        string? initialPrompt,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _client.TranscribeAsync(audio, language, initialPrompt, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await RecoverAfterTimeoutAsync(cancellationToken);
            return await _client.TranscribeAsync(audio, language, initialPrompt, cancellationToken);
        }
    }

    private async Task RecoverAfterTimeoutAsync(CancellationToken cancellationToken)
    {
        await _manager.StopAsync(cancellationToken);
        _client.Refresh(_manager.BaseUri);
        await _manager.EnsureStartedAsync(cancellationToken);
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
