using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;

namespace App.Api.Http;

/// <summary>
/// Failed-login limits (REQ-P06, ADR-016, ADR-018, ADR-019):
/// more than 10 failures for one (client IP, email) per minute block that pair until the window ends;
/// more than 50 failures for one email from any IP per hour block that email; more than
/// Login:MaxFailuresPerIpPerMinute (default 30) failures from one client IP across all emails block that IP.
/// Every attempt reserves a slot up front (atomic check-and-count, so parallel bursts cannot slip through) and a
/// successful login gives it back, so successes never count. Emails are normalized exactly like Identity does.
/// Uses the real clock, never the X-Test-Now override.
/// </summary>
public sealed class LoginThrottle
{
    private static readonly Window PerPair = new("login-pair", TimeSpan.FromMinutes(1), 10);
    private static readonly Window PerEmail = new("login-email", TimeSpan.FromHours(1), 50);

    private readonly IMemoryCache _cache;
    private readonly TimeProvider _time;
    // Identity's default normalizer (stateless); the registered one is scoped, this class is a singleton.
    private readonly ILookupNormalizer _normalizer = new UpperInvariantLookupNormalizer();
    private readonly Window _perIp;
    private readonly Lock _gate = new();

    public LoginThrottle(IMemoryCache cache, TimeProvider time, IConfiguration config)
    {
        _cache = cache;
        _time = time;
        _perIp = new Window("login-ip", TimeSpan.FromMinutes(1), config.GetValue("Login:MaxFailuresPerIpPerMinute", 30));
    }

    private sealed record Window(string Prefix, TimeSpan Length, int MaxFailures);

    private sealed class Counter(DateTimeOffset start)
    {
        public DateTimeOffset Start { get; } = start;
        public int Attempts;
    }

    /// <summary>Reserves an attempt. False when the pair, the email or the client IP is blocked (nothing is counted then).</summary>
    public bool TryBegin(string? clientIp, string email)
    {
        lock (_gate)
        {
            // Per-IP first: a blocked client must not create per-email/per-pair entries (unbounded cache growth).
            var ip = clientIp ?? "unknown";
            if (Current(_perIp, ip, _time.GetUtcNow()).Attempts > _perIp.MaxFailures) return false;
            var counters = Counters(clientIp, email);
            if (counters.Any(c => c.Counter.Attempts > c.Window.MaxFailures)) return false;
            foreach (var c in counters) c.Counter.Attempts++;
            return true;
        }
    }

    /// <summary>Gives a reserved attempt back after a successful login.</summary>
    public void Release(string? clientIp, string email)
    {
        lock (_gate)
        {
            foreach (var c in Counters(clientIp, email))
                if (c.Counter.Attempts > 0) c.Counter.Attempts--;
        }
    }

    private (Window Window, Counter Counter)[] Counters(string? clientIp, string email)
    {
        var now = _time.GetUtcNow();
        var ip = clientIp ?? "unknown";
        var normalized = _normalizer.NormalizeEmail(email.Trim()) ?? "";
        return
        [
            (PerPair, Current(PerPair, $"{ip}|{normalized}", now)),
            (PerEmail, Current(PerEmail, normalized, now)),
            (_perIp, Current(_perIp, ip, now)),
        ];
    }

    /// <summary>The counter of the current window (a new one when the previous window has ended). Call under the gate.</summary>
    private Counter Current(Window w, string key, DateTimeOffset now)
    {
        var cacheKey = $"{w.Prefix}:{key}";
        if (_cache.TryGetValue(cacheKey, out Counter? c) && c is not null && now < c.Start + w.Length) return c;
        c = new Counter(now);
        _cache.Set(cacheKey, c, w.Length); // cache expiry only frees memory; the window check uses TimeProvider
        return c;
    }
}
