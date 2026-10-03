using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace App.AcceptanceTests;

[Collection(AcceptanceCollection.Name)]
public partial class SeoTests(AppFactory app)
{
    private const string BaseUrl = "https://localhost"; // Site__BaseUrl in Testing (design.md §2)

    private async Task<(HttpResponseMessage Res, string Html)> GetPageAsync(string path)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        var res = await app.CreateClient().SendAsync(req);
        return (res, await res.Content.ReadAsStringAsync());
    }

    private static string? Meta(string html, string property)
    {
        foreach (Match m in MetaTag().Matches(html))
        {
            var tag = m.Value;
            var key = Attr(tag, "property") ?? Attr(tag, "name");
            if (key == property) return WebUtility.HtmlDecode(Attr(tag, "content") ?? "");
        }
        return null;
    }

    private static string? Attr(string tag, string name)
    {
        var m = Regex.Match(tag, $"\\b{name}\\s*=\\s*(\"([^\"]*)\"|'([^']*)')", RegexOptions.IgnoreCase);
        return m.Success ? (m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value) : null;
    }

    [GeneratedRegex("<meta\\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex MetaTag();

    [GeneratedRegex("<title>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitleTag();

    [GeneratedRegex("<script[^>]*type=[\"']application/ld\\+json[\"'][^>]*>(.*?)</script>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex JsonLd();

    [Fact, Trait("Req", "REQ-090")]
    public async Task Event_page_has_title_open_graph_and_json_ld_event()
    {
        var admin = await Radar.AdminAsync(app);
        var g = Radar.Token();
        var start = Clock.Future(9);
        var item = Radar.EventItem($"Koncert {g} & Miq", start, "Prizren");
        item["venue"] = "Kalaja e Prizrenit";
        item["description"] = "Një mbrëmje e veçantë me muzikë live.";
        var published = await Radar.PublishAsync(admin, item);

        var (res, html) = await GetPageAsync($"/events/{published.ItemId}");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("text/html", res.Content.Headers.ContentType?.MediaType);
        var title = WebUtility.HtmlDecode(TitleTag().Match(html).Groups[1].Value);
        Assert.Contains($"Koncert {g} & Miq", title);
        Assert.Contains($"Koncert {g} & Miq", Meta(html, "og:title"));
        Assert.False(string.IsNullOrWhiteSpace(Meta(html, "og:description")));
        Assert.Equal($"{BaseUrl}/events/{published.ItemId}", Meta(html, "og:url"));

        var ld = JsonLd().Matches(html).Select(m => JsonExt.Parse(m.Groups[1].Value))
            .Single(j => j.ValueKind == JsonValueKind.Object && j.Str("@type") == "Event");
        Assert.Equal($"Koncert {g} & Miq", ld.Str("name"));
        Assert.Equal(start, ld.Time("startDate"));
        var location = ld.GetProperty("location");
        Assert.Equal(JsonValueKind.Object, location.ValueKind);
        Assert.Contains("Prizren", location.GetRawText());
    }

    [Fact, Trait("Req", "REQ-090")]
    public async Task Deal_page_has_title_and_open_graph()
    {
        var admin = await Radar.AdminAsync(app);
        var g = Radar.Token();
        var published = await Radar.PublishAsync(admin, Radar.DealItem($"Ofertë {g} Telefon", validUntil: Clock.Future(8)));

        var (res, html) = await GetPageAsync($"/deals/{published.ItemId}");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("text/html", res.Content.Headers.ContentType?.MediaType);
        Assert.Contains($"Ofertë {g} Telefon", WebUtility.HtmlDecode(TitleTag().Match(html).Groups[1].Value));
        Assert.Contains($"Ofertë {g} Telefon", Meta(html, "og:title"));
        Assert.False(string.IsNullOrWhiteSpace(Meta(html, "og:description")));
        Assert.Equal($"{BaseUrl}/deals/{published.ItemId}", Meta(html, "og:url"));
    }

    [Fact, Trait("Req", "REQ-090")]
    public async Task Title_with_markup_is_encoded_and_cannot_break_out_of_the_head()
    {
        var admin = await Radar.AdminAsync(app);
        var g = Radar.Token();
        var nasty = $"Koncert {g} </title></script><script>alert(1)</script>";
        var published = await Radar.PublishAsync(admin, Radar.EventItem(nasty));

        var (res, html) = await GetPageAsync($"/events/{published.ItemId}");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains(nasty, WebUtility.HtmlDecode(TitleTag().Match(html).Groups[1].Value));
    }

    [Theory, Trait("Req", "REQ-090")]
    [InlineData("events")]
    [InlineData("deals")]
    public async Task Page_of_an_unknown_or_withdrawn_item_is_404(string path)
    {
        var (res, _) = await GetPageAsync($"/{path}/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);

        var admin = await Radar.AdminAsync(app);
        var published = await Radar.PublishAsync(admin,
            path == "events" ? Radar.EventItem(Radar.Title(Radar.Token())) : Radar.DealItem(Radar.Title(Radar.Token(), "Ofertë")));
        await admin.Post($"{published.AdminPath}/withdraw").Expect(HttpStatusCode.NoContent);
        var (withdrawn, _) = await GetPageAsync($"/{path}/{published.ItemId}");
        Assert.Equal(HttpStatusCode.NotFound, withdrawn.StatusCode);
    }

    [Fact, Trait("Req", "REQ-091")]
    public async Task Sitemap_lists_static_pages_and_only_active_published_items()
    {
        var admin = await Radar.AdminAsync(app);
        var activeEvent = await Radar.PublishAsync(admin, Radar.EventItem(Radar.Title(Radar.Token())));
        var activeDeal = await Radar.PublishAsync(admin, Radar.DealItem(Radar.Title(Radar.Token(), "Ofertë")));
        var expired = await Radar.PublishAsync(admin, Radar.EventItem(Radar.Title(Radar.Token())));
        await admin.Post($"{expired.AdminPath}/expire").Expect(HttpStatusCode.NoContent);
        var expiredDeal = await Radar.PublishAsync(admin, Radar.DealItem(Radar.Title(Radar.Token(), "Ofertë")));
        await admin.Post($"{expiredDeal.AdminPath}/expire").Expect(HttpStatusCode.NoContent);
        var withdrawn = await Radar.PublishAsync(admin, Radar.EventItem(Radar.Title(Radar.Token())));
        await admin.Post($"{withdrawn.AdminPath}/withdraw").Expect(HttpStatusCode.NoContent);
        var unpublished = await Radar.IngestOneAsync(admin, Radar.EventItem(Radar.Title(Radar.Token())));

        var res = await app.CreateClient().GetAsync("/sitemap.xml");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("xml", res.Content.Headers.ContentType?.MediaType);
        var xml = XDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.Equal("urlset", xml.Root!.Name.LocalName);
        var locs = xml.Descendants().Where(e => e.Name.LocalName == "loc").Select(e => e.Value.Trim()).ToList();

        Assert.Contains($"{BaseUrl}/", locs);
        Assert.Contains($"{BaseUrl}/events", locs);
        Assert.Contains($"{BaseUrl}/deals", locs);
        Assert.Contains($"{BaseUrl}/events/{activeEvent.ItemId}", locs);
        Assert.Contains($"{BaseUrl}/deals/{activeDeal.ItemId}", locs);
        Assert.DoesNotContain(locs, l => l.Contains(expired.ItemId.ToString(), StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(locs, l => l.Contains(expiredDeal.ItemId.ToString(), StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(locs, l => l.Contains(withdrawn.ItemId.ToString(), StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(locs, l => l.Contains(unpublished.Id().ToString(), StringComparison.OrdinalIgnoreCase));
    }

    [Fact, Trait("Req", "REQ-091")]
    public async Task Sitemap_excludes_items_whose_time_has_passed()
    {
        var admin = await Radar.AdminAsync(app);
        // An event that started 7 h ago (no end: effective end 1 h ago), fetched and approved while it was still running.
        var start = Clock.Now.AddHours(-7);
        var then = Clock.Now.AddHours(-3);
        var ing = await Radar.IngestAsync(admin, [Radar.EventItem(Radar.Title(Radar.Token()), start)], then);
        var approved = await admin.Post($"/api/admin/candidates/{ing.Single.Id()}/approve", null, then).Json();
        var endedEvent = approved.Id("itemId");
        // An open-ended deal last verified 8 days ago.
        var staleDeal = await Radar.PublishAsync(admin, Radar.DealItem(Radar.Title(Radar.Token(), "Ofertë"), noValidUntil: true), Clock.Now.AddDays(-8));
        Assert.Equal("expired", (await app.CreateClient().Get($"/api/events/{endedEvent}").Json()).Str("status"));

        var locs = XDocument.Parse(await app.CreateClient().GetStringAsync("/sitemap.xml"))
            .Descendants().Where(e => e.Name.LocalName == "loc").Select(e => e.Value.Trim()).ToList();

        Assert.DoesNotContain(locs, l => l.Contains(endedEvent.ToString(), StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(locs, l => l.Contains(staleDeal.ItemId.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    [Fact, Trait("Req", "REQ-092")]
    public async Task Robots_disallows_admin_and_references_the_sitemap()
    {
        var res = await app.CreateClient().GetAsync("/robots.txt");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("text/plain", res.Content.Headers.ContentType?.MediaType);
        var lines = (await res.Content.ReadAsStringAsync()).Split('\n').Select(l => l.Trim()).ToList();
        Assert.Contains("User-agent: *", lines);
        Assert.Contains("Disallow: /admin", lines);
        Assert.Contains("Disallow: /api/admin", lines);
        Assert.Contains($"Sitemap: {BaseUrl}/sitemap.xml", lines);
    }
}
