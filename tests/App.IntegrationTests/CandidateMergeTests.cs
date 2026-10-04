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
public class CandidateMergeTests(AppFactory app)
{
    private async Task<HttpClient> LoginAdminAsync()
    {
        var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@kosovaradar.local", password = "Radar-admin-dev-2026" })).StatusCode);
        return client;
    }

    private async Task<Guid> SeedAsync(ItemKind kind = ItemKind.Event, DateTimeOffset? fetchedAt = null)
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
            FetchedAt = fetchedAt ?? DateTimeOffset.UtcNow,
            Status = CandidateStatus.Pending,
            Validation = ValidationOutcome.Valid,
            Fields = kind == ItemKind.Event
                ? new CandidateFields { Title = "Koncert", Url = "https://example.test/x", CitySlug = "prishtine", StartsAt = DateTimeOffset.UtcNow.AddDays(10) }
                : new CandidateFields { Title = "Ofertë", Url = "https://example.test/x", NewPrice = 5 },
        };
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();
        return candidate.Id;
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    private async Task<Guid> PublishAsync(HttpClient admin, ItemKind kind = ItemKind.Event)
    {
        var res = await admin.PostAsync($"/api/admin/candidates/{await SeedAsync(kind)}/approve", null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await JsonAsync(res)).GetProperty("itemId").GetGuid();
    }

    [Fact]
    public async Task Merge_adds_the_source_refreshes_lastVerifiedAt_audits_and_creates_no_item()
    {
        var admin = await LoginAdminAsync();
        var target = await PublishAsync(admin);
        var fetched = DateTimeOffset.UtcNow.AddHours(3);
        var id = await SeedAsync(fetchedAt: fetched);

        var res = await admin.PostAsJsonAsync($"/api/admin/candidates/{id}/merge", new { targetId = target });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("merged", (await JsonAsync(res)).GetProperty("status").GetString());
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var fingerprint = await db.Candidates.Where(c => c.Id == id).Select(c => c.Fingerprint).SingleAsync();
        Assert.Equal(2, await db.ItemSources.CountAsync(x => x.ItemId == target));
        Assert.Equal(fetched, (await db.Events.SingleAsync(x => x.Id == target)).LastVerifiedAt, TimeSpan.FromMilliseconds(1));
        Assert.False(await db.Events.AnyAsync(x => x.Fingerprint == fingerprint));
        var audit = await JsonAsync(await admin.GetAsync($"/api/admin/audit?entityId={id}"));
        Assert.Contains(audit.GetProperty("items").EnumerateArray(), e => e.GetProperty("action").GetString() == "candidate.merge");
    }

    [Fact]
    public async Task Merging_twice_gets_409()
    {
        var admin = await LoginAdminAsync();
        var target = await PublishAsync(admin);
        var id = await SeedAsync();
        await admin.PostAsJsonAsync($"/api/admin/candidates/{id}/merge", new { targetId = target });

        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/admin/candidates/{id}/merge", new { targetId = target })).StatusCode);
    }

    [Fact]
    public async Task A_deal_merges_into_a_deal_but_not_into_an_event()
    {
        var admin = await LoginAdminAsync();
        var ev = await PublishAsync(admin);
        var deal = await PublishAsync(admin, ItemKind.Deal);
        var id = await SeedAsync(ItemKind.Deal);

        var bad = await admin.PostAsJsonAsync($"/api/admin/candidates/{id}/merge", new { targetId = ev });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.True((await JsonAsync(bad)).GetProperty("errors").TryGetProperty("targetId", out _));
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/admin/candidates/{id}/merge", new { targetId = deal })).StatusCode);
    }

    [Fact]
    public async Task An_expired_target_is_refused_and_the_candidate_stays_pending()
    {
        var admin = await LoginAdminAsync();
        var target = await PublishAsync(admin);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Events.SingleAsync(x => x.Id == target)).Status = ItemStatus.Expired;
            await db.SaveChangesAsync();
        }
        var id = await SeedAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/admin/candidates/{id}/merge", new { targetId = target })).StatusCode);
        Assert.Equal("pending", (await JsonAsync(await admin.GetAsync($"/api/admin/candidates/{id}"))).GetProperty("status").GetString());
    }

    [Fact]
    public async Task An_older_candidate_keeps_the_newer_lastVerifiedAt_and_a_second_merge_changes_nothing()
    {
        var admin = await LoginAdminAsync();
        var target = await PublishAsync(admin);
        var id = await SeedAsync(fetchedAt: DateTimeOffset.UtcNow.AddDays(-2));
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var before = (await db.Events.AsNoTracking().SingleAsync(x => x.Id == target)).LastVerifiedAt;

        await admin.PostAsJsonAsync($"/api/admin/candidates/{id}/merge", new { targetId = target });
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/admin/candidates/{id}/merge", new { targetId = target })).StatusCode);

        Assert.Equal(before, (await db.Events.AsNoTracking().SingleAsync(x => x.Id == target)).LastVerifiedAt);
        Assert.Equal(2, await db.ItemSources.CountAsync(x => x.ItemId == target));
    }

    [Fact]
    public async Task A_matched_candidate_cannot_be_merged_into_another_item_and_a_bad_guid_is_400_on_targetId()
    {
        var admin = await LoginAdminAsync();
        var other = await PublishAsync(admin);
        var matchedItem = await PublishAsync(admin);
        var id = await SeedAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Candidates.SingleAsync(x => x.Id == id)).MatchedItemId = matchedItem;
        await db.SaveChangesAsync();

        var res = await admin.PostAsJsonAsync($"/api/admin/candidates/{id}/merge", new { targetId = other });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.True((await JsonAsync(res)).GetProperty("errors").TryGetProperty("targetId", out _));
        var bad = await admin.PostAsJsonAsync($"/api/admin/candidates/{id}/merge", new { targetId = "abc" });
        Assert.True((await JsonAsync(bad)).GetProperty("errors").TryGetProperty("targetId", out _));
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/admin/candidates/{id}/merge", new { targetId = matchedItem })).StatusCode);
    }

    [Fact]
    public async Task Unknown_ids_are_404_and_anonymous_is_401()
    {
        var admin = await LoginAdminAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync($"/api/admin/candidates/{Guid.NewGuid()}/merge", new { targetId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync("/api/admin/candidates/nope/merge", new { targetId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.CreateClient().PostAsJsonAsync($"/api/admin/candidates/{Guid.NewGuid()}/merge", new { targetId = Guid.NewGuid() })).StatusCode);
    }
}
