using System.Net;
using System.Net.Sockets;

namespace LocalAsrClient.Core.Asr;

public static class RemoteEndpointPolicy
{
    public static Uri ParseAndValidate(string value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var endpoint)
            || endpoint.Scheme != Uri.UriSchemeHttp
            && endpoint.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("转写接口地址必须是完整的 HTTP 或 HTTPS 地址。");
        }

        if (!string.IsNullOrEmpty(endpoint.UserInfo))
        {
            throw new InvalidOperationException("转写接口地址不能包含用户名或密码。");
        }

        if (!string.IsNullOrEmpty(endpoint.Fragment))
        {
            throw new InvalidOperationException("转写接口地址不能包含片段标识。");
        }

        var host = endpoint.DnsSafeHost.TrimEnd('.');
        var hasAddress = IPAddress.TryParse(host, out var address);
        if (hasAddress && !IsUsableUnicastAddress(address!))
        {
            throw new InvalidOperationException("转写接口地址不能使用未指定、多播或广播地址。");
        }

        if (endpoint.Scheme == Uri.UriSchemeHttps)
        {
            return endpoint;
        }

        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || hasAddress && IsAllowedHttpAddress(address!))
        {
            return endpoint;
        }

        throw new InvalidOperationException("公网地址和主机名必须使用 HTTPS；HTTP 仅允许本机或局域网 IP 地址。");
    }

    public static bool IsUnencryptedHttp(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return endpoint.Scheme == Uri.UriSchemeHttp;
    }

    private static bool IsUsableUnicastAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.Any.Equals(address)
            || IPAddress.IPv6Any.Equals(address)
            || IPAddress.Broadcast.Equals(address))
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => bytes[0] is < 224 or > 239,
            AddressFamily.InterNetworkV6 => bytes[0] != 0xff,
            _ => false
        };
    }

    private static bool IsAllowedHttpAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes[0] == 127
                || bytes[0] == 10
                || bytes[0] == 172 && bytes[1] is >= 16 and <= 31
                || bytes[0] == 192 && bytes[1] == 168
                || bytes[0] == 169 && bytes[1] == 254;
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return false;
        }

        return IPAddress.IPv6Loopback.Equals(address)
            || (bytes[0] & 0xfe) == 0xfc
            || bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80;
    }
}
