using System.Net.Http;

namespace LocalAsrClient.App.Asr;

internal static class WhisperTranscribeHttp
{
    public static readonly TimeSpan PooledConnectionLifetime = TimeSpan.FromMinutes(2);

    public static HttpClient CreateClient(Uri baseUri)
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = PooledConnectionLifetime,
            MaxConnectionsPerServer = 1,
        };

        return new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = baseUri,
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }
}
