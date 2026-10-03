using System.Net;
using App.Api.Http;

namespace App.UnitTests;

public class ClientIpTests
{
    [Theory]
    [InlineData("203.0.113.7", "203.0.113.7")]
    [InlineData("::ffff:203.0.113.7", "203.0.113.7")]
    [InlineData("2001:db8:1:2:aaaa:bbbb:cccc:dddd", "2001:db8:1:2::/64")]
    [InlineData("2001:db8:1:2::1", "2001:db8:1:2::/64")]
    public void Keys_ipv4_by_address_and_ipv6_by_64_prefix(string address, string expected) =>
        Assert.Equal(expected, ClientIp.Key(IPAddress.Parse(address)));

    [Fact]
    public void Missing_address_is_unknown() => Assert.Equal("unknown", ClientIp.Key((IPAddress?)null));
}
