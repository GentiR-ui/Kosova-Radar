using System.Text.Json;
using System.Text.Json.Nodes;

namespace App.AcceptanceTests;

[Collection(AcceptanceCollection.Name)]
public class DuplicateTests(AppFactory app)
{
    private static readonly DateOnly Day = Clock.Today.AddDays(30);

    private static void AssertDuplicateOf(JsonElement c, string kind, Guid id, bool isCandidate)
    {
        Assert.Contains("PossibleDuplicate", c.IssueCodes());
        Assert.Equal("needsReview", c.Str("validation"));
        var dup = c.GetProperty("duplicateOf");
        Assert.Equal(JsonValueKind.Object, dup.ValueKind);
        Assert.Equal(kind, dup.Str("kind"));
        Assert.Equal(id, dup.Id());
        Assert.Equal(isCandidate, dup.Bool("isCandidate"));
        Assert.False(string.IsNullOrEmpty(dup.Str("title")));
    }

    private static void AssertNoDuplicate(JsonElement c)
    {
        Assert.DoesNotContain("PossibleDuplicate", c.IssueCodes());
        Assert.True(c.IsNull("duplicateOf"));
    }

    // ---------- REQ-050 ----------

    [Fact, Trait("Req", "REQ-050")]
    public async Task Candidate_matching_a_published_event_of_another_source_is_a_possible_duplicate()
    {
        var admin = await Radar.AdminAsync(app);
        var title = $"Koncert {Radar.Token()} Rock Prizren Night";
        var published = await Radar.PublishAsync(admin, Radar.EventItem(title, Clock.At(Day, 20), "Prizren"));

        var c = await Radar.IngestOneAsync(admin, Radar.EventItem(title.ToUpperInvariant() + "!", Clock.At(Day, 18), "Prizren"));

        AssertDuplicateOf(c, "event", published.ItemId, isCandidate: false);
    }

    [Fact, Trait("Req", "REQ-050")]
    public async Task Candidate_matching_a_published_item_of_the_same_source_is_a_possible_duplicate()
    {
        var admin = await Radar.AdminAsync(app);
        var title = $"Koncert {Radar.Token()} Rock Prizren Night";
        var first = Radar.EventItem(title, Clock.At(Day, 20), "Prizren");
        var published = await Radar.PublishAsync(admin, first);

        var second = Radar.EventItem(title, Clock.At(Day, 21), "Prizren");
        await Radar.RefetchWithAsync(admin, published.SourceId, published.SourceBody, [first, second]);
        var c = (await Radar.CandidatesOfSourceAsync(admin, published.SourceId))
            .Single(x => x.Str("externalId") == second["externalId"]!.GetValue<string>());

        AssertDuplicateOf(c, "event", published.ItemId, isCandidate: false);
    }

    [Fact, Trait("Req", "REQ-050")]
    public async Task Candidate_matching_a_pending_candidate_of_another_source_is_a_possible_duplicate()
    {
        var admin = await Radar.AdminAsync(app);
        var title = $"Koncert {Radar.Token()} Rock Prizren Night";
        var pending = await Radar.IngestOneAsync(admin, Radar.EventItem(title, Clock.At(Day, 20), "Prizren"));
        Assert.Equal("pending", pending.Str("status"));

        var c = await Radar.IngestOneAsync(admin, Radar.EventItem(title, Clock.At(Day, 19), "Prizren"));

        AssertDuplicateOf(c, "event", pending.Id(), isCandidate: true);
    }

    [Fact, Trait("Req", "REQ-050")]
    public async Task Pending_candidates_of_the_same_source_are_not_duplicates_of_each_other()
    {
        var admin = await Radar.AdminAsync(app);
        var title = $"Koncert {Radar.Token()} Rock Prizren Night";
        var ing = await Radar.IngestAsync(admin,
            [Radar.EventItem(title, Clock.At(Day, 20), "Prizren"), Radar.EventItem(title, Clock.At(Day, 21), "Prizren")]);

        Assert.Equal(2, ing.Candidates.Count);
        foreach (var c in ing.Candidates) AssertNoDuplicate(c);
    }

    [Theory, Trait("Req", "REQ-050")]
    [InlineData("same", true)]
    [InlineData("similarity-0.8", true)]
    [InlineData("similarity-0.75", false)]
    [InlineData("other-city", false)]
    [InlineData("other-date", false)]
    [InlineData("pristina-midnight", false)]
    [InlineData("other-kind", false)]
    public async Task Duplicate_rules_kind_city_pristina_date_and_similarity(string variant, bool duplicate)
    {
        var admin = await Radar.AdminAsync(app);
        var g = Radar.Token();
        var titleA = variant == "similarity-0.75" ? $"Koncert {g} Rock Night" : $"Koncert {g} Rock Prizren Night";
        var startA = variant == "pristina-midnight" ? Clock.At(Day, 23, 30) : Clock.At(Day, 20);
        var published = await Radar.PublishAsync(admin, Radar.EventItem(titleA, startA, "Prizren"));

        var item = variant switch
        {
            "similarity-0.8" => Radar.EventItem($"Koncert {g} Rock Prizren", Clock.At(Day, 18), "Prizren"),   // 4/5
            "similarity-0.75" => Radar.EventItem($"Koncert {g} Rock", Clock.At(Day, 18), "Prizren"),           // 3/4
            "other-city" => Radar.EventItem(titleA, Clock.At(Day, 18), "Pejë"),
            "other-date" => Radar.EventItem(titleA, Clock.At(Day.AddDays(1), 20), "Prizren"),
            "pristina-midnight" => Radar.EventItem(titleA, Clock.At(Day.AddDays(1), 0, 30), "Prizren"),
            "other-kind" => Radar.DealItem(titleA, city: "Prizren"),
            _ => Radar.EventItem(titleA, Clock.At(Day, 10), "Prizren"),
        };
        var c = await Radar.IngestOneAsync(admin, item);

        if (duplicate) AssertDuplicateOf(c, "event", published.ItemId, isCandidate: false);
        else AssertNoDuplicate(c);
    }

    [Theory, Trait("Req", "REQ-050")]
    [InlineData(null, null, true)]
    [InlineData("Prizren", "Prizren", true)]
    [InlineData("Prizren", null, false)]
    [InlineData("Prizren", "Pejë", false)]
    public async Task Deal_duplicates_need_the_same_city_or_both_without_city(string? cityA, string? cityB, bool duplicate)
    {
        var admin = await Radar.AdminAsync(app);
        var title = $"Ofertë {Radar.Token()} Laptop Gaming Pro";
        var published = await Radar.PublishAsync(admin, Radar.DealItem(title, city: cityA));

        var c = await Radar.IngestOneAsync(admin, Radar.DealItem(title, city: cityB, validUntil: Clock.Future(20)));

        if (duplicate) AssertDuplicateOf(c, "deal", published.ItemId, isCandidate: false);
        else AssertNoDuplicate(c);
    }

    // ---------- REQ-051 ----------

    [Fact, Trait("Req", "REQ-051")]
    public async Task Changed_published_item_creates_a_linked_candidate_and_public_item_stays_unchanged()
    {
        var admin = await Radar.AdminAsync(app);
        var item = Radar.EventItem(Radar.Title(Radar.Token()));
        item["description"] = "Përshkrimi i parë";
        var published = await Radar.PublishAsync(admin, item);

        item["description"] = "Përshkrimi i ndryshuar";
        var run = await Radar.RefetchWithAsync(admin, published.SourceId, published.SourceBody, [item]);

        Assert.Equal(1, run.Int("created"));
        Assert.Equal(0, run.Int("updated"));
        var linked = (await Radar.CandidatesOfSourceAsync(admin, published.SourceId)).Single(c => c.Id() != published.CandidateId);
        Assert.Equal(published.ItemId, linked.Id("matchedItemId"));
        Assert.Contains("ChangedSinceApproval", linked.IssueCodes());
        Assert.Equal("needsReview", linked.Str("validation"));
        Assert.Equal("pending", linked.Str("status"));
        Assert.Equal("Përshkrimi i ndryshuar", linked.GetProperty("fields").Str("description"));

        var pub = await app.CreateClient().Get(published.PublicPath).Json();
        Assert.Equal("Përshkrimi i parë", pub.Str("description"));
        Assert.Equal("published", (await Radar.CandidateAsync(admin, published.CandidateId)).Str("status"));
    }

    // ---------- REQ-052 ----------

    [Fact, Trait("Req", "REQ-052")]
    public async Task Unchanged_published_item_gets_lastVerifiedAt_of_the_new_fetch()
    {
        var admin = await Radar.AdminAsync(app);
        var item = Radar.EventItem(Radar.Title(Radar.Token()), Clock.Future(10));
        var t0 = Clock.Now.AddDays(-3);
        var published = await Radar.PublishAsync(admin, item, t0);
        var anon = app.CreateClient();
        var before = await anon.Get(published.PublicPath).Json();
        Assert.Equal(t0, before.Time("lastVerifiedAt"));
        Assert.Equal("stale", before.Str("freshness"));

        var t1 = Clock.Now.AddMinutes(-30);
        var run = await Radar.FetchAsync(admin, published.SourceId, t1);

        Assert.Equal(1, run.Int("unchanged"));
        Assert.Equal(0, run.Int("created"));
        var after = await anon.Get(published.PublicPath).Json();
        Assert.Equal(t1, after.Time("lastVerifiedAt"));
        Assert.Equal("fresh", after.Str("freshness"));
        Assert.Single((await Radar.CandidatePageAsync(admin, published.SourceId)).Items());
    }

    // ---------- REQ-053 ----------

    [Fact, Trait("Req", "REQ-053")]
    public async Task Changed_pending_candidate_is_updated_in_place_and_revalidated()
    {
        var admin = await Radar.AdminAsync(app);
        var item = Radar.EventItem(Radar.Title(Radar.Token()));
        var ing = await Radar.IngestAsync(admin, [item]);
        var original = ing.Single;
        Assert.Equal("valid", original.Str("validation"));

        item.Remove("startsAt");
        item["description"] = "U shty, data do të njoftohet";
        var run = await Radar.RefetchWithAsync(admin, ing.SourceId, ing.SourceBody, [item]);

        Assert.Equal(0, run.Int("created"));
        Assert.Equal(1, run.Int("updated"));
        Assert.Equal(0, run.Int("unchanged"));
        var page = await Radar.CandidatePageAsync(admin, ing.SourceId);
        Assert.Equal(1, page.Int("totalCount"));
        var updated = await Radar.CandidateAsync(admin, original.Id());
        Assert.Equal("pending", updated.Str("status"));
        Assert.True(updated.GetProperty("fields").IsNull("startsAt"));
        Assert.Equal("U shty, data do të njoftohet", updated.GetProperty("fields").Str("description"));
        Assert.Contains("MissingStartsAt", updated.IssueCodes());
        Assert.Equal("needsReview", updated.Str("validation"));
    }
}
