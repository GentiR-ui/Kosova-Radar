using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace App.AcceptanceTests;

[Collection(AcceptanceCollection.Name)]
public class ReviewTests(AppFactory app)
{
    private async Task<(HttpClient Admin, HttpClient Reviewer, TestUser User)> ClientsAsync()
    {
        var admin = await Radar.AdminAsync(app);
        var (reviewer, user) = await Radar.SignedInUserAsync(app, admin, "reviewer");
        return (admin, reviewer, user);
    }

    // ---------- REQ-060 ----------

    [Fact, Trait("Req", "REQ-060")]
    public async Task Reviewer_lists_candidates_filtered_by_status_validation_kind_and_source()
    {
        var (admin, reviewer, _) = await ClientsAsync();
        var g = Radar.Token();
        var validEvent = Radar.EventItem(Radar.Title(g));
        var dealNoPrice = Radar.DealItem(Radar.Title(g, "Ofertë"), oldPrice: null, newPrice: null);
        var noTitle = Radar.EventItem(Radar.Title(g, "Pa"));
        noTitle.Remove("title");
        var ing = await Radar.IngestAsync(admin, [validEvent, dealNoPrice, noTitle]);
        var src = ing.SourceId;

        async Task<List<JsonElement>> List(string q) => (await reviewer.Get($"/api/admin/candidates?sourceId={src}{q}").Json()).Items();

        var all = await reviewer.Get($"/api/admin/candidates?sourceId={src}").Json();
        Assert.Equal(3, all.Int("totalCount"));
        Assert.Equal(1, all.Int("page"));
        Assert.Equal(20, all.Int("pageSize"));
        foreach (var c in all.Items())
        {
            Assert.Equal(src, c.Id("sourceId"));
            Assert.Equal(ing.SourceName, c.Str("sourceName"));
            Assert.Equal(JsonValueKind.Array, c.GetProperty("issues").ValueKind);
            Assert.False(c.IsNull("fetchedAt"));
            Assert.True(c.Bool("isSynthetic"));
        }
        var deal = all.Items().Single(c => c.Str("kind") == "deal");
        Assert.Contains("MissingPrice", deal.IssueCodes());
        var rejected = all.Items().Single(c => c.IsNull("title"));
        Assert.Contains("MissingTitle", rejected.IssueCodes());

        Assert.Equal(validEvent["title"]!.GetValue<string>(), Assert.Single(await List("&validation=valid")).Str("title"));
        Assert.Equal("deal", Assert.Single(await List("&validation=needsReview")).Str("kind"));
        Assert.Single(await List("&validation=rejected"));
        Assert.Equal("deal", Assert.Single(await List("&kind=deal")).Str("kind"));
        Assert.Equal(2, (await List("&kind=event")).Count);
        Assert.Equal(2, (await List("&status=pending")).Count);
        Assert.Single(await List("&status=rejected"));
        Assert.Empty(await List("&status=published"));
        Assert.Single(await List("&status=pending&kind=event&validation=valid"));
    }

    [Fact, Trait("Req", "REQ-060")]
    public async Task Candidate_list_is_paginated()
    {
        var (admin, reviewer, _) = await ClientsAsync();
        var g = Radar.Token();
        var ing = await Radar.IngestAsync(admin, [Radar.EventItem(Radar.Title(g)), Radar.EventItem(Radar.Title(g, "Teatër")), Radar.EventItem(Radar.Title(g, "Sport"))]);

        var p1 = await reviewer.Get($"/api/admin/candidates?sourceId={ing.SourceId}&page=1&pageSize=2").Json();
        var p2 = await reviewer.Get($"/api/admin/candidates?sourceId={ing.SourceId}&page=2&pageSize=2").Json();
        var p3 = await reviewer.Get($"/api/admin/candidates?sourceId={ing.SourceId}&page=3&pageSize=2").Json();

        Assert.Equal(2, p1.Items().Count);
        Assert.Equal(3, p1.Int("totalCount"));
        Assert.Equal(2, p1.Int("pageSize"));
        Assert.Single(p2.Items());
        Assert.Equal(2, p2.Int("page"));
        Assert.Empty(p3.Items());
        Assert.Equal(3, p1.Items().Concat(p2.Items()).Select(c => c.Id()).Distinct().Count());
    }

    [Theory, Trait("Req", "REQ-060")]
    [InlineData("status=bogus", "status")]
    [InlineData("validation=maybe", "validation")]
    [InlineData("kind=concert", "kind")]
    [InlineData("pageSize=101", "pageSize")]
    [InlineData("page=0", "page")]
    public async Task Invalid_candidate_filters_get_400(string query, string field)
    {
        var (_, reviewer, _) = await ClientsAsync();
        await reviewer.Get($"/api/admin/candidates?{query}").ExpectValidation(field);
    }

    // ---------- REQ-061 ----------

    [Fact, Trait("Req", "REQ-061")]
    public async Task Reviewer_opens_a_candidate_with_fields_evidence_and_source()
    {
        var (admin, reviewer, _) = await ClientsAsync();
        var item = Radar.EventItem(Radar.Title(Radar.Token()));
        item["description"] = "Hyrja falas — mirë se vini!";
        item["venue"] = "Salla e Kuqe";
        var ing = await Radar.IngestAsync(admin, [item]);

        var c = await reviewer.Get($"/api/admin/candidates/{ing.Single.Id()}").Json();

        Assert.Equal(item.Raw(), c.Str("evidence"));
        Assert.Equal(ing.SourceName, c.Str("sourceName"));
        Assert.Equal(ing.SourceId, c.Id("sourceId"));
        Assert.Equal(item["url"]!.GetValue<string>(), c.Str("sourceUrl"));
        Assert.Equal(item["externalId"]!.GetValue<string>(), c.Str("externalId"));
        var f = c.GetProperty("fields");
        Assert.Equal(item["title"]!.GetValue<string>(), f.Str("title"));
        Assert.Equal("Hyrja falas — mirë se vini!", f.Str("description"));
        Assert.Equal("Salla e Kuqe", f.Str("venueName"));
        Assert.Equal(item["url"]!.GetValue<string>(), f.Str("url"));
        Assert.Equal("prishtine", f.Str("citySlug"));
        Assert.Equal(DateTimeOffset.Parse(item["startsAt"]!.GetValue<string>()), f.Time("startsAt"));
        Assert.Equal("event", c.Str("kind"));
        Assert.Equal(JsonValueKind.Array, c.GetProperty("issues").ValueKind);
    }

    [Theory, Trait("Req", "REQ-061")]
    [InlineData("00000000-0000-0000-0000-000000000042")]
    [InlineData("not-a-guid")]
    public async Task Unknown_or_malformed_candidate_id_gets_404(string id)
    {
        var (_, reviewer, _) = await ClientsAsync();
        await reviewer.Get($"/api/admin/candidates/{id}").Expect(HttpStatusCode.NotFound);
    }

    // ---------- REQ-062 ----------

    [Fact, Trait("Req", "REQ-062")]
    public async Task Approving_a_valid_event_publishes_it()
    {
        var (admin, reviewer, _) = await ClientsAsync();
        var g = Radar.Token();
        var item = Radar.EventItem(Radar.Title(g));
        var fetchedAt = Clock.Now.AddHours(-1);
        var ing = await Radar.IngestAsync(admin, [item], fetchedAt);

        var res = await reviewer.Post($"/api/admin/candidates/{ing.Single.Id()}/approve").Json();
        Assert.Equal("event", res.Str("kind"));
        var itemId = res.Id("itemId");

        var ev = await app.CreateClient().Get($"/api/events/{itemId}").Json();
        Assert.Equal(item["title"]!.GetValue<string>(), ev.Str("title"));
        Assert.Equal(fetchedAt, ev.Time("lastVerifiedAt"));
        Assert.Equal("active", ev.Str("status"));
        Assert.Equal("prishtine", ev.GetProperty("city").Str("slug"));
        Assert.Equal(ing.SourceName, ev.Str("sourceName"));
        Assert.Equal(item["url"]!.GetValue<string>(), ev.Str("sourceUrl"));
        Assert.Equal(DateTimeOffset.Parse(item["startsAt"]!.GetValue<string>()), ev.Time("startsAt"));

        Assert.Contains(itemId, await Radar.PublicIdsAsync(app.CreateClient(), $"/api/events?search={g}"));
        var c = await Radar.CandidateAsync(reviewer, ing.Single.Id());
        Assert.Equal("published", c.Str("status"));
        Assert.Equal(itemId, c.Id("publishedItemId"));
    }

    [Fact, Trait("Req", "REQ-062")]
    public async Task Approving_a_deal_with_only_a_price_text_publishes_it()
    {
        var (admin, reviewer, _) = await ClientsAsync();
        var g = Radar.Token();
        var item = Radar.DealItem(Radar.Title(g, "Ofertë"), oldPrice: null, newPrice: null);
        item["priceText"] = "2 për 1";
        var ing = await Radar.IngestAsync(admin, [item]);

        var res = await reviewer.Post($"/api/admin/candidates/{ing.Single.Id()}/approve").Json();
        Assert.Equal("deal", res.Str("kind"));

        var deal = await app.CreateClient().Get($"/api/deals/{res.Id("itemId")}").Json();
        Assert.Equal("2 për 1", deal.Str("priceText"));
        Assert.Equal("active", deal.Str("status"));
        Assert.Contains(res.Id("itemId"), await Radar.PublicIdsAsync(app.CreateClient(), $"/api/deals?search={g}"));
    }

    [Fact, Trait("Req", "REQ-062")]
    public async Task Approving_a_needs_review_candidate_with_its_required_fields_publishes_it()
    {
        var (admin, reviewer, _) = await ClientsAsync();
        var c = await Radar.IngestOneAsync(admin, Radar.EventItem(Radar.Title(Radar.Token())), isUnreliable: true);
        Assert.Equal("needsReview", c.Str("validation"));

        var res = await reviewer.Post($"/api/admin/candidates/{c.Id()}/approve").Json();
        await app.CreateClient().Get($"/api/events/{res.Id("itemId")}").Expect(HttpStatusCode.OK);
    }

    // ---------- REQ-063 ----------

    public static TheoryData<string, string[]> MissingRequired() => new()
    {
        { "event-no-start", ["startsAt"] },
        { "event-no-city", ["citySlug"] },
        { "event-no-start-no-city", ["startsAt", "citySlug"] },
        { "deal-no-price", ["newPrice"] },
        { "event-expired", ["validation"] },
        { "event-no-title", ["title"] },
    };

    [Theory, Trait("Req", "REQ-063")]
    [MemberData(nameof(MissingRequired))]
    public async Task Approving_without_required_fields_or_with_rejected_validation_gets_400_and_publishes_nothing(string variant, string[] fields)
    {
        var (admin, reviewer, _) = await ClientsAsync();
        var g = Radar.Token();
        var item = variant.StartsWith("deal") ? Radar.DealItem(Radar.Title(g, "Ofertë"), oldPrice: null, newPrice: null) : Radar.EventItem(Radar.Title(g));
        if (variant.Contains("no-start")) item.Remove("startsAt");
        if (variant.Contains("no-city")) item.Remove("city");
        if (variant == "event-expired") item["startsAt"] = Clock.Iso(Clock.Now.AddDays(-3));
        if (variant == "event-no-title") item.Remove("title");
        var c = await Radar.IngestOneAsync(admin, item);

        await reviewer.Post($"/api/admin/candidates/{c.Id()}/approve").ExpectValidation(fields);

        var after = await Radar.CandidateAsync(reviewer, c.Id());
        Assert.Equal(c.Str("status"), after.Str("status"));
        Assert.True(after.IsNull("publishedItemId"));
        if (!variant.Contains("no-title"))
            Assert.Empty((await app.CreateClient().Get($"/api/search?q={g}").Json()).Items());
    }

    // ---------- REQ-064 ----------

    [Fact, Trait("Req", "REQ-064")]
    public async Task Rejecting_with_a_reason_stores_reason_and_reviewer()
    {
        var (admin, reviewer, user) = await ClientsAsync();
        var c = await Radar.IngestOneAsync(admin, Radar.EventItem(Radar.Title(Radar.Token())));

        var res = await reviewer.Post($"/api/admin/candidates/{c.Id()}/reject", new { reason = "Nuk është në Kosovë" }).Json();

        Assert.Equal("rejected", res.Str("status"));
        Assert.Equal("Nuk është në Kosovë", res.Str("rejectionReason"));
        var after = await Radar.CandidateAsync(admin, c.Id());
        Assert.Equal("rejected", after.Str("status"));
        Assert.Equal("Nuk është në Kosovë", after.Str("rejectionReason"));
        var audit = (await Radar.AuditAsync(admin, c.Id())).Single(a => a.Str("action") == "candidate.reject");
        Assert.Equal(user.Id, audit.Id("userId"));
    }

    [Theory, Trait("Req", "REQ-064")]
    [InlineData(1)]
    [InlineData(500)]
    public async Task Reason_length_boundaries_are_accepted(int length)
    {
        var (admin, reviewer, _) = await ClientsAsync();
        var c = await Radar.IngestOneAsync(admin, Radar.EventItem(Radar.Title(Radar.Token())));
        var reason = new string('x', length);

        var res = await reviewer.Post($"/api/admin/candidates/{c.Id()}/reject", new { reason }).Json();

        Assert.Equal("rejected", res.Str("status"));
        Assert.Equal(reason, res.Str("rejectionReason"));
    }

    // ---------- REQ-065 ----------

    [Fact, Trait("Req", "REQ-065")]
    public async Task Editing_a_pending_candidate_saves_fields_keeps_evidence_and_revalidates()
    {
        var (admin, reviewer, _) = await ClientsAsync();
        var item = Radar.EventItem(Radar.Title(Radar.Token()));
        item.Remove("startsAt");
        item.Remove("city");
        var c = await Radar.IngestOneAsync(admin, item);
        Assert.Contains("MissingStartsAt", c.IssueCodes());
        Assert.Contains("UnknownCity", c.IssueCodes());

        var start = Clock.Future(9);
        var body = Radar.FieldsRequest(c);
        body["startsAt"] = Clock.Utc(start);
        body["citySlug"] = "prizren";
        body["venueName"] = "Kalaja";
        var res = await reviewer.Put($"/api/admin/candidates/{c.Id()}", body).Json();

        Assert.Equal("valid", res.Str("validation"));
        Assert.Equal("pending", res.Str("status"));
        Assert.DoesNotContain("MissingStartsAt", res.IssueCodes());
        Assert.DoesNotContain("UnknownCity", res.IssueCodes());
        var after = await Radar.CandidateAsync(reviewer, c.Id());
        Assert.Equal(start, after.GetProperty("fields").Time("startsAt"));
        Assert.Equal("prizren", after.GetProperty("fields").Str("citySlug"));
        Assert.Equal("Kalaja", after.GetProperty("fields").Str("venueName"));
        Assert.Equal(item.Raw(), after.Str("evidence"));
        Assert.Equal(c.Str("sourceUrl"), after.Str("sourceUrl"));
    }

    [Fact, Trait("Req", "REQ-065")]
    public async Task Editing_can_also_introduce_issues()
    {
        var (admin, reviewer, _) = await ClientsAsync();
        var c = await Radar.IngestOneAsync(admin, Radar.EventItem(Radar.Title(Radar.Token())));
        Assert.Equal("valid", c.Str("validation"));

        var body = Radar.FieldsRequest(c);
        body["startsAt"] = null;
        var res = await reviewer.Put($"/api/admin/candidates/{c.Id()}", body).Json();

        Assert.Contains("MissingStartsAt", res.IssueCodes());
        Assert.Equal("needsReview", res.Str("validation"));
    }

    [Fact, Trait("Req", "REQ-065")]
    public async Task Editing_an_auto_rejected_candidate_can_make_it_pending()
    {
        var (admin, reviewer, _) = await ClientsAsync();
        var item = Radar.EventItem(Radar.Title(Radar.Token()));
        item.Remove("title");
        var c = await Radar.IngestOneAsync(admin, item);
        Assert.Equal("rejected", c.Str("status"));

        var title = Radar.Title(Radar.Token());
        var body = Radar.FieldsRequest(c);
        body["title"] = title;
        var res = await reviewer.Put($"/api/admin/candidates/{c.Id()}", body).Json();

        Assert.Equal("pending", res.Str("status"));
        Assert.Equal("valid", res.Str("validation"));
        Assert.Equal(title, res.GetProperty("fields").Str("title"));
        Assert.Equal(item.Raw(), res.Str("evidence"));
    }

    [Theory, Trait("Req", "REQ-065")]
    [InlineData("title", "\"\"")]
    [InlineData("url", "\"ftp://example.com/x\"")]
    [InlineData("imageUrl", "\"not a url\"")]
    [InlineData("citySlug", "\"atlantis\"")]
    [InlineData("categorySlug", "\"no-such-category\"")]
    [InlineData("currency", "\"eur\"")]
    [InlineData("priceAmount", "-1")]
    [InlineData("priceAmount", "1000001")]
    public async Task Invalid_edits_get_400_on_the_field_and_change_nothing(string field, string json)
    {
        var (admin, reviewer, _) = await ClientsAsync();
        var c = await Radar.IngestOneAsync(admin, Radar.EventItem(Radar.Title(Radar.Token())));

        var body = Radar.FieldsRequest(c);
        body[field] = JsonNode.Parse(json);
        await reviewer.Put($"/api/admin/candidates/{c.Id()}", body).ExpectValidation(field);

        var after = await Radar.CandidateAsync(reviewer, c.Id());
        Assert.Equal(c.GetProperty("fields").GetRawText(), after.GetProperty("fields").GetRawText());
    }

    // ---------- REQ-066 ----------

    [Fact, Trait("Req", "REQ-066")]
    public async Task Merging_adds_the_source_to_the_published_item_and_creates_no_new_item()
    {
        var (admin, reviewer, _) = await ClientsAsync();
        var g = Radar.Token();
        var title = $"Koncert {g} Rock Prizren Night";
        var start = Clock.Future(12);
        var t0 = Clock.Now.AddDays(-3);
        var nameA = $"Burim A {g}";
        var published = await Radar.PublishAsync(admin, Radar.EventItem(title, start, "Prizren"), t0, nameA);

        var t1 = Clock.Now.AddHours(-1);
        var itemB = Radar.EventItem(title, start, "Prizren");
        var nameB = $"Burim B {g}";
        var ingB = await Radar.IngestAsync(admin, [itemB], t1, sourceName: nameB);
        Assert.Contains("PossibleDuplicate", ingB.Single.IssueCodes());

        var res = await reviewer.Post($"/api/admin/candidates/{ingB.Single.Id()}/merge", new { targetId = published.ItemId }).Json();

        Assert.Equal("merged", res.Str("status"));
        var ev = await app.CreateClient().Get(published.PublicPath).Json();
        var sources = ev.GetProperty("sources").Array();
        Assert.Equal(2, sources.Count);
        Assert.Equal(nameA, sources[0].Str("name"));
        Assert.Contains(sources, s => s.Str("name") == nameB && s.Str("url") == itemB["url"]!.GetValue<string>());
        Assert.Equal(t1, ev.Time("lastVerifiedAt"));
        Assert.Equal("fresh", ev.Str("freshness"));
        Assert.Equal(published.ItemId, Assert.Single(await Radar.PublicListAsync(app.CreateClient(), $"/api/events?search={g}")).Id());
    }

    [Fact, Trait("Req", "REQ-066")]
    public async Task Merging_an_older_candidate_keeps_the_newer_lastVerifiedAt()
    {
        var (admin, reviewer, _) = await ClientsAsync();
        var g = Radar.Token();
        var t0 = Clock.Now.AddHours(-2);
        var published = await Radar.PublishAsync(admin, Radar.EventItem($"Koncert {g} Jazz", Clock.Future(12)), t0);
        var older = await Radar.IngestOneAsync(admin, Radar.EventItem($"Koncert {g} Jazz Live", Clock.Future(12)), Clock.Now.AddDays(-1));

        await reviewer.Post($"/api/admin/candidates/{older.Id()}/merge", new { targetId = published.ItemId }).Json();

        Assert.Equal(t0, (await app.CreateClient().Get(published.PublicPath).Json()).Time("lastVerifiedAt"));
    }

    // ---------- REQ-067 ----------

    private async Task AssertAllActionsConflict(HttpClient c, JsonElement candidate, Guid validTarget)
    {
        var id = candidate.Id();
        var body = Radar.FieldsRequest(candidate);
        body["title"] = Radar.Title(Radar.Token());
        await c.Post($"/api/admin/candidates/{id}/approve").Expect(HttpStatusCode.Conflict);
        await c.Post($"/api/admin/candidates/{id}/reject", new { reason = "Përsëri" }).Expect(HttpStatusCode.Conflict);
        await c.Put($"/api/admin/candidates/{id}", body).Expect(HttpStatusCode.Conflict);
        await c.Post($"/api/admin/candidates/{id}/merge", new { targetId = validTarget }).Expect(HttpStatusCode.Conflict);

        var after = await Radar.CandidateAsync(c, id);
        Assert.Equal(candidate.Str("status"), after.Str("status"));
        Assert.Equal(candidate.Str("rejectionReason"), after.Str("rejectionReason"));
        Assert.Equal(candidate.GetProperty("fields").GetRawText(), after.GetProperty("fields").GetRawText());
    }

    [Fact, Trait("Req", "REQ-067")]
    public async Task Actions_on_a_published_candidate_get_409()
    {
        var (admin, reviewer, _) = await ClientsAsync();
        var target = await Radar.PublishAsync(admin, Radar.EventItem(Radar.Title(Radar.Token())));
        var published = await Radar.PublishAsync(admin, Radar.EventItem(Radar.Title(Radar.Token())));
        var candidate = await Radar.CandidateAsync(admin, published.CandidateId);
        Assert.Equal("published", candidate.Str("status"));

        await AssertAllActionsConflict(reviewer, candidate, target.ItemId);
    }

    [Fact, Trait("Req", "REQ-067")]
    public async Task Actions_on_a_merged_candidate_get_409()
    {
        var (admin, reviewer, _) = await ClientsAsync();
        var target = await Radar.PublishAsync(admin, Radar.EventItem(Radar.Title(Radar.Token())));
        var c = await Radar.IngestOneAsync(admin, Radar.EventItem(Radar.Title(Radar.Token())));
        await reviewer.Post($"/api/admin/candidates/{c.Id()}/merge", new { targetId = target.ItemId }).Json();
        var merged = await Radar.CandidateAsync(admin, c.Id());
        Assert.Equal("merged", merged.Str("status"));

        await AssertAllActionsConflict(reviewer, merged, target.ItemId);
    }

    [Fact, Trait("Req", "REQ-067")]
    public async Task Actions_on_a_candidate_rejected_by_a_person_get_409()
    {
        var (admin, reviewer, _) = await ClientsAsync();
        var target = await Radar.PublishAsync(admin, Radar.EventItem(Radar.Title(Radar.Token())));
        var c = await Radar.IngestOneAsync(admin, Radar.EventItem(Radar.Title(Radar.Token())));
        await reviewer.Post($"/api/admin/candidates/{c.Id()}/reject", new { reason = "Jo relevante" }).Json();
        var rejected = await Radar.CandidateAsync(admin, c.Id());

        await AssertAllActionsConflict(reviewer, rejected, target.ItemId);
        Assert.Empty(await Radar.PublicListAsync(app.CreateClient(), $"/api/events?search={c.GetProperty("fields").Str("title")!.Split(' ')[2]}"));
    }

    // ---------- REQ-068 ----------

    [Fact, Trait("Req", "REQ-068")]
    public async Task Approving_a_changed_since_approval_candidate_updates_the_same_item()
    {
        var (admin, reviewer, _) = await ClientsAsync();
        var g = Radar.Token();
        var item = Radar.EventItem(Radar.Title(g), Clock.Future(14));
        item["description"] = "Versioni i parë";
        var published = await Radar.PublishAsync(admin, item);

        var newStart = Clock.Future(15, 19);
        item["description"] = "Versioni i dytë";
        item["startsAt"] = Clock.Iso(newStart);
        await Radar.RefetchWithAsync(admin, published.SourceId, published.SourceBody, [item]);
        var linked = (await Radar.CandidatesOfSourceAsync(admin, published.SourceId)).Single(c => c.Id() != published.CandidateId);
        Assert.Contains("ChangedSinceApproval", linked.IssueCodes());

        var res = await reviewer.Post($"/api/admin/candidates/{linked.Id()}/approve").Json();

        Assert.Equal(published.ItemId, res.Id("itemId"));
        var ev = await app.CreateClient().Get(published.PublicPath).Json();
        Assert.Equal("Versioni i dytë", ev.Str("description"));
        Assert.Equal(newStart, ev.Time("startsAt"));
        Assert.Equal(published.ItemId, Assert.Single(await Radar.PublicListAsync(app.CreateClient(), $"/api/events?search={g}")).Id());
    }

    // ---------- REQ-074 ----------

    [Theory, Trait("Req", "REQ-074")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("501")]
    public async Task Reject_without_reason_or_with_a_too_long_reason_gets_400(string? reason)
    {
        var (admin, reviewer, _) = await ClientsAsync();
        var c = await Radar.IngestOneAsync(admin, Radar.EventItem(Radar.Title(Radar.Token())));
        object body = reason switch
        {
            null => new { },
            "501" => new { reason = new string('x', 501) },
            _ => new { reason },
        };

        await reviewer.Post($"/api/admin/candidates/{c.Id()}/reject", body).ExpectValidation("reason");

        var after = await Radar.CandidateAsync(reviewer, c.Id());
        Assert.Equal("pending", after.Str("status"));
        Assert.True(after.IsNull("rejectionReason"));
    }

    // ---------- REQ-075 ----------

    [Theory, Trait("Req", "REQ-075")]
    [InlineData("deal")]
    [InlineData("withdrawn")]
    [InlineData("unknown")]
    [InlineData("candidate")]
    [InlineData("missing")]
    public async Task Merge_into_an_invalid_target_gets_400_on_targetId(string target)
    {
        var (admin, reviewer, _) = await ClientsAsync();
        var c = await Radar.IngestOneAsync(admin, Radar.EventItem(Radar.Title(Radar.Token())));
        object body;
        switch (target)
        {
            case "deal":
                body = new { targetId = (await Radar.PublishAsync(admin, Radar.DealItem(Radar.Title(Radar.Token(), "Ofertë")))).ItemId };
                break;
            case "withdrawn":
                var ev = await Radar.PublishAsync(admin, Radar.EventItem(Radar.Title(Radar.Token())));
                await admin.Post($"/api/admin/events/{ev.ItemId}/withdraw").Expect(HttpStatusCode.NoContent);
                body = new { targetId = ev.ItemId };
                break;
            case "unknown":
                body = new { targetId = Guid.NewGuid() };
                break;
            case "candidate":
                body = new { targetId = (await Radar.IngestOneAsync(admin, Radar.EventItem(Radar.Title(Radar.Token())))).Id() };
                break;
            default:
                body = new { };
                break;
        }

        await reviewer.Post($"/api/admin/candidates/{c.Id()}/merge", body).ExpectValidation("targetId");

        var after = await Radar.CandidateAsync(reviewer, c.Id());
        Assert.Equal("pending", after.Str("status"));
    }
}
