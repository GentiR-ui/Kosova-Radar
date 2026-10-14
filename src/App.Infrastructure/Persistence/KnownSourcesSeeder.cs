using App.Application.Audit;
using App.Application.Sources;
using App.Domain.Reference;
using App.Domain.Sources;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Persistence;

/// <summary>
/// Creates the known HTML sources (design.md §6, ADR-035) when no source has their URL yet. Unless the caller says
/// otherwise (<c>Seed__KnownSourcesActive</c>, <c>Seed__KnownSourcesAutoPublish</c>) they start inactive and without
/// auto-publish: an Admin enables them or uses "Merr tani". Existing sources are never touched, so renames and settings survive.
/// Callers run it only when <c>Seed__KnownSources</c> is on.
/// </summary>
public sealed class KnownSourcesSeeder(AppDbContext db, TimeProvider time, IAuditWriter audit)
{
    private static readonly (string Name, string Adapter, string Url, ItemKind Kind)[] Known =
    [
        ("Prishtina.com — Evente", HtmlAdapters.PrishtinaEvents, "https://prishtina.com/events", ItemKind.Event),
        ("Ka' Po Dalim", HtmlAdapters.Kapodalim, "https://kapodalim.com/ks", ItemKind.Event),
        // Shops list products on sale without an end date: today's deals (REQ-045, ADR-045).
        ("Foleja — Deals", HtmlAdapters.Foleja, "https://www.foleja.com/foleja-Deals/", ItemKind.Deal),
        ("Gjirafa Mall — Oferta", HtmlAdapters.GjirafaMall, "https://gjirafamall.com/oferta-speciale", ItemKind.Deal),
    ];

    /// <summary>The address T053 seeded before T055 found the server-rendered listing; its parser was a placeholder that could only fail.</summary>
    private const string LegacyGjirafaMallUrl = "https://gjirafamall.com";

    public async Task SeedAsync(bool active = false, bool autoPublish = false, CancellationToken ct = default)
    {
        // The one exception to "existing sources are never touched": a row still on the site root has never been able to
        // fetch anything, so it moves to the real listing (and keeps its name, activity and settings).
        var listingUrl = Known.Single(k => k.Adapter == HtmlAdapters.GjirafaMall).Url;
        if (!await db.Sources.AnyAsync(s => s.BaseUrl == listingUrl, ct))
        {
            var legacy = await db.Sources.Where(s => s.BaseUrl == LegacyGjirafaMallUrl && s.Adapter == HtmlAdapters.GjirafaMall).ToListAsync(ct);
            foreach (var row in legacy)
            {
                var before = SourceAuditState.From(row);
                row.BaseUrl = listingUrl;
                // The failures belong to the placeholder parser, not to the real listing.
                row.LastError = null;
                row.ConsecutiveFailures = 0;
                audit.RecordAsSystem(AuditActions.SourceUpdate, AuditEntityTypes.Source, row.Id, before, SourceAuditState.From(row));
            }
            if (legacy.Count > 0) await db.SaveChangesAsync(ct); // so the lookup below sees the moved row
        }

        foreach (var (name, adapter, url, kind) in Known)
        {
            if (await db.Sources.AnyAsync(s => s.BaseUrl == url, ct)) continue;
            var source = new Source
            {
                Name = name,
                Type = SourceType.Html,
                Adapter = adapter,
                DefaultKind = kind,
                BaseUrl = url,
                IsActive = active,
                IsUnreliable = false,
                AutoPublish = autoPublish,
                PollIntervalMinutes = 180,
                CreatedAt = time.GetUtcNow(),
            };
            db.Sources.Add(source);
            // Env-driven seeding (an active, auto-publishing source before any Admin looked) leaves the same trace as the form.
            audit.RecordAsSystem(AuditActions.SourceCreate, AuditEntityTypes.Source, source.Id, after: SourceAuditState.From(source));
        }

        await db.SaveChangesAsync(ct);
    }
}
