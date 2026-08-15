using LocalAsrClient.Core.Asr;

namespace LocalAsrClient.Core.Tests.Asr;

public sealed class RemoteProxyPolicyTests
{
    [Theory]
    [InlineData("http://127.0.0.1:7890", "http://127.0.0.1:7890/")]
    [InlineData("https://proxy.example.com:8443", "https://proxy.example.com:8443/")]
    [InlineData("socks4://127.0.0.1:1080", "socks4://127.0.0.1:1080/")]
    [InlineData("socks4a://proxy.example.com:1080", "socks4a://proxy.example.com:1080/")]
    [InlineData("socks5://proxy.example.com:1080", "socks5://proxy.example.com:1080/")]
    public void ParseOptionalAndValidate_AllowsSupportedProxySchemes(
        string value,
        string expected)
    {
        var proxy = RemoteProxyPolicy.ParseOptionalAndValidate(value);

        Assert.Equal(expected, proxy?.AbsoluteUri);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void ParseOptionalAndValidate_TreatsBlankAsDirectConnection(string? value)
    {
        Assert.Null(RemoteProxyPolicy.ParseOptionalAndValidate(value));
    }

    [Theory]
    [InlineData("ftp://proxy.example.com:21")]
    [InlineData("http://proxy.example.com:8080/path")]
    [InlineData("http://proxy.example.com:8080?mode=fast")]
    [InlineData("http://proxy.example.com:8080/#fragment")]
    public void ParseOptionalAndValidate_RejectsUnsupportedOrNonServerAddresses(string value)
    {
        Assert.Throws<InvalidOperationException>(() =>
            RemoteProxyPolicy.ParseOptionalAndValidate(value));
    }

    [Fact]
    public void ParseOptionalAndValidate_RejectsEmbeddedCredentials()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            RemoteProxyPolicy.ParseOptionalAndValidate("socks5://user:password@127.0.0.1:1080"));

        Assert.Contains("账号密码", error.Message, StringComparison.Ordinal);
    }
}
