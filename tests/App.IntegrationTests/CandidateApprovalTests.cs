using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Domain.Content;
using App.Domain.Reference;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace App.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public class CandidateApprovalTests(AppFactory app)
{
    private async Task<HttpClient> LoginAdminAsync()
    {
        var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@kosovaradar.local", password = "Radar-admin-dev-2026" })).StatusCode);
        return client;
    }

    private async Task<(Guid CandidateId, Guid SourceId)> SeedAsync(ItemKind kind, CandidateFields fields,
        CandidateStatus status = CandidateStatus.Pending, ValidationOutcome validation = ValidationOutcome.Valid)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var source = new App.Domain.Sources.Source { Name = $"Burim {Guid.NewGuid():N}"[..14], CreatedAt = DateTimeOffset.UtcNow };
        db.Sources.Add(source);
        var candidate = new Candidate
        {
            SourceId = source.Id,
            Kind = kind,
            SourceUrl = "https://example.test/x",
            Evidence = "{}",
            Fingerprint = Guid.NewGuid().ToString("N"),
            ContentHash = "h",
            FetchedAt = DateTimeOffset.UtcNow.AddHours(-2),
            Status = status,
            Validation = validation,
            Fields = fields,
            IsSynthetic = true,
        };
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();
        return (candidate.Id, source.Id);
    }

    private static CandidateFields EventFields(string? venue = "Kino Armata") => new()
    {
        Title = "Koncert",
        Description = "Natë e bukur",
        Url = "https://example.test/e",
        CitySlug = "prishtine",
        VenueName = venue,
        StartsAt = DateTimeOffset.UtcNow.AddDays(10),
        CategorySlug = "muzike",
    };

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    public async Task Approving_an_event_publishes_it_with_source_venue_and_audit_in_one_save()
    {
        var admin = await LoginAdminAsync();
        var (id, sourceId) = await SeedAsync(ItemKind.Event, EventFields());

        var res = await admin.PostAsync($"/api/admin/candidates/{id}/approve", null);
        var body = await JsonAsync(res);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("event", body.GetProperty("kind").GetString());
        var itemId = body.GetProperty("itemId").GetGuid();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ev = await db.Events.SingleAsync(e => e.Id == itemId);
        var candidate = await db.Candidates.SingleAsync(c => c.Id == id);
        Assert.Equal(candidate.FetchedAt, ev.LastVerifiedAt);
        Assert.Equal(sourceId, ev.PrimarySourceId);
        Assert.Equal("koncert nate e bukur kino armata", ev.SearchText);
        Assert.NotNull(ev.VenueId);
        Assert.NotNull(ev.CategoryId);
        Assert.Equal(CandidateStatus.Published, candidate.Status);
        Assert.Equal(itemId, candidate.PublishedItemId);
        Assert.Single(await db.ItemSources.Where(s => s.ItemId == itemId && s.CandidateId == id).ToListAsync());
        var audit = await JsonAsync(await admin.GetAsync($"/api/admin/audit?entityId={id}"));
        Assert.Contains(audit.GetProperty("items").EnumerateArray(), e => e.GetProperty("action").GetString() == "candidate.approve");
    }

    [Fact]
    public async Task The_same_venue_in_the_same_city_is_reused_ignoring_case_and_diacritics()
    {
        var admin = await LoginAdminAsync();
        var (a, _) = await SeedAsync(ItemKind.Event, EventFields("Teatri Kombëtar"));
        var (b, _) = await SeedAsync(ItemKind.Event, EventFields("TEATRI KOMBETAR"));

        var first = (await JsonAsync(await admin.PostAsync($"/api/admin/candidates/{a}/approve", null))).GetProperty("itemId").GetGuid();
        var second = (await JsonAsync(await admin.PostAsync($"/api/admin/candidates/{b}/approve", null))).GetProperty("itemId").GetGuid();

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(
            (await db.Events.SingleAsync(e => e.Id == first)).VenueId,
            (await db.Events.SingleAsync(e => e.Id == second)).VenueId);
    }

    [Fact]
    public async Task Approving_a_deal_computes_the_discount_and_allows_a_missing_city()
    {
        var admin = await LoginAdminAsync();
        var (id, _) = await SeedAsync(ItemKind.Deal, new CandidateFields
        {
            Title = "Ofertë",
            Url = "https://example.test/d",
            OldPrice = 80m,
            NewPrice = 60m,
            Currency = "EUR",
            CategorySlug = "teknologji",
        });

        var res = await admin.PostAsync($"/api/admin/candidates/{id}/approve", null);
        var itemId = (await JsonAsync(res)).GetProperty("itemId").GetGuid();

        await using var scope = app.Services.CreateAsyncScope();
        var deal = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Deals.SingleAsync(d => d.Id == itemId);
        Assert.Equal(25, deal.DiscountPercent);
        Assert.Null(deal.CityId);
    }

    [Fact]
    public async Task Times_with_a_local_offset_are_stored_as_the_same_instant_in_utc()
    {
        var admin = await LoginAdminAsync();
        var start = new DateTimeOffset(2030, 5, 1, 20, 0, 0, TimeSpan.FromHours(2));
        var (ev, _) = await SeedAsync(ItemKind.Event, EventFields() with { StartsAt = start, EndsAt = start.AddHours(2) });
        var (deal, _) = await SeedAsync(ItemKind.Deal, new CandidateFields
        {
            Title = "Ofertë",
            Url = "https://example.test/d",
            NewPrice = 5m,
            ValidUntil = start,
        });

        var eventId = (await JsonAsync(await admin.PostAsync($"/api/admin/candidates/{ev}/approve", null))).GetProperty("itemId").GetGuid();
        var dealId = (await JsonAsync(await admin.PostAsync($"/api/admin/candidates/{deal}/approve", null))).GetProperty("itemId").GetGuid();

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(start, (await db.Events.SingleAsync(e => e.Id == eventId)).StartsAt);
        Assert.Equal(start, (await db.Deals.SingleAsync(d => d.Id == dealId)).ValidUntil);
    }

    [Fact]
    public async Task An_ingested_title_longer_than_the_column_gets_400_and_publishes_nothing()
    {
        var admin = await LoginAdminAsync();
        var (id, _) = await SeedAsync(ItemKind.Event, EventFields() with { Title = new string('x', 301), Currency = "EURO" });

        var res = await admin.PostAsync($"/api/admin/candidates/{id}/approve", null);
        var errors = (await JsonAsync(res)).GetProperty("errors");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.True(errors.TryGetProperty("title", out _));
        Assert.True(errors.TryGetProperty("currency", out _));
        await using var scope = app.Services.CreateAsyncScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<AppDbContext>().ItemSources.CountAsync(s => s.CandidateId == id));
    }

    [Fact]
    public async Task Deal_detail_is_expired_after_valid_until_and_a_withdrawn_deal_is_404()
    {
        var admin = await LoginAdminAsync();
        var (id, _) = await SeedAsync(ItemKind.Deal, new CandidateFields
        {
            Title = "Ofertë",
            Url = "https://example.test/d",
            NewPrice = 5m,
            ValidUntil = DateTimeOffset.UtcNow.AddDays(1),
        });
        var itemId = (await JsonAsync(await admin.PostAsync($"/api/admin/candidates/{id}/approve", null))).GetProperty("itemId").GetGuid();
        var anonymous = app.CreateClient();

        Assert.Equal("active", (await JsonAsync(await anonymous.GetAsync($"/api/deals/{itemId}"))).GetProperty("status").GetString());
        var later = new HttpRequestMessage(HttpMethod.Get, $"/api/deals/{itemId}");
        later.Headers.Add("X-Test-Now", DateTime.UtcNow.AddDays(3).ToString("O"));
        var expired = await JsonAsync(await anonymous.SendAsync(later));
        Assert.Equal("expired", expired.GetProperty("status").GetString());
        Assert.Equal("stale", expired.GetProperty("freshness").GetString());

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Deals.SingleAsync(d => d.Id == itemId)).Status = ItemStatus.Withdrawn;
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/deals/{itemId}")).StatusCode);
    }

    [Fact]
    public async Task A_second_approve_gets_409_and_publishes_nothing_more()
    {
        var admin = await LoginAdminAsync();
        var (id, _) = await SeedAsync(ItemKind.Event, EventFields());
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/admin/candidates/{id}/approve", null)).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/admin/candidates/{id}/approve", null)).StatusCode);

        await using var scope = app.Services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AppDbContext>().ItemSources.CountAsync(s => s.CandidateId == id));
    }

    [Fact]
    public async Task A_rejected_validation_reports_validation_together_with_missing_fields_and_writes_nothing()
    {
        var admin = await LoginAdminAsync();
        var fields = EventFields() with { Title = null };
        var (id, _) = await SeedAsync(ItemKind.Event, fields, CandidateStatus.Rejected, ValidationOutcome.Rejected);

        var res = await admin.PostAsync($"/api/admin/candidates/{id}/approve", null);
        var errors = (await JsonAsync(res)).GetProperty("errors");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.True(errors.TryGetProperty("title", out _));
        Assert.True(errors.TryGetProperty("validation", out _));
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(CandidateStatus.Rejected, (await db.Candidates.SingleAsync(c => c.Id == id)).Status);
        Assert.Empty(await db.AuditLogs.Where(a => a.EntityId == id && a.Action == "candidate.approve").ToListAsync());
    }

    [Fact]
    public async Task An_event_with_an_unknown_city_slug_or_deal_without_price_gets_400()
    {
        var admin = await LoginAdminAsync();
        var (ev, _) = await SeedAsync(ItemKind.Event, EventFields() with { CitySlug = "nowhere" });
        var (deal, _) = await SeedAsync(ItemKind.Deal, new CandidateFields { Title = "T", Url = "https://example.test/d" });

        var e = await JsonAsync(await admin.PostAsync($"/api/admin/candidates/{ev}/approve", null));
        var d = await JsonAsync(await admin.PostAsync($"/api/admin/candidates/{deal}/approve", null));

        Assert.True(e.GetProperty("errors").TryGetProperty("citySlug", out _));
        Assert.True(d.GetProperty("errors").TryGetProperty("newPrice", out _));
    }

    [Fact]
    public async Task Unknown_malformed_and_anonymous_requests_are_404_and_401()
    {
        var admin = await LoginAdminAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync($"/api/admin/candidates/{Guid.NewGuid()}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync("/api/admin/candidates/nope/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.CreateClient().PostAsync($"/api/admin/candidates/{Guid.NewGuid()}/approve", null)).StatusCode);
    }

    [Fact]
    public async Task Public_detail_hides_withdrawn_items_and_lists_the_primary_source_first()
    {
        var admin = await LoginAdminAsync();
        var (id, _) = await SeedAsync(ItemKind.Event, EventFields());
        var itemId = (await JsonAsync(await admin.PostAsync($"/api/admin/candidates/{id}/approve", null))).GetProperty("itemId").GetGuid();
        var anonymous = app.CreateClient();

        var detail = await JsonAsync(await anonymous.GetAsync($"/api/events/{itemId}"));
        Assert.Equal("active", detail.GetProperty("status").GetString());
        Assert.Equal("Kino Armata", detail.GetProperty("venueName").GetString());
        Assert.Single(detail.GetProperty("sources").EnumerateArray());

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Events.SingleAsync(e => e.Id == itemId)).Status = ItemStatus.Withdrawn;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/events/{itemId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/events/not-a-guid")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/deals/{itemId}")).StatusCode);
    }
}
