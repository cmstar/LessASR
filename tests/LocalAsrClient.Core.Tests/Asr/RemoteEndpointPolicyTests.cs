using LocalAsrClient.Core.Asr;

namespace LocalAsrClient.Core.Tests.Asr;

public sealed class RemoteEndpointPolicyTests
{
    [Theory]
    [InlineData("https://api.example.com/v1/audio/transcriptions")]
    [InlineData("https://203.0.113.10:9443/custom/transcribe?tenant=one")]
    [InlineData("http://localhost:8080/v1/audio/transcriptions")]
    [InlineData("http://127.255.255.254:8080/v1/audio/transcriptions")]
    [InlineData("http://10.0.0.1/v1/audio/transcriptions")]
    [InlineData("http://172.16.0.1/v1/audio/transcriptions")]
    [InlineData("http://172.31.255.254/v1/audio/transcriptions")]
    [InlineData("http://192.168.255.254/v1/audio/transcriptions")]
    [InlineData("http://169.254.1.2/v1/audio/transcriptions")]
    [InlineData("http://[::1]:8080/v1/audio/transcriptions")]
    [InlineData("http://[fc00::1]/v1/audio/transcriptions")]
    [InlineData("http://[fdff::1]/v1/audio/transcriptions")]
    [InlineData("http://[fe80::1]/v1/audio/transcriptions")]
    public void ParseAndValidate_AcceptsHttpsAndLocalHttp(string value)
    {
        var endpoint = RemoteEndpointPolicy.ParseAndValidate(value);

        Assert.Equal(value, endpoint.AbsoluteUri.TrimEnd('/'));
    }

    [Theory]
    [InlineData("http://example.com/v1/audio/transcriptions")]
    [InlineData("http://asr.local/v1/audio/transcriptions")]
    [InlineData("http://8.8.8.8/v1/audio/transcriptions")]
    [InlineData("http://172.15.255.255/v1/audio/transcriptions")]
    [InlineData("http://172.32.0.0/v1/audio/transcriptions")]
    [InlineData("http://224.0.0.1/v1/audio/transcriptions")]
    [InlineData("http://255.255.255.255/v1/audio/transcriptions")]
    [InlineData("http://0.0.0.0/v1/audio/transcriptions")]
    [InlineData("http://[::]/v1/audio/transcriptions")]
    [InlineData("ftp://192.168.1.8/v1/audio/transcriptions")]
    [InlineData("/v1/audio/transcriptions")]
    [InlineData("https://user:password@example.com/v1/audio/transcriptions")]
    [InlineData("https://example.com/v1/audio/transcriptions#fragment")]
    public void ParseAndValidate_RejectsUnsafeEndpoint(string value)
    {
        Assert.Throws<InvalidOperationException>(() => RemoteEndpointPolicy.ParseAndValidate(value));
    }

    [Fact]
    public void IsUnencryptedHttp_DistinguishesAllowedHttpFromHttps()
    {
        Assert.True(RemoteEndpointPolicy.IsUnencryptedHttp(
            new Uri("http://192.168.1.8/v1/audio/transcriptions")));
        Assert.False(RemoteEndpointPolicy.IsUnencryptedHttp(
            new Uri("https://192.168.1.8/v1/audio/transcriptions")));
    }
}
