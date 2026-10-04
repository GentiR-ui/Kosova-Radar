using System.Collections.Concurrent;
using App.Application.Ingestion;

namespace App.Infrastructure.Ingestion;

/// <summary>Parsed robots.txt per origin, kept for 24 hours (design.md §6). Only answers are cached, never failures.</summary>
public sealed class RobotsCache(TimeProvider time)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);
    private const int PruneAbove = 1024;

    private readonly ConcurrentDictionary<string, (RobotsRules Rules, DateTimeOffset Expires)> _entries = new(StringComparer.OrdinalIgnoreCase);

    public RobotsCache() : this(TimeProvider.System)
    {
    }

    public bool TryGet(string origin, out RobotsRules rules)
    {
        if (_entries.TryGetValue(origin, out var entry) && entry.Expires > time.GetUtcNow())
        {
            rules = entry.Rules;
            return true;
        }

        rules = RobotsRules.AllowAll;
        return false;
    }

    public void Set(string origin, RobotsRules rules)
    {
        var now = time.GetUtcNow();
        if (_entries.Count > PruneAbove)
            foreach (var stale in _entries.Where(e => e.Value.Expires <= now).Select(e => e.Key))
                _entries.TryRemove(stale, out _);
        _entries[origin] = (rules, now + Lifetime);
    }
}

/// <summary>
/// robots.txt check before a source is fetched, through the same guarded client as the source itself (so a blocked
/// host is <c>BlockedAddress</c> before anything else): 2xx → parse, other 4xx → allow all, 5xx and 429 → refused
/// (<c>RobotsDisallowed</c>, not cached so the next due run tries again), network failures keep their own error code.
/// </summary>
public sealed class RobotsChecker(SourceHttpFetcher fetcher, RobotsCache cache)
{
    /// <exception cref="SourceFetchException">RobotsDisallowed, or the robots.txt fetch failed (Unreachable, Timeout, BlockedAddress, …).</exception>
    public async Task EnsureAllowedAsync(Uri url, CancellationToken ct)
    {
        var origin = url.GetLeftPart(UriPartial.Authority);
        if (!cache.TryGet(origin, out var rules))
        {
            rules = await LoadAsync(new Uri($"{origin}/robots.txt"), ct);
            cache.Set(origin, rules);
        }

        if (!rules.IsAllowed(url))
            throw new SourceFetchException(RunErrorCodes.RobotsDisallowed, "The site's robots.txt does not allow fetching this address.");
    }

    private async Task<RobotsRules> LoadAsync(Uri robotsUrl, CancellationToken ct)
    {
        try
        {
            return RobotsRules.Parse(await fetcher.GetStringAsync(robotsUrl, ct));
        }
        catch (SourceFetchException e) when (e is { ErrorCode: RunErrorCodes.HttpStatus, HttpStatus: >= 400 and < 500 and not 429 })
        {
            return RobotsRules.AllowAll;
        }
        catch (SourceFetchException e) when (e.ErrorCode is RunErrorCodes.HttpStatus or RunErrorCodes.TooLarge or RunErrorCodes.MalformedPayload)
        {
            throw new SourceFetchException(RunErrorCodes.RobotsDisallowed, "The site's robots.txt is unavailable or unusable, so the source is not fetched.");
        }
    }
}
