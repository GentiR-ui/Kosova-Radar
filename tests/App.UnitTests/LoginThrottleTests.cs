using App.Api.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;

namespace App.UnitTests;

public class LoginThrottleTests
{
    private const string Ip = "1.1.1.1";
    private const string Email = "a@test.local";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));
    private readonly LoginThrottle _throttle;

    public LoginThrottleTests() => _throttle = Create(30);

    private LoginThrottle Create(int perIp) => new(new MemoryCache(new MemoryCacheOptions()), _time,
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Login:MaxFailuresPerIpPerMinute"] = perIp.ToString(System.Globalization.CultureInfo.InvariantCulture),
        }).Build());

    /// <summary>Failed attempts: reserved and never released.</summary>
    private void Fail(int times, string ip = Ip, string email = Email)
    {
        for (var i = 0; i < times; i++) Assert.True(_throttle.TryBegin(ip, email));
    }

    [Fact]
    public void Eleven_failures_pass_the_twelfth_attempt_is_blocked()
    {
        Fail(11);
        Assert.False(_throttle.TryBegin(Ip, "A@TEST.LOCAL ")); // email normalized
    }

    [Fact]
    public void Successful_logins_never_count()
    {
        Fail(10);
        for (var i = 0; i < 5; i++)
        {
            Assert.True(_throttle.TryBegin(Ip, Email));
            _throttle.Release(Ip, Email);
        }
        Fail(1);
        Assert.False(_throttle.TryBegin(Ip, Email));
    }

    [Fact]
    public void Unicode_variants_of_the_email_share_the_block()
    {
        Fail(11, email: "kosova@test.local");
        Assert.False(_throttle.TryBegin(Ip, "Kosova@test.local")); // Kelvin sign normalizes to K
    }

    [Fact]
    public void More_than_the_per_ip_cap_of_failures_across_emails_block_the_ip()
    {
        for (var i = 0; i < 31; i++) Assert.True(_throttle.TryBegin(Ip, $"u{i}@test.local"));
        Assert.False(_throttle.TryBegin(Ip, "new@test.local"));
        Assert.True(_throttle.TryBegin("2.2.2.2", "new@test.local"));
    }

    [Fact]
    public void Other_ip_and_other_email_are_not_affected()
    {
        Fail(11);
        Assert.True(_throttle.TryBegin("2.2.2.2", Email));
        Assert.True(_throttle.TryBegin(Ip, "b@test.local"));
    }

    [Fact]
    public void Block_ends_when_the_minute_window_ends()
    {
        Fail(11);
        _time.Advance(TimeSpan.FromSeconds(59));
        Assert.False(_throttle.TryBegin(Ip, Email));
        _time.Advance(TimeSpan.FromSeconds(2));
        Assert.True(_throttle.TryBegin(Ip, Email));
    }

    [Fact]
    public void More_than_fifty_failures_for_one_email_from_many_ips_block_it_for_the_hour()
    {
        for (var i = 0; i < 51; i++) Assert.True(_throttle.TryBegin($"10.0.0.{i}", Email));
        Assert.False(_throttle.TryBegin("9.9.9.9", Email));
        _time.Advance(TimeSpan.FromMinutes(61));
        Assert.True(_throttle.TryBegin("9.9.9.9", Email));
    }

    [Fact]
    public async Task A_parallel_burst_cannot_exceed_the_limit()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 40).Select(_ => Task.Run(() => _throttle.TryBegin(Ip, Email))));
        Assert.Equal(11, results.Count(allowed => allowed));
    }
}
