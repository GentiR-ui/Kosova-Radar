using System.Net;
using System.Net.Sockets;

namespace App.Infrastructure.Ingestion;

/// <summary>SSRF guard rules (design.md §6): which addresses a source fetch may connect to.</summary>
public static class IpClassifier
{
    /// <summary>True only for addresses on the public internet; everything else is blocked.</summary>
    public static bool IsPublic(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // ::ffff:a.b.c.d (mapped) is the IPv4 address; so is the deprecated ::a.b.c.d (compatible) form.
            if (address.IsIPv4MappedToIPv6) return IsPublic(address.MapToIPv4());
            var bytes = address.GetAddressBytes();
            if (bytes.AsSpan(0, 12).IndexOfAnyExcept((byte)0) < 0) return false; // ::, ::1 and ::a.b.c.d
            // NAT64 (64:ff9b::/96) embeds an IPv4 address that may be private.
            if (bytes[0] == 0x00 && bytes[1] == 0x64 && bytes[2] == 0xff && bytes[3] == 0x9b && bytes.AsSpan(4, 8).IndexOfAnyExcept((byte)0) < 0)
                return IsPublic(new IPAddress(bytes.AsSpan(12, 4)));
            // Only global unicast (2000::/3) is public; this also drops the translation prefixes (::ffff:0:0/96, 64:ff9b:1::/48),
            // discard (100::/64) and everything else reserved. Inside it: 6to4 and Teredo embed an IPv4 address that may be
            // private, and 2001:db8::/32 is documentation.
            if ((bytes[0] & 0xE0) != 0x20) return false;
            if (bytes[0] == 0x20 && (bytes[1] == 0x02 || (bytes[1] == 0x01 && (bytes[2] == 0 && bytes[3] == 0 || bytes[2] == 0x0d && bytes[3] == 0xb8)))) return false;
            return true;
        }

        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = address.GetAddressBytes();
        return !(
            b[0] == 0 // 0.0.0.0/8
            || b[0] == 10
            || b[0] == 127
            || (b[0] == 100 && b[1] is >= 64 and <= 127) // 100.64.0.0/10
            || (b[0] == 169 && b[1] == 254)
            || (b[0] == 172 && b[1] is >= 16 and <= 31)
            || (b[0] == 192 && b[1] == 0 && b[2] is 0 or 2) // 192.0.0.0/24 and documentation 192.0.2.0/24
            || (b[0] == 192 && b[1] == 88 && b[2] == 99) // 6to4 relay anycast
            || (b[0] == 198 && b[1] == 51 && b[2] == 100) // documentation
            || (b[0] == 203 && b[1] == 0 && b[2] == 113) // documentation
            || (b[0] == 168 && b[1] == 63 && b[2] == 129 && b[3] == 16) // Azure WireServer: public-looking, host-local
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 198 && b[1] is 18 or 19) // 198.18.0.0/15
            || b[0] >= 224); // multicast, reserved and broadcast
    }
}
