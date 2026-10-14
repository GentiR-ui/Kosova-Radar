using System.Globalization;
using System.Text.Json.Nodes;

namespace App.AcceptanceTests;

[Collection(AcceptanceCollection.Name)]
public class AutoPublishTests(AppFactory app)
{
    private static string Ext(JsonObject item) => item["externalId"]!.GetValue<string>();

    private static string Day(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Creates an inactive fixture source with auto-publish enabled, fetches it once and returns the candidates.</summary>
    private static async Task<Ingested> IngestAutoAsync(HttpClient admin, params JsonObject[] items)
    {
        var body = Radar.FixtureSource(Radar.Payload(items));
        body["autoPublish"] = true;
        var source = await Radar.CreateSourceAsync(admin, body);
        Assert.True(source.Bool("autoPublish"));
        var run = await Radar.FetchAsync(admin, source.Id());
        Assert.Equal("succeeded", run.Str("status"));
        var candidates = await Radar.CandidatesOfSourceAsync(admin, source.Id());
        return new Ingested(source.Id(), source.Str("name")!, body, run, candidates);
    }

    private static JsonObject DateOnlyEvent(string title, DateOnly day)
    {
        var item = Radar.EventItem(title);
        item.Remove("startsAt");
        item["startDate"] = Day(day);
        return item;
    }

    // ---------- REQ-023 ----------

    [Fact, Trait("Req", "REQ-023")]
    public async Task Sources_do_not_auto_publish_unless_requested()
    {
        var admin = await Radar.AdminAsync(app);
        var source = await Radar.CreateSourceAsync(admin, Radar.FixtureSource(Radar.Payload()));
        Assert.False(source.Bool("autoPublish"));
        Assert.False((await Radar.SourceAsync(admin, source.Id())).Bool("autoPublish"));
    }

    [Fact, Trait("Req", "REQ-023")]
    public async Task Valid_candidate_of_an_auto_publish_source_is_published_without_approval()
    {
        var admin = await Radar.AdminAsync(app);
        var g = Radar.Token();
        var valid = Radar.EventItem(Radar.Title(g), Clock.Future(2));
        var noCity = Radar.EventItem(Radar.Title(g), Clock.Future(1));
        noCity.Remove("city");

        var ing = await IngestAutoAsync(admin, valid, noCity);

        var published = ing.Candidate(Ext(valid));
        Assert.Equal("valid", published.Str("validation"));
        Assert.Equal("published", published.Str("status"));
        var review = ing.Candidate(Ext(noCity));
        Assert.Equal("needsReview", review.Str("validation"));
        Assert.Equal("pending", review.Str("status"));

        var page = await app.CreateClient().Get($"/api/events?search={g}").Json();
        var only = Assert.Single(page.Items());
        Assert.Equal(valid["title"]!.GetValue<string>(), only.Str("title"));
    }

    [Fact, Trait("Req", "REQ-023")]
    public async Task Candidates_of_a_source_without_auto_publish_stay_pending()
    {
        var admin = await Radar.AdminAsync(app);
        var g = Radar.Token();
        var c = await Radar.IngestOneAsync(admin, Radar.EventItem(Radar.Title(g)));

        Assert.Equal("valid", c.Str("validation"));
        Assert.Equal("pending", c.Str("status"));
        Assert.Empty((await app.CreateClient().Get($"/api/events?search={g}").Json()).Items());
    }

    // ---------- REQ-024 ----------

    [Fact, Trait("Req", "REQ-024")]
    public async Task Changed_item_of_an_auto_publish_source_updates_the_same_public_item()
    {
        var admin = await Radar.AdminAsync(app);
        var g = Radar.Token();
        var item = Radar.EventItem(Radar.Title(g), Clock.Future(2));
        var ing = await IngestAutoAsync(admin, item);
        Assert.Equal("published", ing.Single.Str("status"));
        var anon = app.CreateClient();
        var before = Assert.Single((await anon.Get($"/api/events?search={g}").Json()).Items());

        var moved = Clock.Future(3);
        item["startsAt"] = Clock.Iso(moved);
        var run = await Radar.RefetchWithAsync(admin, ing.SourceId, ing.SourceBody, [item]);
        Assert.Equal("succeeded", run.Str("status"));

        var after = Assert.Single((await anon.Get($"/api/events?search={g}").Json()).Items());
        Assert.Equal(before.Id(), after.Id());
        Assert.Equal(moved, after.Time("startsAt"));
    }

    // ---------- REQ-026 ----------

    [Fact, Trait("Req", "REQ-026")]
    public async Task Auto_publish_source_skips_events_more_than_three_days_ahead()
    {
        var admin = await Radar.AdminAsync(app);
        var g = Radar.Token();
        var soon = Radar.EventItem(Radar.Title(g), Clock.Future(3));
        var later = Radar.EventItem(Radar.Title(g), Clock.Future(4));

        var ing = await IngestAutoAsync(admin, soon, later);

        Assert.Equal(2, ing.Run.Int("received"));
        var only = Assert.Single(ing.Candidates);
        Assert.Equal(Ext(soon), only.Str("externalId"));
        Assert.Equal("published", only.Str("status"));
        var ids = await Radar.PublicIdsAsync(app.CreateClient(), $"/api/events?search={g}");
        Assert.Single(ids);
    }

    // ---------- REQ-044 ----------

    [Fact, Trait("Req", "REQ-044")]
    public async Task Auto_publish_source_publishes_only_daily_deals_with_at_least_35_percent_off()
    {
        var admin = await Radar.AdminAsync(app);
        var g = Radar.Token();
        var dailyBig = Radar.DealItem(Radar.Title(g, "Ofertë"), oldPrice: 100m, newPrice: 65m, validUntil: Clock.Now.AddHours(12));
        var dailySmall = Radar.DealItem(Radar.Title(g, "Ofertë"), oldPrice: 25m, newPrice: 20m, validUntil: Clock.Now.AddHours(12));
        var longBig = Radar.DealItem(Radar.Title(g, "Ofertë"), oldPrice: 100m, newPrice: 50m, validUntil: Clock.Future(10));

        var ing = await IngestAutoAsync(admin, dailyBig, dailySmall, longBig);

        Assert.Equal("published", ing.Candidate(Ext(dailyBig)).Str("status"));
        foreach (var item in new[] { dailySmall, longBig })
        {
            var c = ing.Candidate(Ext(item));
            Assert.Equal("valid", c.Str("validation"));
            Assert.Equal("pending", c.Str("status"));
        }
        var ids = await Radar.PublicIdsAsync(app.CreateClient(), $"/api/deals?search={g}");
        Assert.Single(ids);
    }

    // ---------- REQ-045 ----------

    private static JsonObject ListedDeal(string g, decimal oldPrice, decimal newPrice) =>
        Radar.DealItem(Radar.Title(g, "Ofertë"), oldPrice: oldPrice, newPrice: newPrice, noValidUntil: true);

    [Fact, Trait("Req", "REQ-045")]
    public async Task Undated_deal_of_an_auto_publish_source_is_published_as_todays_deal_and_goes_stale()
    {
        var admin = await Radar.AdminAsync(app);
        var g = Radar.Token();

        var ing = await IngestAutoAsync(admin, ListedDeal(g, 100m, 60m));

        Assert.Equal("published", ing.Single.Str("status"));
        var anon = app.CreateClient();
        Assert.Single(await Radar.PublicIdsAsync(anon, $"/api/deals?search={g}"));
        Assert.Empty(await Radar.PublicIdsAsync(anon, $"/api/deals?search={g}", Clock.Now.AddHours(7)));
    }

    [Fact, Trait("Req", "REQ-045")]
    public async Task Listed_deal_stays_active_while_later_fetches_still_list_it()
    {
        var admin = await Radar.AdminAsync(app);
        var g = Radar.Token();
        var deal = ListedDeal(g, 100m, 60m);
        var ing = await IngestAutoAsync(admin, deal);
        Assert.Equal("published", ing.Single.Str("status"));

        var run = await Radar.RefetchWithAsync(admin, ing.SourceId, ing.SourceBody, [deal], Clock.Now.AddHours(5));
        Assert.Equal("succeeded", run.Str("status"));

        var anon = app.CreateClient();
        Assert.Single(await Radar.PublicIdsAsync(anon, $"/api/deals?search={g}", Clock.Now.AddHours(10)));
        Assert.Empty(await Radar.PublicIdsAsync(anon, $"/api/deals?search={g}", Clock.Now.AddHours(12)));
    }

    [Fact, Trait("Req", "REQ-045")]
    public async Task Listed_deal_stops_being_active_when_its_discount_falls_below_35_percent()
    {
        var admin = await Radar.AdminAsync(app);
        var g = Radar.Token();
        var deal = ListedDeal(g, 100m, 60m);
        var ing = await IngestAutoAsync(admin, deal);
        Assert.Equal("published", ing.Single.Str("status"));

        deal["newPrice"] = 80m;
        var run = await Radar.RefetchWithAsync(admin, ing.SourceId, ing.SourceBody, [deal]);
        Assert.Equal("succeeded", run.Str("status"));

        Assert.Empty(await Radar.PublicIdsAsync(app.CreateClient(), $"/api/deals?search={g}"));
    }

    // ---------- REQ-025 ----------

    [Fact, Trait("Req", "REQ-025")]
    public async Task Event_with_a_start_date_but_no_time_is_valid_and_keeps_the_time_unknown()
    {
        var admin = await Radar.AdminAsync(app);
        var day = Clock.Today.AddDays(4);
        var c = await Radar.IngestOneAsync(admin, DateOnlyEvent(Radar.Title(Radar.Token()), day));

        Assert.DoesNotContain("MissingStartsAt", c.IssueCodes());
        Assert.Equal("valid", c.Str("validation"));
        Assert.True(c.GetProperty("fields").IsNull("startsAt"));
        Assert.Equal(Day(day), c.GetProperty("fields").Str("startDate"));

        var itemId = await Radar.ApproveAsync(admin, c.Id());
        var ev = await app.CreateClient().Get($"/api/events/{itemId}").Json();
        Assert.False(ev.Bool("startTimeKnown"));
        Assert.Equal(Day(day), Clock.Date(ev.Time("startsAt")));
    }

    [Fact, Trait("Req", "REQ-025")]
    public async Task Event_with_a_known_start_time_reports_startTimeKnown()
    {
        var admin = await Radar.AdminAsync(app);
        var published = await Radar.PublishAsync(admin, Radar.EventItem(Radar.Title(Radar.Token())));
        var ev = await app.CreateClient().Get($"/api/events/{published.ItemId}").Json();
        Assert.True(ev.Bool("startTimeKnown"));
    }

    [Fact, Trait("Req", "REQ-025")]
    public async Task Date_only_event_stays_listed_until_the_end_of_its_day()
    {
        var admin = await Radar.AdminAsync(app);
        var g = Radar.Token();
        var day = Clock.Today.AddDays(3);
        var c = await Radar.IngestOneAsync(admin, DateOnlyEvent(Radar.Title(g), day));
        var itemId = await Radar.ApproveAsync(admin, c.Id());
        var anon = app.CreateClient();

        Assert.Contains(itemId, await Radar.PublicIdsAsync(anon, $"/api/events?search={g}", Clock.At(day, 23, 30)));
        Assert.DoesNotContain(itemId, await Radar.PublicIdsAsync(anon, $"/api/events?search={g}", Clock.At(day.AddDays(1), 0, 30)));
    }
}
