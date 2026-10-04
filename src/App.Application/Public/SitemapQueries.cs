using App.Application.Abstractions;
using App.Domain.Content;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Public;

/// <summary>Ids of the active published items for <c>/sitemap.xml</c> (REQ-091).</summary>
public sealed class SitemapQueries(IAppDbContext db, IClock clock)
{
    /// <summary>The sitemap protocol allows 50,000 URLs per file; three are the static pages.</summary>
    public const int MaxUrls = 50_000;

    public async Task<(IReadOnlyList<Guid> Events, IReadOnlyList<Guid> Deals)> ActiveIdsAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var events = await db.Events.AsNoTracking().Where(ActiveItems.Event(now))
            .OrderBy(e => e.StartsAt).ThenBy(e => e.Id).Select(e => e.Id).Take(MaxUrls - 3).ToListAsync(ct);
        var deals = await db.Deals.AsNoTracking().Where(ActiveItems.Deal(now))
            .OrderByDescending(d => d.LastVerifiedAt).ThenBy(d => d.Id).Select(d => d.Id).Take(MaxUrls - 3 - events.Count).ToListAsync(ct);
        return (events, deals);
    }
}
