using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace App.AcceptanceTests;

[Collection(AcceptanceCollection.Name)]
public class PublicApiTests(AppFactory app)
{
    private static string Ext(JsonObject item) => item["externalId"]!.GetValue<string>();

    /// <summary>Ingests the items through one source and approves the given ones; returns externalId → itemId.</summary>
    private static async Task<(Ingested Ing, Dictionary<string, Guid> Items)> PublishManyAsync(HttpClient admin, JsonObject[] items,
        DateTimeOffset? fetchNow = null, Func<JsonObject, bool>? approve = null)
    {
        var ing = await Radar.IngestAsync(admin, items, fetchNow);
        var result = new Dictionary<string, Guid>();
        foreach (var item in items.Where(i => approve?.Invoke(i) ?? true))
            result[Ext(item)] = await Radar.ApproveAsync(admin, ing.Candidate(Ext(item)).Id());
        return (ing, result);
    }

    // ---------- REQ-080 ----------

    [Fact, Trait("Req", "REQ-080")]
    public async Task Events_list_shows_only_active_published_events_by_start_time()
    {
        var admin = await Radar.AdminAsync(app);
        var g = Radar.Token();
        var late = Radar.EventItem(Radar.Title(g), Clock.Future(7));
        var early = Radar.EventItem(Radar.Title(g), Clock.Future(3));
        var middle = Radar.EventItem(Radar.Title(g), Clock.Future(5));
        var pending = Radar.EventItem(Radar.Title(g), Clock.Future(4));
        var expired = Radar.EventItem(Radar.Title(g), Clock.Future(6));
        var withdrawn = Radar.EventItem(Radar.Title(g), Clock.Future(2));
        var (_, ids) = await PublishManyAsync(admin, [late, early, middle, pending, expired, withdrawn], approve: i => i != pending);
        await admin.Post($"/api/admin/events/{ids[Ext(expired)]}/expire").Expect(HttpStatusCode.NoContent);
        await admin.Post($"/api/admin/events/{ids[Ext(withdrawn)]}/withdraw").Expect(HttpStatusCode.NoContent);

        var page = await app.CreateClient().Get($"/api/events?search={g}").Json();

        Assert.Equal(3, page.Int("totalCount"));
        Assert.Equal(new[] { ids[Ext(early)], ids[Ext(middle)], ids[Ext(late)] }, page.Items().Select(i => i.Id()).ToArray());
        var first = page.Items()[0];
        Assert.Equal(early["title"]!.GetValue<string>(), first.Str("title"));
        Assert.Equal("active", first.Str("status"));
        Assert.Equal("prishtine", first.GetProperty("city").Str("slug"));
        Assert.Equal("Prishtinë", first.GetProperty("city").Str("name"));
        Assert.Equal(DateTimeOffset.Parse(early["startsAt"]!.GetValue<string>()), first.Time("startsAt"));
    }

    [Fact, Trait("Req", "REQ-080")]
    public async Task Events_list_is_paginated_with_default_20_and_maximum_100()
    {
        var admin = await Radar.AdminAsync(app);
        var g = Radar.Token();
        var items = Enumerable.Range(1, 3).Select(d => Radar.EventItem(Radar.Title(g), Clock.Future(d + 2))).ToArray();
        var (_, ids) = await PublishManyAsync(admin, items);
        var anon = app.CreateClient();

        var defaults = await anon.Get("/api/events").Json();
        Assert.Equal(1, defaults.Int("page"));
        Assert.Equal(20, defaults.Int("pageSize"));
        Assert.True(defaults.Items().Count <= 20);
        Assert.True(defaults.Int("totalCount") >= 3);

        var max = await anon.Get("/api/events?pageSize=100").Json();
        Assert.Equal(100, max.Int("pageSize"));

        var p1 = await anon.Get($"/api/events?search={g}&pageSize=2&page=1").Json();
        var p2 = await anon.Get($"/api/events?search={g}&pageSize=2&page=2").Json();
        var p3 = await anon.Get($"/api/events?search={g}&pageSize=2&page=3").Json();
        Assert.Equal(3, p1.Int("totalCount"));
        Assert.Equal(new[] { ids[Ext(items[0])], ids[Ext(items[1])] }, p1.Items().Select(i => i.Id()).ToArray());
        Assert.Equal(new[] { ids[Ext(items[2])] }, p2.Items().Select(i => i.Id()).ToArray());
        Assert.Empty(p3.Items());
        Assert.Equal(3, p3.Int("totalCount"));
    }

    // ---------- REQ-081 ----------

    [Fact, Trait("Req", "REQ-081")]
    public async Task Events_are_filtered_by_city_category_and_pristina_dates()
    {
        var admin = await Radar.AdminAsync(app);
        var g = Radar.Token();
        var d1 = Clock.Today.AddDays(20);
        var d2 = Clock.Today.AddDays(22);
        var d3 = Clock.Today.AddDays(24);
        var a = Radar.EventItem(Radar.Title(g), Clock.At(d1, 20), "Prishtinë");
        a["category"] = "muzike";
        var b = Radar.EventItem(Radar.Title(g), Clock.At(d2, 20), "Prizren");
        b["category"] = "sport";
        var c = Radar.EventItem(Radar.Title(g), Clock.At(d3, 0, 30), "Prishtinë"); // 00:30 Pristina = previous day in UTC
        c["category"] = "art";
        var (_, ids) = await PublishManyAsync(admin, [a, b, c]);
        var (A, B, C) = (ids[Ext(a)], ids[Ext(b)], ids[Ext(c)]);
        var anon = app.CreateClient();
        string D(DateOnly d) => d.ToString("yyyy-MM-dd");
        async Task<Guid[]> Ids(string q) => (await Radar.PublicIdsAsync(anon, $"/api/events?search={g}{q}")).ToArray();

        Assert.Equal(new[] { A, B, C }, await Ids(""));
        Assert.Equal(new[] { B }, await Ids("&city=prizren"));
        Assert.Equal(new[] { A }, await Ids("&category=muzike"));
        Assert.Empty(await Ids("&city=prizren&category=muzike"));
        Assert.Equal(new[] { B, C }, await Ids($"&from={D(d2)}"));
        Assert.Equal(new[] { A }, await Ids($"&to={D(d1)}"));
        Assert.Equal(new[] { A }, await Ids($"&from={D(d1)}&to={D(d1)}"));
        Assert.Equal(new[] { A, B }, await Ids($"&from={D(d1)}&to={D(d2)}"));
        Assert.Equal(new[] { C }, await Ids($"&from={D(d3)}&to={D(d3)}"));
        Assert.Empty(await Ids($"&from={D(d3.AddDays(-1))}&to={D(d3.AddDays(-1))}"));
        Assert.Equal(new[] { A }, await Ids($"&city=prishtine&to={D(d2)}"));
    }

    [Fact, Trait("Req", "REQ-081")]
    public async Task Event_search_matches_title_description_and_venue_ignoring_case_and_diacritics()
    {
        var admin = await Radar.AdminAsync(app);
        var (gt, gd, gv) = (Radar.Token(), Radar.Token(), Radar.Token());
        var item = Radar.EventItem($"Netë e Bardhë {gt}");
        item["description"] = $"Këngëtarë {gd} në skenë";
        item["venue"] = $"Çarshia {gv}";
        var published = await Radar.PublishAsync(admin, item);
        var anon = app.CreateClient();

        foreach (var q in new[] { gt, $"bardhe {gt}", $"BARDHË {gt.ToUpperInvariant()}", $"kengetare {gd}", gd, $"carshia {gv}", $"ÇARSHIA {gv}", gv })
            Assert.Equal(new[] { published.ItemId }, (await Radar.PublicIdsAsync(anon, $"/api/events?search={Uri.EscapeDataString(q)}")).ToArray());

        Assert.Empty(await Radar.PublicIdsAsync(anon, $"/api/events?search={Uri.EscapeDataString($"zeze {gt}")}"));
    }

    // ---------- REQ-082 ----------

    [Theory, Trait("Req", "REQ-082")]
    [InlineData("/api/events?city=atlantis", "city")]
    [InlineData("/api/events?category=no-such-category", "category")]
    [InlineData("/api/events?from=2030-12-10&to=2030-12-01", "from|to")]
    [InlineData("/api/events?from=not-a-date", "from")]
    [InlineData("/api/events?page=0", "page")]
    [InlineData("/api/events?pageSize=0", "pageSize")]
    [InlineData("/api/events?pageSize=101", "pageSize")]
    [InlineData("/api/deals?city=atlantis", "city")]
    [InlineData("/api/deals?category=no-such-category", "category")]
    [InlineData("/api/deals?page=0", "page")]
    [InlineData("/api/deals?pageSize=101", "pageSize")]
    [InlineData("/api/search?q=koncert&pageSize=101", "pageSize")]
    public async Task Invalid_list_parameters_get_400_naming_the_parameter(string url, string field)
    {
        var res = await app.CreateClient().Get(url);
        var text = await res.Content.ReadAsStringAsync();
        Assert.True(res.StatusCode == HttpStatusCode.BadRequest, $"Expected 400 for {url}, got {(int)res.StatusCode}: {text}");
        var keys = JsonExt.Parse(text).GetProperty("errors").EnumerateObject().Select(p => p.Name).ToList();
        Assert.True(field.Split('|').Any(f => keys.Contains(f, StringComparer.OrdinalIgnoreCase)), $"Expected an error on {field}: {text}");
    }

    [Theory, Trait("Req", "REQ-082")]
    [InlineData("/api/events?page=1&pageSize=1")]
    [InlineData("/api/events?pageSize=100")]
    [InlineData("/api/events?from=2030-12-01&to=2030-12-01")]
    [InlineData("/api/events?city=prizren&category=muzike")]
    [InlineData("/api/deals?city=prishtine&category=teknologji&pageSize=1")]
    public async Task Valid_boundary_parameters_are_accepted(string url)
    {
        await app.CreateClient().Get(url).Expect(HttpStatusCode.OK);
    }

    // ---------- REQ-083 ----------

    [Fact, Trait("Req", "REQ-083")]
    public async Task Deals_list_shows_active_deals_by_valid_until_with_open_ended_deals_last()
    {
        var admin = await Radar.AdminAsync(app);
        var g = Radar.Token();
        var d10 = Radar.DealItem(Radar.Title(g, "Ofertë"), validUntil: Clock.Future(10));
        var d3 = Radar.DealItem(Radar.Title(g, "Ofertë"), validUntil: Clock.Future(3));
        var open = Radar.DealItem(Radar.Title(g, "Ofertë"), noValidUntil: true);
        var d6 = Radar.DealItem(Radar.Title(g, "Ofertë"), validUntil: Clock.Future(6));
        var pending = Radar.DealItem(Radar.Title(g, "Ofertë"), validUntil: Clock.Future(4));
        var expired = Radar.DealItem(Radar.Title(g, "Ofertë"), validUntil: Clock.Future(5));
        var withdrawn = Radar.DealItem(Radar.Title(g, "Ofertë"), validUntil: Clock.Future(2));
        var (_, ids) = await PublishManyAsync(admin, [d10, d3, open, d6, pending, expired, withdrawn], approve: i => i != pending);
        await admin.Post($"/api/admin/deals/{ids[Ext(expired)]}/expire").Expect(HttpStatusCode.NoContent);
        await admin.Post($"/api/admin/deals/{ids[Ext(withdrawn)]}/withdraw").Expect(HttpStatusCode.NoContent);

        var page = await app.CreateClient().Get($"/api/deals?search={g}").Json();

        Assert.Equal(4, page.Int("totalCount"));
        Assert.Equal(new[] { ids[Ext(d3)], ids[Ext(d6)], ids[Ext(d10)], ids[Ext(open)] }, page.Items().Select(i => i.Id()).ToArray());
        var first = page.Items()[0];
        Assert.Equal(25m, first.Dec("oldPrice"));
        Assert.Equal(20m, first.Dec("newPrice"));
        Assert.Equal("EUR", first.Str("currency"));
        Assert.Equal("Dyqani Test", first.Str("merchantName"));
        Assert.Equal("active", first.Str("status"));
        Assert.True(page.Items()[3].IsNull("validUntil"));

        var p2 = await app.CreateClient().Get($"/api/deals?search={g}&pageSize=3&page=2").Json();
        Assert.Equal(new[] { ids[Ext(open)] }, p2.Items().Select(i => i.Id()).ToArray());
        Assert.Equal(20, (await app.CreateClient().Get("/api/deals").Json()).Int("pageSize"));
    }

    [Fact, Trait("Req", "REQ-083")]
    public async Task Deals_are_filtered_by_city_category_and_search()
    {
        var admin = await Radar.AdminAsync(app);
        var g = Radar.Token();
        var a = Radar.DealItem($"Laptop Çmim i Ulët {g}", city: "Prizren");
        a["category"] = "teknologji";
        var b = Radar.DealItem(Radar.Title(g, "Darkë"), city: "Pejë");
        b["category"] = "ushqim";
        b["description"] = $"Pjatë tradicionale {g}";
        var (_, ids) = await PublishManyAsync(admin, [a, b]);
        var anon = app.CreateClient();
        async Task<Guid[]> Ids(string q) => (await Radar.PublicIdsAsync(anon, $"/api/deals?{q}")).ToArray();

        Assert.Equal(new[] { ids[Ext(a)] }, await Ids($"search={g}&city=prizren"));
        Assert.Equal(new[] { ids[Ext(b)] }, await Ids($"search={g}&category=ushqim"));
        Assert.Equal(new[] { ids[Ext(a)] }, await Ids($"search={Uri.EscapeDataString($"cmim i ulet {g}")}&city=prizren&category=teknologji"));
        Assert.Equal(new[] { ids[Ext(b)] }, await Ids($"search={Uri.EscapeDataString($"PJATE TRADICIONALE {g}")}"));
        Assert.Empty(await Ids($"search={g}&city=prizren&category=ushqim"));
    }

    // ---------- REQ-084 ----------

    [Theory, Trait("Req", "REQ-084")]
    [InlineData("event")]
    [InlineData("deal")]
    public async Task Items_carry_source_and_freshness_in_list_and_detail(string kind)
    {
        var admin = await Radar.AdminAsync(app);
        var g = Radar.Token();
        var item = kind == "event" ? Radar.EventItem(Radar.Title(g), Clock.Future(10)) : Radar.DealItem(Radar.Title(g, "Ofertë"), validUntil: Clock.Future(10));
        var t0 = Clock.Now.AddHours(-1);
        var name = $"Burimi {g}";
        var published = await Radar.PublishAsync(admin, item, t0, name);
        var anon = app.CreateClient();
        var list = kind == "event" ? $"/api/events?search={g}" : $"/api/deals?search={g}";

        var cases = new (DateTimeOffset? Now, string Expected)[] { (t0.AddHours(47), "fresh"), (t0.AddHours(49), "stale"), (null, "fresh") };
        foreach (var (now, expected) in cases)
        {
            var listed = Assert.Single(await Radar.PublicListAsync(anon, list, now));
            var detail = await anon.Get(published.PublicPath, now).Json();
            foreach (var dto in new[] { listed, detail })
            {
                Assert.Equal(name, dto.Str("sourceName"));
                Assert.Equal(item["url"]!.GetValue<string>(), dto.Str("sourceUrl"));
                Assert.Equal(t0, dto.Time("lastVerifiedAt"));
                Assert.Equal(expected, dto.Str("freshness"));
            }
        }
    }

    // ---------- REQ-085 ----------

    [Theory, Trait("Req", "REQ-085")]
    [InlineData("event")]
    [InlineData("deal")]
    public async Task Detail_returns_the_item_with_its_sources(string kind)
    {
        var admin = await Radar.AdminAsync(app);
        var g = Radar.Token();
        var item = kind == "event" ? Radar.EventItem(Radar.Title(g)) : Radar.DealItem(Radar.Title(g, "Ofertë"));
        item["description"] = "Përshkrim i plotë";
        var name = $"Burimi {g}";
        var published = await Radar.PublishAsync(admin, item, sourceName: name);

        var detail = await app.CreateClient().Get(published.PublicPath).Json();

        Assert.Equal(published.ItemId, detail.Id());
        Assert.Equal(item["title"]!.GetValue<string>(), detail.Str("title"));
        Assert.Equal("Përshkrim i plotë", detail.Str("description"));
        var source = Assert.Single(detail.GetProperty("sources").Array());
        Assert.Equal(name, source.Str("name"));
        Assert.Equal(item["url"]!.GetValue<string>(), source.Str("url"));
    }

    [Theory, Trait("Req", "REQ-085")]
    [InlineData("events")]
    [InlineData("deals")]
    public async Task Detail_of_unknown_unpublished_rejected_or_withdrawn_ids_is_404(string path)
    {
        var admin = await Radar.AdminAsync(app);
        var anon = app.CreateClient();
        var isEvent = path == "events";
        JsonObject New() => isEvent ? Radar.EventItem(Radar.Title(Radar.Token())) : Radar.DealItem(Radar.Title(Radar.Token(), "Ofertë"));

        await anon.Get($"/api/{path}/{Guid.NewGuid()}").Expect(HttpStatusCode.NotFound);
        await anon.Get($"/api/{path}/not-a-guid").Expect(HttpStatusCode.NotFound);

        var pending = await Radar.IngestOneAsync(admin, New());
        await anon.Get($"/api/{path}/{pending.Id()}").Expect(HttpStatusCode.NotFound);

        var rejected = await Radar.IngestOneAsync(admin, New());
        await admin.Post($"/api/admin/candidates/{rejected.Id()}/reject", new { reason = "Jo" }).Json();
        await anon.Get($"/api/{path}/{rejected.Id()}").Expect(HttpStatusCode.NotFound);

        var withdrawn = await Radar.PublishAsync(admin, New());
        await admin.Post($"{withdrawn.AdminPath}/withdraw").Expect(HttpStatusCode.NoContent);
        await anon.Get(withdrawn.PublicPath).Expect(HttpStatusCode.NotFound);

        // An item of the other kind is not found under this path.
        var other = await Radar.PublishAsync(admin, isEvent ? Radar.DealItem(Radar.Title(Radar.Token(), "Ofertë")) : Radar.EventItem(Radar.Title(Radar.Token())));
        await anon.Get($"/api/{path}/{other.ItemId}").Expect(HttpStatusCode.NotFound);
    }

    // ---------- REQ-086 ----------

    [Theory, Trait("Req", "REQ-086")]
    [InlineData("no-end", 5, 7)]
    [InlineData("end-after-10h", 9, 11)]
    [InlineData("end-before-start", 5, 7)]
    public async Task Event_past_its_effective_end_leaves_lists_and_shows_expired(string variant, int activeAtHours, int expiredAtHours)
    {
        var admin = await Radar.AdminAsync(app);
        var g = Radar.Token();
        var start = Clock.Future(2, 12);
        DateTimeOffset? end = variant switch { "end-after-10h" => start.AddHours(10), "end-before-start" => start.AddHours(-1), _ => null };
        var published = await Radar.PublishAsync(admin, Radar.EventItem(Radar.Title(g), start, endsAt: end));
        var anon = app.CreateClient();

        var activeAt = start.AddHours(activeAtHours);
        Assert.Contains(published.ItemId, await Radar.PublicIdsAsync(anon, $"/api/events?search={g}", activeAt));
        Assert.Contains(published.ItemId, await Radar.PublicIdsAsync(anon, $"/api/search?q={g}", activeAt));
        Assert.Equal("active", (await anon.Get(published.PublicPath, activeAt).Json()).Str("status"));

        var expiredAt = start.AddHours(expiredAtHours);
        Assert.DoesNotContain(published.ItemId, await Radar.PublicIdsAsync(anon, $"/api/events?search={g}", expiredAt));
        Assert.DoesNotContain(published.ItemId, await Radar.PublicIdsAsync(anon, $"/api/search?q={g}", expiredAt));
        var detail = await anon.Get(published.PublicPath, expiredAt).Json();
        Assert.Equal("expired", detail.Str("status"));
        Assert.Equal(published.ItemId, detail.Id());
    }

    [Fact, Trait("Req", "REQ-086")]
    public async Task Deal_past_its_valid_until_leaves_lists_and_shows_expired()
    {
        var admin = await Radar.AdminAsync(app);
        var g = Radar.Token();
        var until = Clock.Future(4, 18);
        var published = await Radar.PublishAsync(admin, Radar.DealItem(Radar.Title(g, "Ofertë"), validUntil: until));
        var anon = app.CreateClient();

        var before = until.AddMinutes(-1);
        Assert.Contains(published.ItemId, await Radar.PublicIdsAsync(anon, $"/api/deals?search={g}", before));
        Assert.Equal("active", (await anon.Get(published.PublicPath, before).Json()).Str("status"));

        var after = until.AddMinutes(1);
        Assert.DoesNotContain(published.ItemId, await Radar.PublicIdsAsync(anon, $"/api/deals?search={g}", after));
        Assert.DoesNotContain(published.ItemId, await Radar.PublicIdsAsync(anon, $"/api/search?q={g}", after));
        Assert.Equal("expired", (await anon.Get(published.PublicPath, after).Json()).Str("status"));
    }

    // ---------- REQ-087 / REQ-079 ----------

    [Fact, Trait("Req", "REQ-087")]
    public async Task Open_ended_deal_not_verified_for_more_than_7_days_leaves_lists_and_search()
    {
        var admin = await Radar.AdminAsync(app);
        var g = Radar.Token();
        var t0 = Clock.Now.AddDays(-8);
        var published = await Radar.PublishAsync(admin, Radar.DealItem(Radar.Title(g, "Ofertë"), noValidUntil: true), t0);
        var anon = app.CreateClient();

        // Six days after verification it is still listed.
        Assert.Contains(published.ItemId, await Radar.PublicIdsAsync(anon, $"/api/deals?search={g}", t0.AddDays(6)));
        Assert.Contains(published.ItemId, await Radar.PublicIdsAsync(anon, $"/api/search?q={g}", t0.AddDays(6)));

        // Now (8 days later) and just past 7 days it is not.
        Assert.DoesNotContain(published.ItemId, await Radar.PublicIdsAsync(anon, $"/api/deals?search={g}"));
        Assert.DoesNotContain(published.ItemId, await Radar.PublicIdsAsync(anon, $"/api/search?q={g}"));
        Assert.DoesNotContain(published.ItemId, await Radar.PublicIdsAsync(anon, $"/api/deals?search={g}", t0.AddDays(7).AddHours(1)));
    }

    [Fact, Trait("Req", "REQ-087")]
    public async Task Refetching_an_open_ended_deal_brings_it_back()
    {
        var admin = await Radar.AdminAsync(app);
        var g = Radar.Token();
        var published = await Radar.PublishAsync(admin, Radar.DealItem(Radar.Title(g, "Ofertë"), noValidUntil: true), Clock.Now.AddDays(-8));
        var anon = app.CreateClient();
        Assert.Empty(await Radar.PublicIdsAsync(anon, $"/api/deals?search={g}"));

        await Radar.FetchAsync(admin, published.SourceId);

        Assert.Equal(new[] { published.ItemId }, (await Radar.PublicIdsAsync(anon, $"/api/deals?search={g}")).ToArray());
    }

    [Fact, Trait("Req", "REQ-079")]
    public async Task Stale_open_ended_deal_requested_by_id_is_expired()
    {
        var admin = await Radar.AdminAsync(app);
        var g = Radar.Token();
        var published = await Radar.PublishAsync(admin, Radar.DealItem(Radar.Title(g, "Ofertë"), noValidUntil: true), Clock.Now.AddDays(-8));
        var anon = app.CreateClient();

        var detail = await anon.Get(published.PublicPath).Json();
        Assert.Equal("expired", detail.Str("status"));
        Assert.Equal(published.ItemId, detail.Id());

        Assert.Equal("active", (await anon.Get(published.PublicPath, Clock.Now.AddDays(-7)).Json()).Str("status"));
    }

    // ---------- REQ-088 ----------

    [Fact, Trait("Req", "REQ-088")]
    public async Task Cities_are_the_38_kosovo_municipalities_ordered_by_slug()
    {
        var cities = (await app.CreateClient().Get("/api/cities").Json()).Array();

        Assert.Equal(38, cities.Count);
        var slugs = cities.Select(c => c.Str("slug")!).ToList();
        Assert.Equal(slugs.OrderBy(s => s, StringComparer.Ordinal).ToList(), slugs);
        Assert.Equal("Prishtinë", cities.Single(c => c.Str("slug") == "prishtine").Str("name"));
        Assert.Equal("Mitrovicë e Veriut", cities.Single(c => c.Str("slug") == "mitrovice-veriore").Str("name"));
        Assert.Equal("Fushë Kosovë", cities.Single(c => c.Str("slug") == "fushe-kosove").Str("name"));
        Assert.Equal("Deçan", cities.Single(c => c.Str("slug") == "decan").Str("name"));
        foreach (var s in new[] { "prizren", "peje", "gjakove", "ferizaj", "gjilan", "leposaviq", "zubin-potok", "hani-i-elezit", "gracanice" })
            Assert.Contains(s, slugs);
    }

    [Fact, Trait("Req", "REQ-088")]
    public async Task Categories_are_listed_and_filterable_by_kind()
    {
        var anon = app.CreateClient();
        var events = (await anon.Get("/api/categories?kind=event").Json()).Array();
        var deals = (await anon.Get("/api/categories?kind=deal").Json()).Array();
        var all = (await anon.Get("/api/categories").Json()).Array();

        Assert.Equal(new[] { "art", "edukim", "familje", "muzike", "nate", "sport", "teater", "ushqim" },
            events.Select(c => c.Str("slug")).ToArray());
        Assert.All(events, c => Assert.Equal("event", c.Str("kind")));
        Assert.Equal("Muzikë & koncerte", events.Single(c => c.Str("slug") == "muzike").Str("name"));
        Assert.Equal(new[] { "mode", "sherbime", "shtepi", "teknologji", "udhetime", "ushqim" },
            deals.Select(c => c.Str("slug")).ToArray());
        Assert.All(deals, c => Assert.Equal("deal", c.Str("kind")));
        Assert.Equal("Udhëtime", deals.Single(c => c.Str("slug") == "udhetime").Str("name"));
        Assert.Equal(14, all.Count);

        await anon.Get("/api/categories?kind=concert").ExpectValidation("kind");
    }

    // ---------- REQ-089 ----------

    [Fact, Trait("Req", "REQ-089")]
    public async Task Search_returns_active_events_then_deals_with_their_kind()
    {
        var admin = await Radar.AdminAsync(app);
        var g = Radar.Token();
        var e5 = Radar.EventItem(Radar.Title(g), Clock.Future(5), "Prizren");
        var e3 = Radar.EventItem(Radar.Title(g), Clock.Future(3));
        var d = Radar.DealItem(Radar.Title(g, "Ofertë"), validUntil: Clock.Future(2));
        var pending = Radar.EventItem(Radar.Title(g), Clock.Future(4));
        var name = $"Burimi {g}";
        var ing = await Radar.IngestAsync(admin, [e5, e3, d, pending], sourceName: name);
        var ids = new Dictionary<string, Guid>();
        foreach (var x in new[] { e5, e3, d }) ids[Ext(x)] = await Radar.ApproveAsync(admin, ing.Candidate(Ext(x)).Id());

        var page = await app.CreateClient().Get($"/api/search?q={g}").Json();
        var items = page.Items();

        Assert.Equal(3, page.Int("totalCount"));
        Assert.Equal(new[] { ids[Ext(e3)], ids[Ext(e5)], ids[Ext(d)] }, items.Select(i => i.Id()).ToArray());
        Assert.Equal(new[] { "event", "event", "deal" }, items.Select(i => i.Str("kind")).ToArray());
        Assert.Equal(e5["title"]!.GetValue<string>(), items[1].Str("title"));
        Assert.Equal(DateTimeOffset.Parse(e5["startsAt"]!.GetValue<string>()), items[1].Time("date"));
        Assert.Equal("Prizren", items[1].Str("cityName"));
        Assert.Equal(name, items[1].Str("sourceName"));
        Assert.Equal(DateTimeOffset.Parse(d["validUntil"]!.GetValue<string>()), items[2].Time("date"));
    }

    [Theory, Trait("Req", "REQ-089")]
    [InlineData("/api/search?q=a")]
    [InlineData("/api/search?q=%20a%20")]
    [InlineData("/api/search?q=")]
    [InlineData("/api/search")]
    public async Task Search_with_a_query_shorter_than_2_characters_gets_400_on_q(string url)
    {
        await app.CreateClient().Get(url).ExpectValidation("q");
    }

    [Fact, Trait("Req", "REQ-089")]
    public async Task Search_with_exactly_2_characters_is_accepted()
    {
        var page = await app.CreateClient().Get("/api/search?q=zq").Json();
        Assert.Equal(JsonValueKind.Array, page.GetProperty("items").ValueKind);
    }
}
