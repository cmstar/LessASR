namespace LocalAsrClient.Core.Asr;

public static class RemoteProxyPolicy
{
    private static readonly HashSet<string> AllowedSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        Uri.UriSchemeHttp,
        Uri.UriSchemeHttps,
        "socks4",
        "socks4a",
        "socks5"
    };

    public static Uri? ParseOptionalAndValidate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var proxy)
            || string.IsNullOrWhiteSpace(proxy.Host))
        {
            throw new InvalidOperationException("代理服务器地址必须是完整 URL，例如 http://127.0.0.1:7890。");
        }

        if (!AllowedSchemes.Contains(proxy.Scheme))
        {
            throw new InvalidOperationException(
                "代理服务器仅支持 HTTP、HTTPS、SOCKS4、SOCKS4a 和 SOCKS5。");
        }

        if (!string.IsNullOrEmpty(proxy.UserInfo))
        {
            throw new InvalidOperationException(
                "代理服务器地址不能包含账号密码；当前版本暂不支持认证代理。");
        }

        if (proxy.AbsolutePath is not ("" or "/")
            || !string.IsNullOrEmpty(proxy.Query)
            || !string.IsNullOrEmpty(proxy.Fragment))
        {
            throw new InvalidOperationException("代理服务器地址只能包含协议、主机和端口。");
        }

        return proxy;
    }
}
