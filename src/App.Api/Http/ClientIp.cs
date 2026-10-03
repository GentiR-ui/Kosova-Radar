using System.Net;
using System.Net.Sockets;

namespace App.Api.Http;

/// <summary>
/// Rate-limit key for the calling client: the IPv4 address, or the /64 prefix for IPv6
/// (a single host usually owns a whole /64, so per-address keys would give it 2^64 budgets).
/// </summary>
public static class ClientIp
{
    public static string Key(HttpContext context) => Key(context.Connection.RemoteIpAddress);

    public static string Key(IPAddress? ip)
    {
        if (ip is null) return "unknown";
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily != AddressFamily.InterNetworkV6) return ip.ToString();
        var bytes = ip.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new IPAddress(bytes) + "/64";
    }
}
