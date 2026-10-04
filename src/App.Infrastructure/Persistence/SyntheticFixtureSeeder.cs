using System.Text.Json;
using App.Application.Ingestion;
using App.Application.Sources;
using App.Domain.Content;
using App.Domain.Sources;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Persistence;

/// <summary>
/// Development data (design.md §9): an inactive fixture source with 24 synthetic items covering the review cases,
/// fetched once when it has never been fetched. Dates are relative to the start time so nothing is expired by accident,
/// except the item that is meant to be. Callers run it only when <c>Seed__SyntheticFixtures</c> is on.
/// </summary>
public sealed class SyntheticFixtureSeeder(AppDbContext db, IngestionService ingestion, IngestionOptions options, TimeProvider time)
{
    public const string SourceName = "Fixture — Shembuj sintetikë";

    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (!options.AllowFixtureSources) return; // the fixture adapter refuses to run; do not create a source nobody can fetch

        var source = await db.Sources.SingleOrDefaultAsync(s => s.Name == SourceName, ct);
        if (source is null)
        {
            source = new Source
            {
                Name = SourceName,
                Type = SourceType.Fixture,
                FixturePayload = BuildPayload(time.GetUtcNow()),
                IsActive = false,
                PollIntervalMinutes = 1440,
                CreatedAt = time.GetUtcNow(),
            };
            db.Sources.Add(source);
            await db.SaveChangesAsync(ct);
        }

        if (source.LastFetchedAt is not null) return;

        // A scheduled run only accepts an active source and a manual run needs a signed-in user for the audit log:
        // switch the source on for this one run, before the scheduler starts.
        source.IsActive = true;
        await db.SaveChangesAsync(ct);
        try
        {
            await ingestion.RunAsync(source.Id, JobTrigger.Schedule, TimeSpan.Zero, ct);
        }
        finally
        {
            await db.Sources.Where(s => s.Id == source.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false), CancellationToken.None);
        }
    }

    internal static string BuildPayload(DateTimeOffset now)
    {
        string At(double days, int hour = 19) =>
            new DateTimeOffset(now.UtcDateTime.Date.AddDays(days).AddHours(hour), TimeSpan.Zero).ToString("yyyy-MM-ddTHH:mm:ssK");

        object Event(string id, string title, string? city, string? startsAt, object? extra = null) => Merge(new Dictionary<string, object?>
        {
            ["externalId"] = id,
            ["kind"] = "event",
            ["title"] = title,
            ["url"] = $"https://fixtures.example/events/{id}",
            ["city"] = city,
            ["startsAt"] = startsAt,
            ["priceText"] = "5 €",
            ["price"] = 5,
        }, extra);

        object Deal(string id, string title, object? extra = null) => Merge(new Dictionary<string, object?>
        {
            ["externalId"] = id,
            ["kind"] = "deal",
            ["title"] = title,
            ["url"] = $"https://fixtures.example/deals/{id}",
            ["city"] = "Prishtinë",
            ["merchant"] = "Dyqani Shembull",
            ["validUntil"] = At(20),
        }, extra);

        object[] items =
        [
            // Valid events (6)
            Event("ev-valid-1", "Koncert pranvere", "Prishtinë", At(3)),
            Event("ev-valid-2", "Natë filmi në park", "Prizren", At(5)),
            Event("ev-valid-3", "Turne shahu", "Pejë", At(7)),
            Event("ev-valid-4", "Panair librash", "Gjilan", At(9), new { endsAt = At(10) }),
            Event("ev-valid-5", "Ndeshje basketbolli", "Ferizaj", At(11), new { imageUrl = "https://fixtures.example/img/basket.jpg" }),
            Event("ev-valid-6", "Teatër për fëmijë", "Mitrovicë", At(13)),
            // Review and reject cases (events)
            Event("ev-no-time", "Event pa orë", "Prishtinë", null),
            Event("ev-expired", "Event i kaluar", "Prishtinë", At(-10)),
            Event("ev-bad-url", "Event me lidhje të gabuar", "Prizren", At(6), new { url = "not-a-url" }),
            Event("ev-abroad", "Koncert në Shkup", "Shkup", At(8)),
            Event("ev-bad-range", "Event me kohë të prapme", "Pejë", At(9), new { endsAt = At(8) }),
            Event("ev-bad-image", "Event me foto të gabuar", "Gjilan", At(12), new { imageUrl = "javascript:alert(1)" }),
            Event("ev-ai-ok", "Shfaqje nga teksti", null, null,
                new { text = $"Koncert në Prishtinë më {At(14)}, hyrja 8 €." }),
            Event("ev-ai-fail", "Event që AI nuk e lexon", null, null, new { text = "Detaje në tekst #ai-fail" }),
            // Same title in two cities (same-source twins are not flagged; duplicates are detected across sources)
            Event("ev-twin-a", "Festivali i verës", "Prishtinë", At(15, 18)),
            Event("ev-twin-b", "Festivali i verës", "Prizren", At(15, 20)),
            // Deals (8)
            Deal("dl-valid-1", "Laptop me zbritje", new { oldPrice = 800, newPrice = 600, discountPercent = 25 }),
            Deal("dl-valid-2", "Telefon i ri", new { oldPrice = 300, newPrice = 240 }),
            Deal("dl-valid-3", "Kafe dhe ëmbëlsirë", new { priceText = "2 € në vend të 3 €" }),
            Deal("dl-no-price", "Ofertë pa çmim"),
            Deal("dl-conflict", "Ofertë me zbritje të pasaktë", new { oldPrice = 100, newPrice = 80, discountPercent = 50 }),
            Deal("dl-inconsistent", "Ofertë me çmime të prapme", new { oldPrice = 50, newPrice = 70 }),
            Deal("dl-expired", "Ofertë e skaduar", new { newPrice = 10, validUntil = At(-3) }),
            Deal("dl-abroad", "Ofertë në Tiranë", new { city = "Tiranë", newPrice = 9 }),
        ];
        return JsonSerializer.Serialize(new { items });
    }

    private static Dictionary<string, object?> Merge(Dictionary<string, object?> baseFields, object? extra)
    {
        if (extra is null) return baseFields;
        foreach (var p in extra.GetType().GetProperties()) baseFields[p.Name] = p.GetValue(extra);
        return baseFields;
    }
}
