using System.Net;
using System.Net.Http;
using LocalAsrClient.Core.Asr;

namespace LocalAsrClient.App.Asr;

public sealed class RemoteHttpClientPool : IDisposable
{
    private readonly Dictionary<string, HttpClient> _clients = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();
    private bool _disposed;

    public HttpClient GetClient(string? proxyUrl)
    {
        var proxy = RemoteProxyPolicy.ParseOptionalAndValidate(proxyUrl);
        var key = proxy?.AbsoluteUri ?? string.Empty;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_clients.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var client = new HttpClient(CreateHandler(proxy), disposeHandler: true)
            {
                Timeout = TimeSpan.FromSeconds(120)
            };
            _clients.Add(key, client);
            return client;
        }
    }

    internal static SocketsHttpHandler CreateHandler(Uri? proxy)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 1,
            UseProxy = true
        };
        if (proxy is not null)
        {
            handler.Proxy = new WebProxy(proxy, false);
        }

        return handler;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var client in _clients.Values)
            {
                client.Dispose();
            }

            _clients.Clear();
        }
    }
}
