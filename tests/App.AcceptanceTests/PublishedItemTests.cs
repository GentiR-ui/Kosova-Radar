using System.Net;
using System.Text.Json;

namespace App.AcceptanceTests;

[Collection(AcceptanceCollection.Name)]
public class PublishedItemTests(AppFactory app)
{
    private async Task<(HttpClient Admin, HttpClient Reviewer, TestUser User)> ClientsAsync()
    {
        var admin = await Radar.AdminAsync(app);
        var (reviewer, user) = await Radar.SignedInUserAsync(app, admin, "reviewer");
        return (admin, reviewer, user);
    }

    // ---------- REQ-069 ----------

    [Fact, Trait("Req", "REQ-069")]
    public async Task Editing_a_published_event_changes_what_the_public_api_returns()
    {
        var (admin, reviewer, _) = await ClientsAsync();
        var oldToken = Radar.Token();
        var published = await Radar.PublishAsync(admin, Radar.EventItem(Radar.Title(oldToken)));
        var current = await reviewer.Get(published.AdminPath).Json();
        Assert.Equal("published", current.Str("adminStatus"));

        var newToken = Radar.Token();
        var newTitle = $"Festival {newToken} Verë";
        var newStart = Clock.Future(18, 21);
        var body = Radar.EventEditRequest(current);
        body["title"] = newTitle;
        body["description"] = "Përshkrim i ri";
        body["startsAt"] = Clock.Utc(newStart);
        body["citySlug"] = "peje";
        await reviewer.Put(published.AdminPath, body).Expect(HttpStatusCode.OK);

        var anon = app.CreateClient();
        var ev = await anon.Get(published.PublicPath).Json();
        Assert.Equal(newTitle, ev.Str("title"));
        Assert.Equal("Përshkrim i ri", ev.Str("description"));
        Assert.Equal(newStart, ev.Time("startsAt"));
        Assert.Equal("peje", ev.GetProperty("city").Str("slug"));
        Assert.Contains(published.ItemId, await Radar.PublicIdsAsync(anon, $"/api/events?search={newToken}"));
        Assert.DoesNotContain(published.ItemId, await Radar.PublicIdsAsync(anon, $"/api/events?search={oldToken}"));
        Assert.Contains(published.ItemId, await Radar.PublicIdsAsync(anon, $"/api/events?search={newToken}&city=peje"));
    }

    [Fact, Trait("Req", "REQ-069")]
    public async Task Editing_a_published_deal_changes_what_the_public_api_returns()
    {
        var (admin, reviewer, _) = await ClientsAsync();
        var published = await Radar.PublishAsync(admin, Radar.DealItem(Radar.Title(Radar.Token(), "Ofertë"), 100m, 80m));
        var current = await reviewer.Get(published.AdminPath).Json();

        var body = Radar.DealEditRequest(current);
        body["newPrice"] = 60m;
        body["merchantName"] = "Dyqani i Ri";
        await reviewer.Put(published.AdminPath, body).Expect(HttpStatusCode.OK);

        var deal = await app.CreateClient().Get(published.PublicPath).Json();
        Assert.Equal(60m, deal.Dec("newPrice"));
        Assert.Equal(100m, deal.Dec("oldPrice"));
        Assert.Equal("Dyqani i Ri", deal.Str("merchantName"));
        Assert.Equal(40m, deal.Dec("discountPercent"));
    }

    [Theory, Trait("Req", "REQ-069")]
    [InlineData("startsAt")]
    [InlineData("citySlug")]
    [InlineData("title")]
    [InlineData("url")]
    public async Task Editing_a_published_event_without_a_required_field_gets_400_and_changes_nothing(string field)
    {
        var (admin, reviewer, _) = await ClientsAsync();
        var published = await Radar.PublishAsync(admin, Radar.EventItem(Radar.Title(Radar.Token())));
        var before = await app.CreateClient().Get(published.PublicPath).Json();

        var body = Radar.EventEditRequest(await reviewer.Get(published.AdminPath).Json());
        body[field] = null;
        await reviewer.Put(published.AdminPath, body).ExpectValidation(field);

        var after = await app.CreateClient().Get(published.PublicPath).Json();
        Assert.Equal(before.Str("title"), after.Str("title"));
        Assert.Equal(before.Time("startsAt"), after.Time("startsAt"));
    }

    [Fact, Trait("Req", "REQ-069")]
    public async Task Editing_an_unknown_item_gets_404()
    {
        var (admin, reviewer, _) = await ClientsAsync();
        var published = await Radar.PublishAsync(admin, Radar.EventItem(Radar.Title(Radar.Token())));
        var body = Radar.EventEditRequest(await reviewer.Get(published.AdminPath).Json());
        await reviewer.Put($"/api/admin/events/{Guid.NewGuid()}", body).Expect(HttpStatusCode.NotFound);
        await reviewer.Get($"/api/admin/deals/{published.ItemId}").Expect(HttpStatusCode.NotFound);
    }

    // ---------- REQ-070 ----------

    [Theory, Trait("Req", "REQ-070")]
    [InlineData("event")]
    [InlineData("deal")]
    public async Task Expiring_removes_the_item_from_lists_and_search_and_detail_shows_expired(string kind)
    {
        var (admin, reviewer, _) = await ClientsAsync();
        var g = Radar.Token();
        var item = kind == "event" ? Radar.EventItem(Radar.Title(g)) : Radar.DealItem(Radar.Title(g, "Ofertë"));
        var published = await Radar.PublishAsync(admin, item);
        var list = kind == "event" ? $"/api/events?search={g}" : $"/api/deals?search={g}";
        var anon = app.CreateClient();
        Assert.Contains(published.ItemId, await Radar.PublicIdsAsync(anon, list));

        await reviewer.Post($"{published.AdminPath}/expire").Expect(HttpStatusCode.NoContent);

        Assert.DoesNotContain(published.ItemId, await Radar.PublicIdsAsync(anon, list));
        Assert.DoesNotContain(published.ItemId, await Radar.PublicIdsAsync(anon, $"/api/search?q={g}"));
        Assert.Equal("expired", (await anon.Get(published.PublicPath).Json()).Str("status"));
        Assert.Equal("expired", (await reviewer.Get(published.AdminPath).Json()).Str("adminStatus"));

        // Idempotent.
        await reviewer.Post($"{published.AdminPath}/expire").Expect(HttpStatusCode.NoContent);
        var adminList = (await reviewer.Get($"/api/admin/items?kind={kind}&status=expired&search={g}").Json()).Items();
        Assert.Equal("expired", Assert.Single(adminList).Str("adminStatus"));
    }

    // ---------- REQ-071 ----------

    [Theory, Trait("Req", "REQ-071")]
    [InlineData("event")]
    [InlineData("deal")]
    public async Task Withdrawing_removes_the_item_from_lists_and_search_and_detail_is_404(string kind)
    {
        var (admin, reviewer, _) = await ClientsAsync();
        var g = Radar.Token();
        var item = kind == "event" ? Radar.EventItem(Radar.Title(g)) : Radar.DealItem(Radar.Title(g, "Ofertë"));
        var published = await Radar.PublishAsync(admin, item);
        var list = kind == "event" ? $"/api/events?search={g}" : $"/api/deals?search={g}";
        var anon = app.CreateClient();

        await reviewer.Post($"{published.AdminPath}/withdraw").Expect(HttpStatusCode.NoContent);

        Assert.DoesNotContain(published.ItemId, await Radar.PublicIdsAsync(anon, list));
        Assert.DoesNotContain(published.ItemId, await Radar.PublicIdsAsync(anon, $"/api/search?q={g}"));
        await anon.Get(published.PublicPath).Expect(HttpStatusCode.NotFound);
        Assert.Equal("withdrawn", (await reviewer.Get(published.AdminPath).Json()).Str("adminStatus"));

        await reviewer.Post($"{published.AdminPath}/withdraw").Expect(HttpStatusCode.NoContent);
        await reviewer.Post($"{published.AdminPath}/expire").Expect(HttpStatusCode.Conflict);
        await anon.Get(published.PublicPath).Expect(HttpStatusCode.NotFound);
    }

    [Fact, Trait("Req", "REQ-071")]
    public async Task Withdrawn_item_does_not_resurface_when_its_source_is_fetched_again()
    {
        var (admin, reviewer, _) = await ClientsAsync();
        var g = Radar.Token();
        var published = await Radar.PublishAsync(admin, Radar.EventItem(Radar.Title(g)));
        await reviewer.Post($"{published.AdminPath}/withdraw").Expect(HttpStatusCode.NoContent);

        var run = await Radar.FetchAsync(admin, published.SourceId);

        Assert.Equal(0, run.Int("created"));
        await app.CreateClient().Get(published.PublicPath).Expect(HttpStatusCode.NotFound);
        Assert.Empty(await Radar.PublicListAsync(app.CreateClient(), $"/api/events?search={g}"));
    }

    // ---------- REQ-072 ----------

    private static void AssertEntry(JsonElement entry, TestUser? by, string action, string entityType, Guid entityId)
    {
        Assert.Equal(action, entry.Str("action"));
        Assert.Equal(entityType, entry.Str("entityType"));
        Assert.Equal(entityId, entry.Id("entityId"));
        if (by is not null)
        {
            Assert.Equal(by.Id, entry.Id("userId"));
            Assert.Equal(by.Email, entry.Str("userEmail"), ignoreCase: true);
        }
        else Assert.Equal(Radar.AdminEmail, entry.Str("userEmail"), ignoreCase: true);
        var raw = entry.GetProperty("at").GetString()!;
        Assert.True(raw.EndsWith('Z') || raw.EndsWith("+00:00"), $"'at' must be UTC: {raw}");
        var at = entry.Time("at");
        Assert.InRange(at, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(1));
        var changes = entry.GetProperty("changes");
        Assert.Equal(JsonValueKind.Object, changes.ValueKind);
        Assert.True(changes.TryGetProperty("before", out _));
        Assert.True(changes.TryGetProperty("after", out _));
    }

    private static async Task<JsonElement> EntryAsync(HttpClient c, Guid entityId, string action)
    {
        var entries = await Radar.AuditAsync(c, entityId);
        Assert.All(entries, e => Assert.Equal(entityId, e.Id("entityId")));
        return Assert.Single(entries, e => e.Str("action") == action);
    }

    [Fact, Trait("Req", "REQ-072")]
    public async Task Candidate_actions_are_audited()
    {
        var (admin, reviewer, user) = await ClientsAsync();
        var ing = await Radar.IngestAsync(admin,
        [
            Radar.EventItem(Radar.Title(Radar.Token())), Radar.EventItem(Radar.Title(Radar.Token())),
            Radar.EventItem(Radar.Title(Radar.Token())), Radar.EventItem(Radar.Title(Radar.Token())),
        ]);
        var (approve, reject, edit, merge) = (ing.Candidates[0], ing.Candidates[1], ing.Candidates[2], ing.Candidates[3]);

        var itemId = await Radar.ApproveAsync(reviewer, approve.Id());
        await reviewer.Post($"/api/admin/candidates/{reject.Id()}/reject", new { reason = "Arsye auditi" }).Json();
        var newTitle = Radar.Title(Radar.Token());
        var body = Radar.FieldsRequest(edit);
        body["title"] = newTitle;
        await reviewer.Put($"/api/admin/candidates/{edit.Id()}", body).Json();
        await reviewer.Post($"/api/admin/candidates/{merge.Id()}/merge", new { targetId = itemId }).Json();

        AssertEntry(await EntryAsync(reviewer, approve.Id(), "candidate.approve"), user, "candidate.approve", "candidate", approve.Id());
        var r = await EntryAsync(reviewer, reject.Id(), "candidate.reject");
        AssertEntry(r, user, "candidate.reject", "candidate", reject.Id());
        Assert.Contains("Arsye auditi", r.GetProperty("changes").GetRawText());
        var e = await EntryAsync(reviewer, edit.Id(), "candidate.edit");
        AssertEntry(e, user, "candidate.edit", "candidate", edit.Id());
        Assert.Contains(newTitle, e.GetProperty("changes").GetProperty("after").GetRawText());
        Assert.Contains(edit.GetProperty("fields").Str("title")!, e.GetProperty("changes").GetProperty("before").GetRawText());
        AssertEntry(await EntryAsync(reviewer, merge.Id(), "candidate.merge"), user, "candidate.merge", "candidate", merge.Id());
    }

    [Fact, Trait("Req", "REQ-072")]
    public async Task Item_actions_are_audited()
    {
        var (admin, reviewer, user) = await ClientsAsync();
        var edited = await Radar.PublishAsync(admin, Radar.EventItem(Radar.Title(Radar.Token())));
        var expired = await Radar.PublishAsync(admin, Radar.DealItem(Radar.Title(Radar.Token(), "Ofertë")));
        var withdrawn = await Radar.PublishAsync(admin, Radar.EventItem(Radar.Title(Radar.Token())));

        var newTitle = Radar.Title(Radar.Token());
        var body = Radar.EventEditRequest(await reviewer.Get(edited.AdminPath).Json());
        body["title"] = newTitle;
        await reviewer.Put(edited.AdminPath, body).Expect(HttpStatusCode.OK);
        await reviewer.Post($"{expired.AdminPath}/expire").Expect(HttpStatusCode.NoContent);
        await reviewer.Post($"{withdrawn.AdminPath}/withdraw").Expect(HttpStatusCode.NoContent);

        var e = await EntryAsync(reviewer, edited.ItemId, "item.edit");
        AssertEntry(e, user, "item.edit", "event", edited.ItemId);
        Assert.Contains(newTitle, e.GetProperty("changes").GetProperty("after").GetRawText());
        AssertEntry(await EntryAsync(reviewer, expired.ItemId, "item.expire"), user, "item.expire", "deal", expired.ItemId);
        AssertEntry(await EntryAsync(reviewer, withdrawn.ItemId, "item.withdraw"), user, "item.withdraw", "event", withdrawn.ItemId);
    }

    [Fact, Trait("Req", "REQ-072")]
    public async Task Source_actions_are_audited()
    {
        var admin = await Radar.AdminAsync(app);
        var (adminB, userB) = await Radar.SignedInUserAsync(app, admin, "admin");
        var body = Radar.FixtureSource(Radar.Payload(Radar.EventItem(Radar.Title(Radar.Token()))));
        var source = await Radar.CreateSourceAsync(adminB, body);
        var oldName = body["name"]!.GetValue<string>();
        var newName = $"Riemërtuar {Radar.Token()}";
        body["name"] = newName;
        await Radar.UpdateSourceAsync(adminB, source.Id(), body);
        await Radar.FetchAsync(adminB, source.Id());

        var create = await EntryAsync(admin, source.Id(), "source.create");
        AssertEntry(create, userB, "source.create", "source", source.Id());
        Assert.Contains(oldName, create.GetProperty("changes").GetProperty("after").GetRawText());
        var update = await EntryAsync(admin, source.Id(), "source.update");
        AssertEntry(update, userB, "source.update", "source", source.Id());
        Assert.Contains(oldName, update.GetProperty("changes").GetProperty("before").GetRawText());
        Assert.Contains(newName, update.GetProperty("changes").GetProperty("after").GetRawText());
        AssertEntry(await EntryAsync(admin, source.Id(), "source.fetch"), userB, "source.fetch", "source", source.Id());
    }

    [Fact, Trait("Req", "REQ-072")]
    public async Task User_actions_are_audited_without_passwords()
    {
        var admin = await Radar.AdminAsync(app);
        var (adminB, userB) = await Radar.SignedInUserAsync(app, admin, "admin");
        var password = $"Secret-{Guid.NewGuid():N}";
        var created = await Radar.CreateUserAsync(adminB, "reviewer", password: password);
        await adminB.Post($"/api/admin/users/{created.Id}/deactivate").Expect(HttpStatusCode.NoContent);

        var create = await EntryAsync(admin, created.Id, "user.create");
        AssertEntry(create, userB, "user.create", "user", created.Id);
        Assert.Contains(created.Email, create.GetProperty("changes").GetRawText(), StringComparison.OrdinalIgnoreCase);
        var deactivate = await EntryAsync(admin, created.Id, "user.deactivate");
        AssertEntry(deactivate, userB, "user.deactivate", "user", created.Id);

        var all = await admin.Get($"/api/admin/audit?entityId={created.Id}").Json();
        Assert.DoesNotContain(password, all.GetRawText());
        Assert.DoesNotContain("password", all.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact, Trait("Req", "REQ-072")]
    public async Task Audit_is_filtered_by_entity_id_and_newest_first()
    {
        var admin = await Radar.AdminAsync(app);
        var body = Radar.FixtureSource(Radar.Payload());
        var source = await Radar.CreateSourceAsync(admin, body);
        body["isUnreliable"] = true;
        await Radar.UpdateSourceAsync(admin, source.Id(), body);

        var entries = await Radar.AuditAsync(admin, source.Id());

        Assert.Equal(new[] { "source.update", "source.create" }, entries.Select(e => e.Str("action")).ToArray());
        Assert.True(entries[0].Time("at") >= entries[1].Time("at"));
    }
}
