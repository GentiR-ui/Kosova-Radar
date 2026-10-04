using App.Application.Ingestion;

namespace App.Infrastructure.Ingestion;

/// <summary>
/// At most one request per host every <c>Ingestion__PerHostDelaySeconds</c> (requirements §6). Callers reserve the
/// next free slot of their host and wait for it; concurrent callers for one host queue up one slot apart.
/// </summary>
public sealed class HostThrottle(TimeSpan delay, TimeProvider time)
{
    private const int PruneAbove = 1024;

    /// <summary>A run that would wait longer than this many delays fails instead (a stuck queue must not stall the scheduler).</summary>
    public const int MaxQueue = 3;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, long> _nextFree = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Reserves the next slot for <paramref name="host"/> and returns how long to wait until it starts, or
    /// <c>null</c> (nothing reserved) when the queue for that host is already longer than <see cref="MaxQueue"/> delays.
    /// </summary>
    public TimeSpan? Reserve(string host)
    {
        if (delay <= TimeSpan.Zero) return TimeSpan.Zero;

        lock (_gate)
        {
            var now = time.GetTimestamp();
            if (_nextFree.Count > PruneAbove)
                foreach (var stale in _nextFree.Where(e => e.Value <= now).Select(e => e.Key).ToList())
                    _nextFree.Remove(stale);

            var start = _nextFree.TryGetValue(host, out var free) && free > now ? free : now;
            if (start - now > MaxQueue * delay.TotalSeconds * time.TimestampFrequency) return null;
            _nextFree[host] = start + (long)(delay.TotalSeconds * time.TimestampFrequency);
            return time.GetElapsedTime(now, start);
        }
    }
}

/// <summary>Spaces every request of the <c>sources</c> client (robots.txt, feeds and retries) per host.</summary>
internal sealed class HostThrottleHandler(HostThrottle throttle) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var wait = throttle.Reserve(request.RequestUri!.Authority)
            ?? throw new SourceFetchException(RunErrorCodes.Timeout, "Too many requests are waiting for this host.");
        if (wait > TimeSpan.Zero)
        {
            // Tell the fetcher first: its deadline must not run out while we are being polite.
            if (request.Options.TryGetValue(SourceHttpFetcher.ThrottleWaitKey, out var report)) report(wait);
            await Task.Delay(wait, ct);
        }

        return await base.SendAsync(request, ct);
    }
}
