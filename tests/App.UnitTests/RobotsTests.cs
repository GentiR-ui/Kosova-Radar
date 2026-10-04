using System.Net;
using App.Application.Ingestion;
using App.Application.Sources;
using App.Infrastructure.Ingestion;

namespace App.UnitTests;

public class RobotsRulesTests
{
    private static bool Allowed(string robots, string url) => RobotsRules.Parse(robots).IsAllowed(new Uri(url));

    [Fact]
    public void Everything_is_allowed_without_rules()
    {
        Assert.True(Allowed("", "https://a.example/feed"));
        Assert.True(RobotsRules.AllowAll.IsAllowed(new Uri("https://a.example/")));
    }

    [Theory]
    [InlineData("/feed", false)]
    [InlineData("/feed.json?x=1", false)]
    [InlineData("/other", true)]
    [InlineData("/", true)]
    public void A_disallow_is_a_path_prefix(string path, bool allowed) =>
        Assert.Equal(allowed, Allowed("User-agent: *\nDisallow: /feed", "https://a.example" + path));

    [Fact]
    public void Disallow_slash_blocks_everything_and_an_empty_disallow_nothing()
    {
        Assert.False(Allowed("User-agent: *\nDisallow: /", "https://a.example/feed"));
        Assert.True(Allowed("User-agent: *\nDisallow:", "https://a.example/feed"));
    }

    [Fact]
    public void The_longest_match_wins_and_allow_wins_a_tie()
    {
        const string robots = "User-agent: *\nDisallow: /events\nAllow: /events/public\nDisallow: /same\nAllow: /same";
        Assert.False(Allowed(robots, "https://a.example/events/private"));
        Assert.True(Allowed(robots, "https://a.example/events/public/1"));
        Assert.True(Allowed(robots, "https://a.example/same"));
    }

    [Theory]
    [InlineData("/*.json", "/a/feed.json", false)]
    [InlineData("/*.json", "/a/feed.xml", true)]
    [InlineData("/*.json$", "/a/feed.json", false)]
    [InlineData("/*.json$", "/a/feed.json?x=1", true)]
    [InlineData("/a/*/c", "/a/b/c/d", false)]
    [InlineData("/a/*/c", "/a/b/d", true)]
    [InlineData("/feed$", "/feed", false)]
    [InlineData("/feed$", "/feed/", true)]
    [InlineData("/*a*b*c", "/xaxxbxxc", false)]
    [InlineData("/*a*b*c", "/xaxxcxxb", true)]
    public void Wildcards_and_the_end_anchor_work(string pattern, string path, bool allowed) =>
        Assert.Equal(allowed, Allowed($"User-agent: *\nDisallow: {pattern}", "https://a.example" + path));

    [Fact]
    public void Our_own_group_replaces_the_star_group()
    {
        const string robots = "User-agent: *\nDisallow: /\n\nUser-agent: KosovaRadarBot\nDisallow: /private";
        Assert.True(Allowed(robots, "https://a.example/feed"));
        Assert.False(Allowed(robots, "https://a.example/private/x"));
    }

    [Fact]
    public void The_agent_name_is_case_insensitive_and_a_version_suffix_is_ignored()
    {
        Assert.False(Allowed("user-agent: kosovaradarbot/1.0\ndisallow: /feed", "https://a.example/feed"));
        Assert.True(Allowed("user-agent: otherbot\ndisallow: /feed", "https://a.example/feed"));
    }

    [Fact]
    public void Several_user_agent_lines_share_one_group_and_a_rule_starts_a_new_one()
    {
        const string robots = "User-agent: otherbot\nUser-agent: *\nDisallow: /a\nUser-agent: thirdbot\nDisallow: /b";
        Assert.False(Allowed(robots, "https://a.example/a"));
        Assert.True(Allowed(robots, "https://a.example/b"));
    }

    [Fact]
    public void Groups_for_our_agent_are_merged()
    {
        const string robots = "User-agent: KosovaRadarBot\nDisallow: /a\n\nUser-agent: KosovaRadarBot\nDisallow: /b";
        Assert.False(Allowed(robots, "https://a.example/a"));
        Assert.False(Allowed(robots, "https://a.example/b"));
    }

    [Fact]
    public void Comments_blank_lines_crlf_bom_and_unknown_fields_are_tolerated()
    {
        const string robots = "﻿User-agent: * # everyone\r\nCrawl-delay: 5\r\nSitemap: https://a.example/s.xml\r\n\r\nDisallow: /feed # no\r\nnonsense line\r\n";
        Assert.False(Allowed(robots, "https://a.example/feed"));
        Assert.True(Allowed(robots, "https://a.example/ok"));
    }

    [Fact]
    public void Rules_before_any_user_agent_are_ignored() =>
        Assert.True(Allowed("Disallow: /feed\nUser-agent: otherbot\nDisallow: /", "https://a.example/feed"));

    [Fact]
    public void The_query_is_part_of_the_matched_path() =>
        Assert.False(Allowed("User-agent: *\nDisallow: /*?secret=", "https://a.example/feed?secret=1"));

    [Fact]
    public void Rules_and_paths_are_compared_percent_decoded()
    {
        Assert.False(Allowed("User-agent: *\nDisallow: /é", "https://a.example/%C3%A9t"));
        Assert.False(Allowed("User-agent: *\nDisallow: /%7Efoo", "https://a.example/~foo"));
        Assert.True(Allowed("User-agent: *\nDisallow: /%C3%A9", "https://a.example/e"));
    }

    [Fact]
    public void Many_user_agent_lines_do_not_multiply_the_rules()
    {
        var robots = string.Concat(Enumerable.Repeat("User-agent: *\n", 20_000)) + string.Concat(Enumerable.Repeat("Disallow: /x\n", 10_000)); // < 500 KiB
        Assert.False(Allowed(robots, "https://a.example/x"));
        Assert.True(Allowed(robots, "https://a.example/y"));
    }

    [Fact]
    public void Only_the_first_500_KiB_are_read_and_only_cr_line_ends_still_split_lines()
    {
        var late = new string('#', 600_000) + "\nUser-agent: *\nDisallow: /feed";
        Assert.True(Allowed("User-agent: *\nDisallow: /other\n" + late, "https://a.example/feed"));
        Assert.False(Allowed("User-agent: *\rDisallow: /feed", "https://a.example/feed"));
    }

    [Fact]
    public void A_pathological_pattern_does_not_hang()
    {
        var pattern = "/" + string.Concat(Enumerable.Repeat("*a", 200)) + "b";
        var path = "/" + new string('a', 2000);
        Assert.True(Allowed($"User-agent: *\nDisallow: {pattern}", "https://a.example" + path));
    }
}

public class HostThrottleTests
{
    private sealed class FakeTime : TimeProvider
    {
        public long Ticks { get; set; }
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Ticks;
    }

    [Fact]
    public void The_first_request_does_not_wait_and_the_next_waits_for_the_delay()
    {
        var time = new FakeTime();
        var throttle = new HostThrottle(TimeSpan.FromSeconds(10), time);

        Assert.Equal(TimeSpan.Zero, throttle.Reserve("a.example"));
        time.Ticks = 3000; // 3 s later
        Assert.Equal(TimeSpan.FromSeconds(7), throttle.Reserve("a.example"));
    }

    [Fact]
    public void Concurrent_requests_queue_one_slot_apart()
    {
        var throttle = new HostThrottle(TimeSpan.FromSeconds(10), new FakeTime());

        Assert.Equal(TimeSpan.Zero, throttle.Reserve("a.example"));
        Assert.Equal(TimeSpan.FromSeconds(10), throttle.Reserve("a.example"));
        Assert.Equal(TimeSpan.FromSeconds(20), throttle.Reserve("A.EXAMPLE"));
    }

    [Fact]
    public void After_the_delay_has_passed_there_is_no_wait()
    {
        var time = new FakeTime();
        var throttle = new HostThrottle(TimeSpan.FromSeconds(10), time);
        throttle.Reserve("a.example");

        time.Ticks = 10_000;

        Assert.Equal(TimeSpan.Zero, throttle.Reserve("a.example"));
    }

    [Fact]
    public void Hosts_and_ports_are_independent()
    {
        var throttle = new HostThrottle(TimeSpan.FromSeconds(10), new FakeTime());
        throttle.Reserve("a.example");

        Assert.Equal(TimeSpan.Zero, throttle.Reserve("b.example"));
        Assert.Equal(TimeSpan.Zero, throttle.Reserve("a.example:8443"));
    }

    [Fact]
    public void A_queue_longer_than_the_cap_is_refused_without_reserving()
    {
        var throttle = new HostThrottle(TimeSpan.FromSeconds(10), new FakeTime());
        for (var i = 0; i <= HostThrottle.MaxQueue; i++) Assert.NotNull(throttle.Reserve("a.example")); // waits 0, 10, 20, 30 s

        Assert.Null(throttle.Reserve("a.example"));
        Assert.Null(throttle.Reserve("a.example")); // still 30 s: the refusal reserved nothing
        Assert.NotNull(throttle.Reserve("b.example"));
    }

    [Fact]
    public void A_zero_delay_never_waits()
    {
        var throttle = new HostThrottle(TimeSpan.Zero, new FakeTime());
        Assert.Equal(TimeSpan.Zero, throttle.Reserve("a.example"));
        Assert.Equal(TimeSpan.Zero, throttle.Reserve("a.example"));
    }
}

public class RobotsCheckerTests
{
    private static readonly Uri Feed = new("https://feeds.example/feed.json");

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!.AbsoluteUri);
            return respond(request);
        }
    }

    private static (RobotsChecker Checker, StubHandler Handler) Create(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond, RobotsCache? cache = null)
    {
        var handler = new StubHandler(respond);
        var fetcher = new SourceHttpFetcher(new HttpClient(handler), new IngestionOptions { HttpTimeoutSeconds = 5 });
        return (new RobotsChecker(fetcher, cache ?? new RobotsCache()), handler);
    }

    private static Task<HttpResponseMessage> Answer(HttpStatusCode status, string body = "") =>
        Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });

    private static async Task<SourceFetchException> RefusedAsync(RobotsChecker checker, Uri? url = null) =>
        await Assert.ThrowsAsync<SourceFetchException>(() => checker.EnsureAllowedAsync(url ?? Feed, CancellationToken.None));

    [Fact]
    public async Task Robots_txt_is_requested_from_the_origin_root()
    {
        var (checker, handler) = Create(_ => Answer(HttpStatusCode.OK, "User-agent: *\nDisallow:"));
        await checker.EnsureAllowedAsync(new Uri("https://feeds.example:8443/a/b/feed.json?x=1"), CancellationToken.None);
        Assert.Equal(["https://feeds.example:8443/robots.txt"], handler.Requests);
    }

    [Fact]
    public async Task A_matching_disallow_is_RobotsDisallowed()
    {
        var (checker, _) = Create(_ => Answer(HttpStatusCode.OK, "User-agent: *\nDisallow: /feed"));
        Assert.Equal(RunErrorCodes.RobotsDisallowed, (await RefusedAsync(checker)).ErrorCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Gone)]
    public async Task A_4xx_robots_txt_allows_everything(HttpStatusCode status)
    {
        var (checker, _) = Create(_ => Answer(status));
        await checker.EnsureAllowedAsync(Feed, CancellationToken.None);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.Found)]
    public async Task A_5xx_or_unresolved_robots_txt_is_RobotsDisallowed(HttpStatusCode status)
    {
        var (checker, _) = Create(_ => Answer(status));
        Assert.Equal(RunErrorCodes.RobotsDisallowed, (await RefusedAsync(checker)).ErrorCode);
    }

    [Fact]
    public async Task A_robots_txt_over_5_MB_is_RobotsDisallowed_not_TooLarge()
    {
        var (checker, _) = Create(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[SourceHttpFetcher.MaxBytes + 1]) }));
        var e = await RefusedAsync(checker);
        Assert.Equal(RunErrorCodes.RobotsDisallowed, e.ErrorCode);
    }

    [Fact]
    public async Task Network_failures_keep_their_own_code()
    {
        var (unreachable, _) = Create(_ => throw new HttpRequestException("reset"));
        Assert.Equal(RunErrorCodes.Unreachable, (await RefusedAsync(unreachable)).ErrorCode);

        var (blocked, _) = Create(_ => throw new HttpRequestException("x", new BlockedAddressException()));
        Assert.Equal(RunErrorCodes.BlockedAddress, (await RefusedAsync(blocked)).ErrorCode);
    }

    [Fact]
    public async Task An_answer_is_cached_per_origin_but_a_failure_is_not()
    {
        var calls = 0;
        var (checker, handler) = Create(_ => ++calls == 1 ? Answer(HttpStatusCode.ServiceUnavailable) : Answer(HttpStatusCode.OK, "User-agent: *\nDisallow: /nope"));

        await RefusedAsync(checker); // 503: not cached
        await checker.EnsureAllowedAsync(Feed, CancellationToken.None);
        await checker.EnsureAllowedAsync(new Uri("https://feeds.example/other.json"), CancellationToken.None);
        await checker.EnsureAllowedAsync(new Uri("https://other.example/feed.json"), CancellationToken.None);

        Assert.Equal(["https://feeds.example/robots.txt", "https://feeds.example/robots.txt", "https://other.example/robots.txt"], handler.Requests);
    }

    [Fact]
    public async Task The_cache_expires_after_24_hours()
    {
        var time = new CacheClock { Now = new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero) };
        var (checker, handler) = Create(_ => Answer(HttpStatusCode.NotFound), new RobotsCache(time));

        await checker.EnsureAllowedAsync(Feed, CancellationToken.None);
        time.Now = time.Now.AddHours(23).AddMinutes(59);
        await checker.EnsureAllowedAsync(Feed, CancellationToken.None);
        Assert.Single(handler.Requests);

        time.Now = time.Now.AddMinutes(2);
        await checker.EnsureAllowedAsync(Feed, CancellationToken.None);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task A_disallow_is_applied_on_every_run_from_the_cache()
    {
        var (checker, handler) = Create(_ => Answer(HttpStatusCode.OK, "User-agent: *\nDisallow: /feed"));
        await RefusedAsync(checker);
        await RefusedAsync(checker);
        Assert.Single(handler.Requests);
    }

    private sealed class CacheClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
