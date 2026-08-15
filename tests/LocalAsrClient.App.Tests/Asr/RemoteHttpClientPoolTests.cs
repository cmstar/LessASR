using System.Net;
using LocalAsrClient.App.Asr;

namespace LocalAsrClient.App.Tests.Asr;

public sealed class RemoteHttpClientPoolTests
{
    [Fact]
    public void CreateHandler_WithoutExplicitProxy_UsesTheSystemProxyConfiguration()
    {
        using var handler = RemoteHttpClientPool.CreateHandler(proxy: null);

        Assert.True(handler.UseProxy);
        Assert.Null(handler.Proxy);
    }

    [Theory]
    [InlineData("http://127.0.0.1:7890/")]
    [InlineData("https://proxy.example.com:8443/")]
    [InlineData("socks4://127.0.0.1:1080/")]
    [InlineData("socks4a://proxy.example.com:1080/")]
    [InlineData("socks5://proxy.example.com:1080/")]
    public void CreateHandler_WithProxy_UsesTheExactServerWithoutLocalBypass(string proxyUrl)
    {
        var proxy = new Uri(proxyUrl);

        using var handler = RemoteHttpClientPool.CreateHandler(proxy);

        Assert.True(handler.UseProxy);
        var webProxy = Assert.IsType<WebProxy>(handler.Proxy);
        Assert.Equal(proxy, webProxy.Address);
        Assert.False(webProxy.BypassProxyOnLocal);
        Assert.Null(webProxy.Credentials);
    }

    [Fact]
    public void GetClient_ReusesAConnectionPoolForTheSameNormalizedProxy()
    {
        using var pool = new RemoteHttpClientPool();

        var first = pool.GetClient(" socks5://127.0.0.1:1080 ");
        var second = pool.GetClient("socks5://127.0.0.1:1080/");
        var direct = pool.GetClient("");

        Assert.Same(first, second);
        Assert.NotSame(first, direct);
    }
}
