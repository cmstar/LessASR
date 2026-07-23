using System.Net.Http;
using LocalAsrClient.Core.Asr;

namespace LocalAsrClient.App.Asr;

public sealed class RefreshingWhisperServerClient : IWhisperServerClient, IDisposable
{
    private readonly object _sync = new();
    private HttpClient _httpClient;
    private WhisperServerClient _inner;

    public RefreshingWhisperServerClient(Uri baseUri)
    {
        _httpClient = WhisperTranscribeHttp.CreateClient(baseUri);
        _inner = new WhisperServerClient(_httpClient);
        BaseUri = baseUri;
    }

    public Uri BaseUri { get; private set; }

    public void Refresh(Uri baseUri)
    {
        lock (_sync)
        {
            var old = _httpClient;
            BaseUri = baseUri;
            _httpClient = WhisperTranscribeHttp.CreateClient(baseUri);
            _inner = new WhisperServerClient(_httpClient);
            old.Dispose();
        }
    }

    public Task<AsrResult> TranscribeAsync(
        InMemoryAudioInput audio,
        string? language,
        string? initialPrompt,
        CancellationToken cancellationToken)
    {
        WhisperServerClient inner;
        lock (_sync)
        {
            inner = _inner;
        }

        return inner.TranscribeAsync(audio, language, initialPrompt, cancellationToken);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _httpClient.Dispose();
        }
    }
}
