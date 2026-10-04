using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Domain.Content;
using App.Domain.Reference;
using App.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace App.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public class PublishedItemTests(AppFactory app)
{
    private async Task<HttpClient> LoginAdminAsync()
    {
        var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@kosovaradar.local", password = "Radar-admin-dev-2026" })).StatusCode);
        return client;
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage res) =>
        JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

    private async Task<Guid> PublishAsync(HttpClient admin, ItemKind kind, string title)
    {
        Guid candidateId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
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
                FetchedAt = DateTimeOffset.UtcNow,
                Status = CandidateStatus.Pending,
                Validation = ValidationOutcome.Valid,
                Fields = kind == ItemKind.Event
                    ? new CandidateFields { Title = title, Url = "https://example.test/x", CitySlug = "prishtine", StartsAt = DateTimeOffset.UtcNow.AddDays(10) }
                    : new CandidateFields { Title = title, Url = "https://example.test/x", NewPrice = 5 },
            };
            db.Candidates.Add(candidate);
            await db.SaveChangesAsync();
            candidateId = candidate.Id;
        }
        var res = await admin.PostAsync($"/api/admin/candidates/{candidateId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await JsonAsync(res)).GetProperty("itemId").GetGuid();
    }

    [Fact]
    public async Task Admin_endpoints_need_a_signed_in_user()
    {
        var anon = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/admin/items")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync($"/api/admin/events/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsync($"/api/admin/events/{Guid.NewGuid()}/withdraw", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PutAsJsonAsync($"/api/admin/deals/{Guid.NewGuid()}", new { })).StatusCode);
    }

    [Fact]
    public async Task Malformed_id_is_404_and_bad_filters_are_400()
    {
        var admin = await LoginAdminAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/admin/events/nope")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync("/api/admin/deals/nope/expire", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync("/api/admin/deals/nope", new { title = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/admin/items?status=bogus")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/admin/items?kind=bogus")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/admin/items?pageSize=1000")).StatusCode);
    }

    [Fact]
    public async Task List_filters_by_kind_status_and_search_and_pages_across_both_tables()
    {
        var admin = await LoginAdminAsync();
        var token = $"zq{Guid.NewGuid():N}"[..12];
        var e1 = await PublishAsync(admin, ItemKind.Event, $"Koncert {token} A");
        var e2 = await PublishAsync(admin, ItemKind.Event, $"Koncert {token} B");
        var d1 = await PublishAsync(admin, ItemKind.Deal, $"Oferte {token} C");
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/events/{e2}/withdraw", null)).StatusCode);

        var all = await JsonAsync(await admin.GetAsync($"/api/admin/items?search={token}"));
        Assert.Equal(3, all.GetProperty("totalCount").GetInt32());
        var deals = await JsonAsync(await admin.GetAsync($"/api/admin/items?search={token}&kind=deal"));
        Assert.Equal(d1, Assert.Single(deals.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        var withdrawn = await JsonAsync(await admin.GetAsync($"/api/admin/items?search={token}&status=withdrawn"));
        var row = Assert.Single(withdrawn.GetProperty("items").EnumerateArray());
        Assert.Equal(e2, row.GetProperty("id").GetGuid());
        Assert.Equal("withdrawn", row.GetProperty("adminStatus").GetString());
        Assert.Equal("event", row.GetProperty("kind").GetString());

        var seen = new List<Guid>();
        for (var page = 1; page <= 3; page++)
        {
            var p = await JsonAsync(await admin.GetAsync($"/api/admin/items?search={token}&page={page}&pageSize=1"));
            Assert.Equal(3, p.GetProperty("totalCount").GetInt32());
            seen.Add(Assert.Single(p.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        }
        Assert.Equal(new[] { e1, e2, d1 }.Order(), seen.Order());
    }

    [Fact]
    public async Task Withdrawn_items_cannot_be_edited_and_repeated_expire_writes_one_audit_entry()
    {
        var admin = await LoginAdminAsync();
        var id = await PublishAsync(admin, ItemKind.Event, "Koncert i vetem");
        for (var i = 0; i < 2; i++)
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/events/{id}/expire", null)).StatusCode);
        var audit = await JsonAsync(await admin.GetAsync($"/api/admin/audit?entityId={id}"));
        Assert.Single(audit.GetProperty("items").EnumerateArray(), e => e.GetProperty("action").GetString() == "item.expire");

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/events/{id}/withdraw", null)).StatusCode);
        var edit = await admin.PutAsJsonAsync($"/api/admin/events/{id}",
            new { title = "Pas terheqjes", url = "https://example.test/y", citySlug = "prishtine", startsAt = DateTimeOffset.UtcNow.AddDays(12) });
        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
        Assert.Equal("withdrawn", (await JsonAsync(await admin.GetAsync($"/api/admin/events/{id}"))).GetProperty("adminStatus").GetString());
    }

    [Fact]
    public async Task Edit_validates_references_and_an_id_of_the_other_kind_is_not_found()
    {
        var admin = await LoginAdminAsync();
        var id = await PublishAsync(admin, ItemKind.Event, "Koncert per redaktim");
        var bad = await admin.PutAsJsonAsync($"/api/admin/events/{id}",
            new { title = "x", url = "https://example.test/y", citySlug = "nuk-ekziston", categorySlug = "nuk-ekziston", startsAt = DateTimeOffset.UtcNow.AddDays(1) });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var errors = (await JsonAsync(bad)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("citySlug", out _));
        Assert.True(errors.TryGetProperty("categorySlug", out _));
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.PutAsJsonAsync($"/api/admin/deals/{id}", new { title = "x", url = "https://example.test/y", newPrice = 1 })).StatusCode);

        var ok = await admin.PutAsJsonAsync($"/api/admin/events/{id}", new
        {
            title = "Koncert i ri",
            url = "https://example.test/y",
            citySlug = "peje",
            venueName = "Salla e Re",
            startsAt = DateTimeOffset.UtcNow.AddDays(3),
        });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var body = await JsonAsync(ok);
        Assert.Equal("published", body.GetProperty("adminStatus").GetString());
        Assert.Equal("Salla e Re", body.GetProperty("venueName").GetString());
        var publicBody = await JsonAsync(await app.CreateClient().GetAsync($"/api/events/{id}"));
        Assert.False(publicBody.TryGetProperty("adminStatus", out _));
    }

    [Fact]
    public async Task A_huge_page_number_is_an_empty_page_not_an_error()
    {
        var admin = await LoginAdminAsync();
        var res = await admin.GetAsync("/api/admin/items?page=21474837&pageSize=100");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Empty((await JsonAsync(res)).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Editing_a_deal_updates_public_detail_search_and_audit()
    {
        var admin = await LoginAdminAsync();
        var id = await PublishAsync(admin, ItemKind.Deal, "Oferte per redaktim");
        var token = $"zq{Guid.NewGuid():N}"[..12];

        var res = await admin.PutAsJsonAsync($"/api/admin/deals/{id}",
            new { title = $"Oferta {token}", url = "https://example.test/y", oldPrice = 100, newPrice = 75, merchantName = "Dyqani" });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var pub = await JsonAsync(await app.CreateClient().GetAsync($"/api/deals/{id}"));
        Assert.Equal($"Oferta {token}", pub.GetProperty("title").GetString());
        Assert.Equal(25, pub.GetProperty("discountPercent").GetInt32());
        var found = await JsonAsync(await admin.GetAsync($"/api/admin/items?search={token}"));
        Assert.Equal(id, Assert.Single(found.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        var audit = await JsonAsync(await admin.GetAsync($"/api/admin/audit?entityId={id}"));
        var entry = Assert.Single(audit.GetProperty("items").EnumerateArray(), e => e.GetProperty("action").GetString() == "item.edit");
        Assert.Contains("Oferte per redaktim", entry.GetProperty("changes").GetProperty("before").GetRawText());
        Assert.Contains(token, entry.GetProperty("changes").GetProperty("after").GetRawText());
    }

    [Fact]
    public async Task Withdraw_is_idempotent_with_one_audit_entry_and_expire_afterwards_is_409()
    {
        var admin = await LoginAdminAsync();
        var id = await PublishAsync(admin, ItemKind.Deal, "Oferte per terheqje");
        for (var i = 0; i < 2; i++)
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/deals/{id}/withdraw", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/admin/deals/{id}/expire", null)).StatusCode);
        var audit = await JsonAsync(await admin.GetAsync($"/api/admin/audit?entityId={id}"));
        Assert.Single(audit.GetProperty("items").EnumerateArray(), e => e.GetProperty("action").GetString() == "item.withdraw");
    }
}
