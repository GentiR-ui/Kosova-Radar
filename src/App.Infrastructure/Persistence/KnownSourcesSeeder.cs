using App.Domain.Sources;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Persistence;

/// <summary>
/// Creates the two V1 HTML sources (design.md §6, ADR-035) when no source has their URL yet. They start inactive:
/// an Admin enables them or uses "Merr tani". Existing sources are never touched, so renames and settings survive.
/// Callers run it only when <c>Seed__KnownSources</c> is on.
/// </summary>
public sealed class KnownSourcesSeeder(AppDbContext db, TimeProvider time)
{
    private static readonly (string Name, string Adapter, string Url)[] Known =
    [
        ("Prishtina.com — Evente", HtmlAdapters.PrishtinaEvents, "https://prishtina.com/events"),
        ("Ka' Po Dalim", HtmlAdapters.Kapodalim, "https://kapodalim.com/ks"),
    ];

    public async Task SeedAsync(CancellationToken ct = default)
    {
        foreach (var (name, adapter, url) in Known)
        {
            if (await db.Sources.AnyAsync(s => s.BaseUrl == url, ct)) continue;
            db.Sources.Add(new Source
            {
                Name = name,
                Type = SourceType.Html,
                Adapter = adapter,
                BaseUrl = url,
                IsActive = false,
                IsUnreliable = false,
                PollIntervalMinutes = 180,
                CreatedAt = time.GetUtcNow(),
            });
        }

        await db.SaveChangesAsync(ct);
    }
}
