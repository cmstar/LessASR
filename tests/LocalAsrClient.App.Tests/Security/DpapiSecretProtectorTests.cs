using LocalAsrClient.App.Security;

namespace LocalAsrClient.App.Tests.Security;

public sealed class DpapiSecretProtectorTests
{
    [Fact]
    public void ProtectAndUnprotect_RoundTripsForCurrentWindowsUser()
    {
        var protector = new DpapiSecretProtector();
        const string apiKey = "sk-test-secret-value";

        var protectedValue = protector.Protect(apiKey);
        var unprotectedValue = protector.Unprotect(protectedValue);

        Assert.NotEqual(apiKey, protectedValue);
        Assert.DoesNotContain(apiKey, protectedValue, StringComparison.Ordinal);
        Assert.Equal(apiKey, unprotectedValue);
    }
}
