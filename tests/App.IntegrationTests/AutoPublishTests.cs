using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Application.Ingestion;
using App.Domain.Content;
using App.Domain.Sources;
using App.Domain.Text;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace App.IntegrationTests;

/// <summary>T049: auto-publish per source (REQ-023, 024, 026, 044; ADR-039, ADR-042) end to end against PostgreSQL.</summary>
[Collection(IntegrationCollection.Name)]
public class AutoPublishTests(AppFactory app)
{
    private static string Token() => Guid.NewGuid().ToString("N")[..8];

    private static string Event(string id, string title, DateTimeOffset start, string city = "Prizren", string? venue = null) =>
        $"{{\"externalId\":\"{id}\",\"kind\":\"event\",\"title\":\"{title}\",\"url\":\"https://example.test/{id}\"," +
        (city.Length > 0 ? $"\"city\":\"{city}\"," : "") +
        (venue is null ? "" : $"\"venue\":\"{venue}\",") +
        $"\"startsAt\":\"{start:yyyy-MM-ddTHH:mm:ssZ}\"}}";

    private static string Deal(string id, string title, decimal oldPrice, decimal newPrice, DateTimeOffset validUntil) =>
        $"{{\"externalId\":\"{id}\",\"kind\":\"deal\",\"title\":\"{title}\",\"url\":\"https://example.test/{id}\",\"city\":\"Prizren\"," +
        $"\"oldPrice\":{oldPrice},\"newPrice\":{newPrice},\"validUntil\":\"{validUntil:yyyy-MM-ddTHH:mm:ssZ}\"}}";

    private static string Payload(params string[] items) => "{\"items\":[" + string.Join(",", items) + "]}";

    private async Task<HttpClient> LoginAdminAsync()
    {
        var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@kosovaradar.local", password = "Radar-admin-dev-2026" })).StatusCode);
        return client;
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage res, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var text = await res.Content.ReadAsStringAsync();
        Assert.True(res.StatusCode == expected, $"Expected {(int)expected} but got {(int)res.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static object SourceBody(string payload, bool autoPublish, string? name = null) => new
    {
        name = name ?? $"Burim {Token()}",
        type = "fixture",
        baseUrl = (string?)null,
        fixturePayload = payload,
        defaultKind = "event",
        pollIntervalMinutes = 1440,
        isActive = false,
        isUnreliable = false,
        autoPublish,
    };

    private async Task<Guid> CreateSourceAsync(HttpClient admin, string payload, bool autoPublish = true) =>
        (await JsonAsync(await admin.PostAsJsonAsync("/api/admin/sources", SourceBody(payload, autoPublish)), HttpStatusCode.Created))
        .GetProperty("id").GetGuid();

    private static async Task<JsonElement> FetchAsync(HttpClient admin, Guid sourceId, DateTimeOffset? now = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/sources/{sourceId}/fetch");
        if (now is { } at) req.Headers.Add("X-Test-Now", at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        return await JsonAsync(await admin.SendAsync(req));
    }

    private async Task<List<Candidate>> CandidatesAsync(Guid sourceId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Candidates.AsNoTracking()
            .Where(c => c.SourceId == sourceId).OrderBy(c => c.ExternalId).ToListAsync();
    }

    private async Task<(Guid SourceId, JsonElement Run, List<Candidate> Candidates)> IngestAsync(HttpClient admin, params string[] items)
    {
        var sourceId = await CreateSourceAsync(admin, Payload(items));
        var run = await FetchAsync(admin, sourceId);
        return (sourceId, run, await CandidatesAsync(sourceId));
    }

    private static async Task ReplacePayloadAsync(HttpClient admin, Guid sourceId, string payload) =>
        await JsonAsync(await admin.PutAsJsonAsync($"/api/admin/sources/{sourceId}", SourceBody(payload, autoPublish: true)));

    [Fact]
    public async Task A_published_candidate_is_audited_as_the_system_and_the_item_has_its_source_link()
    {
        var admin = await LoginAdminAsync();
        var (sourceId, run, candidates) = await IngestAsync(admin, Event("a-" + Token(), $"Koncert {Token()} Prizren", DateTimeOffset.UtcNow.AddDays(1)));

        Assert.Equal(1, run.GetProperty("created").GetInt32());
        var c = Assert.Single(candidates);
        Assert.Equal(CandidateStatus.Published, c.Status);
        Assert.NotNull(c.PublishedItemId);

        var audit = await JsonAsync(await admin.GetAsync($"/api/admin/audit?entityId={c.Id}"));
        var entry = Assert.Single(audit.GetProperty("items").EnumerateArray());
        Assert.Equal("candidate.autoPublish", entry.GetProperty("action").GetString());
        Assert.Equal("system", entry.GetProperty("userEmail").GetString());
        Assert.Equal(Guid.Empty, entry.GetProperty("userId").GetGuid());
        Assert.Equal(sourceId, entry.GetProperty("changes").GetProperty("after").GetProperty("sourceId").GetGuid());
        Assert.Equal("candidate", entry.GetProperty("entityType").GetString());
        Assert.Equal("published", entry.GetProperty("changes").GetProperty("after").GetProperty("status").GetString());

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var link = await db.ItemSources.AsNoTracking().SingleAsync(i => i.ItemId == c.PublishedItemId);
        Assert.Equal(sourceId, link.SourceId);
        Assert.Equal(c.Id, link.CandidateId);
        var item = await db.Events.AsNoTracking().SingleAsync(e => e.Id == c.PublishedItemId);
        Assert.Equal(c.FetchedAt, item.LastVerifiedAt);
        Assert.Equal(sourceId, item.PrimarySourceId);
    }

    [Fact]
    public async Task A_candidate_that_needs_review_stays_pending_and_is_not_audited_as_published()
    {
        var admin = await LoginAdminAsync();
        var (_, _, candidates) = await IngestAsync(admin, Event("a-" + Token(), $"Koncert {Token()}", DateTimeOffset.UtcNow.AddDays(1), city: ""));

        var c = Assert.Single(candidates);
        Assert.Equal(ValidationOutcome.NeedsReview, c.Validation);
        Assert.Equal(CandidateStatus.Pending, c.Status);
        Assert.Null(c.PublishedItemId);
        var audit = await JsonAsync(await admin.GetAsync($"/api/admin/audit?entityId={c.Id}"));
        Assert.Empty(audit.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task A_possible_duplicate_of_a_published_item_stays_pending()
    {
        var admin = await LoginAdminAsync();
        var token = Token();
        var start = DateTimeOffset.UtcNow.AddDays(2);
        await IngestAsync(admin, Event("a-" + Token(), $"Festivali {token} i muzikes", start));

        var (_, _, second) = await IngestAsync(admin, Event("b-" + Token(), $"Festivali {token} i muzikes", start));

        var c = Assert.Single(second);
        Assert.Equal(CandidateStatus.Pending, c.Status);
        Assert.Contains(c.Issues, i => i.Code == IssueCodes.PossibleDuplicate);
    }

    [Fact]
    public async Task Events_of_one_feed_at_a_new_venue_and_a_repeated_listing_publish_one_item_each()
    {
        var admin = await LoginAdminAsync();
        var start = DateTimeOffset.UtcNow.AddDays(1);
        var venue = $"Salla {Token()}";
        var repeated = Event("r-" + Token(), $"Njeri {Token()} koncert", start, venue: venue);
        var other = Event("o-" + Token(), $"Tjetri {Token()} teatri", start.AddHours(3), venue: venue);

        var (_, run, candidates) = await IngestAsync(admin, repeated, other, repeated);

        Assert.Equal("succeeded", run.GetProperty("status").GetString());
        Assert.Equal(3, run.GetProperty("received").GetInt32());
        Assert.Equal(2, candidates.Count);
        Assert.All(candidates, c => Assert.Equal(CandidateStatus.Published, c.Status));
        Assert.Equal(2, candidates.Select(c => c.PublishedItemId).Distinct().Count());
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var normalized = TextNormalizer.Fold(venue);
        Assert.Equal(1, await db.Venues.CountAsync(v => v.NormalizedName == normalized));
    }

    [Fact]
    public async Task Two_similar_listings_in_one_run_publish_the_first_and_flag_the_second()
    {
        var admin = await LoginAdminAsync();
        var token = Token();
        var start = DateTimeOffset.UtcNow.AddDays(1);

        var (_, _, candidates) = await IngestAsync(admin,
            Event("a-" + Token(), $"Dita {token} e xhazit", start),
            Event("b-" + Token(), $"Dita {token} e xhazit", start));

        Assert.Equal(2, candidates.Count);
        Assert.Single(candidates, c => c.Status == CandidateStatus.Published);
        var flagged = Assert.Single(candidates, c => c.Status == CandidateStatus.Pending);
        Assert.Contains(flagged.Issues, i => i.Code == IssueCodes.PossibleDuplicate);
    }

    [Fact]
    public async Task A_changed_published_item_is_updated_in_place_and_audited_as_the_system()
    {
        var admin = await LoginAdminAsync();
        var id = "a-" + Token();
        var title = $"Koncert {Token()} Prizren";
        var start = DateTimeOffset.UtcNow.AddDays(1);
        var (sourceId, _, first) = await IngestAsync(admin, Event(id, title, start));
        var itemId = first.Single().PublishedItemId!.Value;

        var moved = start.AddHours(2);
        await ReplacePayloadAsync(admin, sourceId, Payload(Event(id, title, moved)));
        var run = await FetchAsync(admin, sourceId);

        Assert.Equal(1, run.GetProperty("created").GetInt32());
        var candidates = await CandidatesAsync(sourceId);
        Assert.Equal(2, candidates.Count);
        Assert.All(candidates, c => Assert.Equal(CandidateStatus.Published, c.Status));
        Assert.All(candidates, c => Assert.Equal(itemId, c.PublishedItemId));
        var latest = candidates.Single(c => c.MatchedItemId == itemId);
        var audit = await JsonAsync(await admin.GetAsync($"/api/admin/audit?entityId={latest.Id}"));
        Assert.Equal("candidate.autoPublish", Assert.Single(audit.GetProperty("items").EnumerateArray()).GetProperty("action").GetString());
        await using var scope = app.Services.CreateAsyncScope();
        var ev = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Events.AsNoTracking().SingleAsync(e => e.Id == itemId);
        Assert.Equal(moved.ToUnixTimeSeconds(), ev.StartsAt.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task A_change_that_also_needs_review_waits_for_a_person()
    {
        var admin = await LoginAdminAsync();
        var id = "a-" + Token();
        var title = $"Koncert {Token()} Prizren";
        var start = DateTimeOffset.UtcNow.AddDays(1);
        var (sourceId, _, first) = await IngestAsync(admin, Event(id, title, start));
        var itemId = first.Single().PublishedItemId!.Value;

        await ReplacePayloadAsync(admin, sourceId, Payload(Event(id, title, start, city: "Nowhereville")));
        await FetchAsync(admin, sourceId);

        var changed = (await CandidatesAsync(sourceId)).Single(c => c.MatchedItemId == itemId);
        Assert.Equal(CandidateStatus.Pending, changed.Status);
        Assert.Contains(changed.Issues, i => i.Code == IssueCodes.UnknownCity);
        Assert.Contains(changed.Issues, i => i.Code == IssueCodes.ChangedSinceApproval);
    }

    [Fact]
    public async Task Events_after_the_three_day_window_are_skipped_and_taken_by_a_later_fetch()
    {
        var admin = await LoginAdminAsync();
        var id = "a-" + Token();
        var title = $"Koncert {Token()} Prizren";
        var start = DateTimeOffset.UtcNow.AddDays(6);
        var sourceId = await CreateSourceAsync(admin, Payload(Event(id, title, start)));

        var early = await FetchAsync(admin, sourceId);
        Assert.Equal(1, early.GetProperty("received").GetInt32());
        Assert.Equal(0, early.GetProperty("created").GetInt32());
        Assert.Empty(await CandidatesAsync(sourceId));

        var run = await FetchAsync(admin, sourceId, DateTimeOffset.UtcNow.AddDays(4));
        Assert.Equal(1, run.GetProperty("created").GetInt32());
        Assert.Equal(CandidateStatus.Published, Assert.Single(await CandidatesAsync(sourceId)).Status);
    }

    [Fact]
    public async Task A_source_without_auto_publish_keeps_every_candidate_pending_and_ignores_the_horizon()
    {
        var admin = await LoginAdminAsync();
        var sourceId = await CreateSourceAsync(admin,
            Payload(Event("a-" + Token(), $"Koncert {Token()} Prizren", DateTimeOffset.UtcNow.AddDays(20))), autoPublish: false);

        var run = await FetchAsync(admin, sourceId);

        Assert.Equal(1, run.GetProperty("created").GetInt32());
        Assert.Equal(CandidateStatus.Pending, Assert.Single(await CandidatesAsync(sourceId)).Status);
    }

    [Fact]
    public async Task Only_a_daily_deal_with_at_least_35_percent_off_is_published()
    {
        var admin = await LoginAdminAsync();
        var now = DateTimeOffset.UtcNow;
        var big = Deal("d1-" + Token(), $"Oferta {Token()} madhe", 100m, 65m, now.AddHours(12));
        var small = Deal("d2-" + Token(), $"Oferta {Token()} vogel", 100m, 70m, now.AddHours(12));
        var longer = Deal("d3-" + Token(), $"Oferta {Token()} gjate", 100m, 40m, now.AddDays(5));

        var (_, _, candidates) = await IngestAsync(admin, big, small, longer);

        Assert.Equal(3, candidates.Count);
        var published = Assert.Single(candidates, c => c.Status == CandidateStatus.Published);
        Assert.Contains("madhe", published.Fields.Title);
        Assert.All(candidates.Where(c => c.Status == CandidateStatus.Pending), c => Assert.Equal(ValidationOutcome.Valid, c.Validation));
    }

    [Fact]
    public async Task A_candidate_that_cannot_be_published_stays_pending_and_the_run_still_succeeds()
    {
        var admin = await LoginAdminAsync();
        // Valid at ingestion, but the edit rules (title at most 300 characters) refuse it at approval.
        var longTitle = new string('x', 290) + " " + Token() + " yyyy";
        var good = Event("g-" + Token(), $"Koncert {Token()} Prizren", DateTimeOffset.UtcNow.AddDays(1));
        var bad = Event("b-" + Token(), longTitle + new string('z', 20), DateTimeOffset.UtcNow.AddDays(1));

        var (_, run, candidates) = await IngestAsync(admin, good, bad);

        Assert.Equal("succeeded", run.GetProperty("status").GetString());
        Assert.Equal(2, candidates.Count);
        Assert.Single(candidates, c => c.Status == CandidateStatus.Published);
        var pending = Assert.Single(candidates, c => c.Status == CandidateStatus.Pending);
        Assert.Null(pending.PublishedItemId);
        var audit = await JsonAsync(await admin.GetAsync($"/api/admin/audit?entityId={pending.Id}"));
        Assert.Empty(audit.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task A_changed_listing_that_appears_twice_in_one_feed_does_not_fail_the_run()
    {
        var admin = await LoginAdminAsync();
        var id = "a-" + Token();
        var title = $"Koncert {Token()} Prizren";
        var start = DateTimeOffset.UtcNow.AddDays(1);
        var (sourceId, _, first) = await IngestAsync(admin, Event(id, title, start));
        var itemId = first.Single().PublishedItemId!.Value;

        var moved = Event(id, title, start.AddHours(2));
        await ReplacePayloadAsync(admin, sourceId, Payload(moved, moved));
        var run = await FetchAsync(admin, sourceId);

        Assert.Equal("succeeded", run.GetProperty("status").GetString());
        Assert.Equal(2, run.GetProperty("received").GetInt32());
        await using var scope = app.Services.CreateAsyncScope();
        var ev = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Events.AsNoTracking().SingleAsync(e => e.Id == itemId);
        Assert.Equal(start.AddHours(2).ToUnixTimeSeconds(), ev.StartsAt.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task Content_a_person_rejected_comes_back_for_review_not_to_the_public_site_when_it_changes()
    {
        var admin = await LoginAdminAsync();
        var id = "a-" + Token();
        var title = $"Koncert {Token()} Prizren";
        var start = DateTimeOffset.UtcNow.AddDays(1);
        var (sourceId, _, first) = await IngestAsync(admin, Event(id, title, start, city: ""));
        var rejected = Assert.Single(first);
        Assert.Equal(CandidateStatus.Pending, rejected.Status);
        await JsonAsync(await admin.PostAsJsonAsync($"/api/admin/candidates/{rejected.Id}/reject", new { reason = "Spam" }));

        await ReplacePayloadAsync(admin, sourceId, Payload(Event(id, title, start)));
        var run = await FetchAsync(admin, sourceId);

        Assert.Equal(1, run.GetProperty("created").GetInt32());
        var again = (await CandidatesAsync(sourceId)).Single(c => c.Id != rejected.Id);
        Assert.Equal(ValidationOutcome.Valid, again.Validation);
        Assert.Equal(CandidateStatus.Pending, again.Status);
        Assert.Null(again.PublishedItemId);
    }

    [Fact]
    public async Task A_source_merged_into_another_sources_item_cannot_rewrite_it_automatically()
    {
        var admin = await LoginAdminAsync();
        var token = Token();
        var start = DateTimeOffset.UtcNow.AddDays(1);
        var title = $"Festivali {token} i madh";
        var (_, _, a) = await IngestAsync(admin, Event("a-" + Token(), title, start));
        var itemId = a.Single().PublishedItemId!.Value;

        var bId = "b-" + Token();
        var (sourceB, _, b) = await IngestAsync(admin, Event(bId, title, start));
        var duplicate = Assert.Single(b);
        Assert.Equal(CandidateStatus.Pending, duplicate.Status);
        await JsonAsync(await admin.PostAsJsonAsync($"/api/admin/candidates/{duplicate.Id}/merge", new { targetId = itemId }));

        await ReplacePayloadAsync(admin, sourceB, Payload(Event(bId, title, start.AddHours(1))));
        await FetchAsync(admin, sourceB);

        var changed = (await CandidatesAsync(sourceB)).Single(c => c.Id != duplicate.Id);
        Assert.Equal(CandidateStatus.Pending, changed.Status);
        Assert.Contains(changed.Issues, i => i.Code == IssueCodes.ChangedSinceApproval);
        await using var scope = app.Services.CreateAsyncScope();
        var ev = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Events.AsNoTracking().SingleAsync(e => e.Id == itemId);
        Assert.Equal(start.ToUnixTimeSeconds(), ev.StartsAt.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task One_run_publishes_at_most_the_configured_number_of_items()
    {
        await using var limited = app.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            services.RemoveAll<AutoPublishOptions>();
            services.AddSingleton(new AutoPublishOptions { MaxPerRun = 2 });
        }));
        var admin = limited.CreateClient();
        Assert.Equal(HttpStatusCode.OK,
            (await admin.PostAsJsonAsync("/api/auth/login", new { email = "admin@kosovaradar.local", password = "Radar-admin-dev-2026" })).StatusCode);
        var start = DateTimeOffset.UtcNow.AddDays(1);
        var items = Enumerable.Range(0, 4).Select(i => Event($"m{i}-" + Token(), $"Ngjarja {Token()} numer {i}", start)).ToArray();
        var sourceId = await CreateSourceAsync(admin, Payload(items));

        var run = await FetchAsync(admin, sourceId);

        Assert.Equal(4, run.GetProperty("created").GetInt32());
        var candidates = await CandidatesAsync(sourceId);
        Assert.Equal(2, candidates.Count(c => c.Status == CandidateStatus.Published));
        Assert.Equal(2, candidates.Count(c => c.Status == CandidateStatus.Pending));
    }

    [Fact]
    public async Task Known_sources_are_seeded_with_the_requested_flags_once()
    {
        const string prishtina = "https://prishtina.com/events", kapodalim = "https://kapodalim.com/ks";
        var moved = new List<(Guid Id, string Url)>();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Seeding is keyed by URL: move the sources other tests may have seeded out of the way, and put them back.
            foreach (var s in await db.Sources.Where(s => s.BaseUrl == prishtina || s.BaseUrl == kapodalim).ToListAsync())
            {
                moved.Add((s.Id, s.BaseUrl!));
                s.BaseUrl += "#moved-" + Token();
            }
            await db.SaveChangesAsync();
        }

        try
        {
            await using var scope = app.Services.CreateAsyncScope();
            var seeder = scope.ServiceProvider.GetRequiredService<KnownSourcesSeeder>();
            await seeder.SeedAsync(active: true, autoPublish: true);
            await seeder.SeedAsync(active: false, autoPublish: false); // existing rows are never changed

            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var seeded = await db.Sources.AsNoTracking().Where(s => s.BaseUrl == prishtina || s.BaseUrl == kapodalim).ToListAsync();
            Assert.Equal(2, seeded.Count);
            Assert.All(seeded, s =>
            {
                Assert.True(s.IsActive);
                Assert.True(s.AutoPublish);
                Assert.Equal(SourceType.Html, s.Type);
            });
        }
        finally
        {
            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Sources.Where(s => s.BaseUrl == prishtina || s.BaseUrl == kapodalim).ExecuteDeleteAsync();
            foreach (var (id, url) in moved)
                await db.Sources.Where(s => s.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.BaseUrl, url));
        }
    }

    [Fact]
    public async Task The_autoPublish_flag_round_trips_and_defaults_to_false_when_omitted()
    {
        var admin = await LoginAdminAsync();
        var id = await CreateSourceAsync(admin, Payload(), autoPublish: true);
        var detail = await JsonAsync(await admin.GetAsync($"/api/admin/sources/{id}"));
        Assert.True(detail.GetProperty("autoPublish").GetBoolean());

        var updated = await JsonAsync(await admin.PutAsJsonAsync($"/api/admin/sources/{id}", new
        {
            name = "Burim i ri",
            type = "fixture",
            baseUrl = (string?)null,
            fixturePayload = Payload(),
            defaultKind = "event",
            pollIntervalMinutes = 1440,
            isActive = false,
            isUnreliable = false,
        }));
        Assert.False(updated.GetProperty("autoPublish").GetBoolean());
    }
}
