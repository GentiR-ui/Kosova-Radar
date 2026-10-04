using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Domain.Content;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace App.IntegrationTests;

/// <summary>Lookup rows 2-4 of the ingestion pipeline and the duplicate/change issues surviving a reviewer edit (T028).</summary>
[Collection(IntegrationCollection.Name)]
public class DuplicateAndChangeTests(AppFactory app)
{
    private static string Token() => Guid.NewGuid().ToString("N")[..8];

    private static string Item(string id, string title, string description, DateTimeOffset start) =>
        $"{{\"externalId\":\"{id}\",\"kind\":\"event\",\"title\":\"{title}\",\"description\":\"{description}\",\"url\":\"https://example.test/{id}\"," +
        $"\"city\":\"Prizren\",\"startsAt\":\"{start:yyyy-MM-ddTHH:mm:ssZ}\"}}";

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

    private static object SourceBody(string payload) => new
    {
        name = $"Burim {Token()}",
        type = "fixture",
        baseUrl = (string?)null,
        fixturePayload = payload,
        defaultKind = "event",
        pollIntervalMinutes = 1440,
        isActive = false,
        isUnreliable = false,
    };

    private async Task<Guid> CreateSourceAsync(HttpClient admin, string payload) =>
        (await JsonAsync(await admin.PostAsJsonAsync("/api/admin/sources", SourceBody(payload)), HttpStatusCode.Created)).GetProperty("id").GetGuid();

    private async Task<JsonElement> FetchAsync(HttpClient admin, Guid sourceId) =>
        await JsonAsync(await admin.PostAsync($"/api/admin/sources/{sourceId}/fetch", null));

    private async Task<JsonElement> RefetchAsync(HttpClient admin, Guid sourceId, string payload)
    {
        await JsonAsync(await admin.PutAsJsonAsync($"/api/admin/sources/{sourceId}", SourceBody(payload)));
        return await FetchAsync(admin, sourceId);
    }

    private async Task<List<JsonElement>> CandidatesAsync(HttpClient admin, Guid sourceId)
    {
        var page = await JsonAsync(await admin.GetAsync($"/api/admin/candidates?sourceId={sourceId}&pageSize=100"));
        var list = new List<JsonElement>();
        foreach (var s in page.GetProperty("items").EnumerateArray())
            list.Add(await JsonAsync(await admin.GetAsync($"/api/admin/candidates/{s.GetProperty("id").GetGuid()}")));
        return list;
    }

    private static string[] Issues(JsonElement c) =>
        c.GetProperty("issues").EnumerateArray().Select(i => i.GetProperty("code").GetString()!).ToArray();

    private static object EditBody(string title, string description, DateTimeOffset start) => new
    {
        title,
        description,
        url = "https://example.test/edited",
        citySlug = "prizren",
        startsAt = start,
    };

    private static bool IsLinked(JsonElement c) => c.GetProperty("matchedItemId").ValueKind == JsonValueKind.String;

    private async Task WithdrawAsync(Guid eventId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Events.Where(e => e.Id == eventId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, ItemStatus.Withdrawn));
    }

    /// <summary>Publishes one event from a new fixture source.</summary>
    private async Task<(Guid SourceId, Guid ItemId, string Title, string ExternalId, DateTimeOffset Start)> PublishAsync(HttpClient admin)
    {
        var title = $"Koncert {Token()} Rock Prizren Night";
        var id = "ev-" + Token();
        var start = DateTimeOffset.UtcNow.AddDays(20);
        var sourceId = await CreateSourceAsync(admin, Payload(Item(id, title, "i pari", start)));
        await FetchAsync(admin, sourceId);
        var candidate = (await CandidatesAsync(admin, sourceId)).Single();
        var approved = await JsonAsync(await admin.PostAsync($"/api/admin/candidates/{candidate.GetProperty("id").GetGuid()}/approve", null));
        return (sourceId, approved.GetProperty("itemId").GetGuid(), title, id, start);
    }

    [Fact]
    public async Task Withdrawn_item_never_resurfaces_even_when_the_feed_changes()
    {
        var admin = await LoginAdminAsync();
        var p = await PublishAsync(admin);
        await WithdrawAsync(p.ItemId);

        var run = await RefetchAsync(admin, p.SourceId, Payload(Item(p.ExternalId, p.Title, "i ndryshuar", p.Start)));

        Assert.Equal(0, run.GetProperty("created").GetInt32());
        Assert.Equal(1, run.GetProperty("unchanged").GetInt32());
        Assert.Single(await CandidatesAsync(admin, p.SourceId));
    }

    [Fact]
    public async Task Person_rejected_content_stays_rejected_but_changed_content_comes_back_as_a_new_candidate()
    {
        var admin = await LoginAdminAsync();
        var id = "ev-" + Token();
        var title = $"Koncert {Token()} Jazz Night";
        var start = DateTimeOffset.UtcNow.AddDays(20);
        var sourceId = await CreateSourceAsync(admin, Payload(Item(id, title, "i pari", start)));
        await FetchAsync(admin, sourceId);
        var first = (await CandidatesAsync(admin, sourceId)).Single();
        await JsonAsync(await admin.PostAsJsonAsync($"/api/admin/candidates/{first.GetProperty("id").GetGuid()}/reject", new { reason = "Spam" }));

        var same = await RefetchAsync(admin, sourceId, Payload(Item(id, title, "i pari", start)));
        var changed = await RefetchAsync(admin, sourceId, Payload(Item(id, title, "i dytë", start)));

        Assert.Equal(1, same.GetProperty("unchanged").GetInt32());
        Assert.Equal(0, same.GetProperty("created").GetInt32());
        Assert.Equal(1, changed.GetProperty("created").GetInt32());
        var all = await CandidatesAsync(admin, sourceId);
        Assert.Equal(2, all.Count);
        Assert.All(all, c => Assert.False(IsLinked(c)));
    }

    [Fact]
    public async Task Editing_a_changed_since_approval_candidate_keeps_the_link_and_the_issue()
    {
        var admin = await LoginAdminAsync();
        var p = await PublishAsync(admin);
        await RefetchAsync(admin, p.SourceId, Payload(Item(p.ExternalId, p.Title, "i ndryshuar", p.Start)));
        var linked = (await CandidatesAsync(admin, p.SourceId)).Single(IsLinked);

        var edited = await JsonAsync(await admin.PutAsJsonAsync($"/api/admin/candidates/{linked.GetProperty("id").GetGuid()}",
            EditBody(p.Title, "redaktuar", p.Start)));

        Assert.Contains("ChangedSinceApproval", Issues(edited));
        Assert.Equal(p.ItemId, edited.GetProperty("matchedItemId").GetGuid());
        Assert.Equal("needsReview", edited.GetProperty("validation").GetString());
    }

    [Fact]
    public async Task Editing_a_possible_duplicate_keeps_the_issue_until_the_title_stops_matching()
    {
        var admin = await LoginAdminAsync();
        var p = await PublishAsync(admin);
        var other = await CreateSourceAsync(admin, Payload(Item("dup-" + Token(), p.Title, "kopje", p.Start)));
        await FetchAsync(admin, other);
        var dup = (await CandidatesAsync(admin, other)).Single();
        Assert.Contains("PossibleDuplicate", Issues(dup));
        var id = dup.GetProperty("id").GetGuid();

        var kept = await JsonAsync(await admin.PutAsJsonAsync($"/api/admin/candidates/{id}", EditBody(p.Title, "tjetër", p.Start)));
        var renamed = await JsonAsync(await admin.PutAsJsonAsync($"/api/admin/candidates/{id}", EditBody($"Festival {Token()} Verës", "kopje", p.Start)));

        Assert.Contains("PossibleDuplicate", Issues(kept));
        Assert.Equal(p.ItemId, kept.GetProperty("duplicateOf").GetProperty("id").GetGuid());
        Assert.DoesNotContain("PossibleDuplicate", Issues(renamed));
        Assert.Equal(JsonValueKind.Null, renamed.GetProperty("duplicateOf").ValueKind);
    }

    [Fact]
    public async Task Approving_a_changed_candidate_whose_item_was_withdrawn_is_refused()
    {
        var admin = await LoginAdminAsync();
        var p = await PublishAsync(admin);
        await RefetchAsync(admin, p.SourceId, Payload(Item(p.ExternalId, p.Title, "i ndryshuar", p.Start)));
        var linked = (await CandidatesAsync(admin, p.SourceId)).Single(IsLinked);
        await WithdrawAsync(p.ItemId);

        var res = await admin.PostAsync($"/api/admin/candidates/{linked.GetProperty("id").GetGuid()}/approve", null);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task A_feed_item_that_changes_kind_is_a_new_unlinked_candidate()
    {
        var admin = await LoginAdminAsync();
        var p = await PublishAsync(admin);
        var deal = $"{{\"externalId\":\"{p.ExternalId}\",\"kind\":\"deal\",\"title\":\"{p.Title}\",\"url\":\"https://example.test/{p.ExternalId}\"," +
                   $"\"city\":\"Prizren\",\"oldPrice\":25,\"newPrice\":20,\"currency\":\"EUR\",\"validUntil\":\"{p.Start:yyyy-MM-ddTHH:mm:ssZ}\"}}";

        var run = await RefetchAsync(admin, p.SourceId, Payload(deal));

        Assert.Equal(1, run.GetProperty("created").GetInt32());
        Assert.DoesNotContain((await CandidatesAsync(admin, p.SourceId)).Where(c => c.GetProperty("kind").GetString() == "deal"), IsLinked);
    }

    [Fact]
    public async Task Approving_a_changed_candidate_updates_the_item_in_place_without_a_second_item_source()
    {
        var admin = await LoginAdminAsync();
        var p = await PublishAsync(admin);
        await RefetchAsync(admin, p.SourceId, Payload(Item(p.ExternalId, p.Title, "i ndryshuar", p.Start)));
        var linked = (await CandidatesAsync(admin, p.SourceId)).Single(IsLinked);

        var res = await JsonAsync(await admin.PostAsync($"/api/admin/candidates/{linked.GetProperty("id").GetGuid()}/approve", null));

        Assert.Equal(p.ItemId, res.GetProperty("itemId").GetGuid());
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.ItemSources.CountAsync(s => s.ItemId == p.ItemId));
        Assert.Equal(1, await db.Events.CountAsync(e => e.PrimarySourceId == p.SourceId));
        Assert.Equal(linked.GetProperty("id").GetGuid(), (await db.ItemSources.SingleAsync(s => s.ItemId == p.ItemId)).CandidateId);
    }
}
