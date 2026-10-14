using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AngleSharp.Dom;
using App.Application.Public;
using App.Application.Sources;
using App.Domain.Content;
using App.Domain.Reference;
using App.Domain.Sources;
using App.Infrastructure.Ingestion;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace App.IntegrationTests;

/// <summary>
/// T042: an HTML page over a stubbed HTTP client becomes candidates; the two known sources are seeded once. The saved pages
/// of T044/T045 also run end to end through the real site parsers, reference resolution and the database.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class HtmlSourceTests(AppFactory app) : IDisposable
{
    private readonly Dictionary<string, string> _pages = [];
    private readonly HashSet<string> _serverErrors = [];
    private readonly List<string> _requested = [];
    private readonly List<IDisposable> _factories = [];

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(respond(request));
    }

    /// <summary>Stands in for the site parsers (the real ones come with T044/T045): <c>li.card</c> with an <c>a</c> and an <c>h3</c>.</summary>
    private sealed class CardParser : IHtmlSiteParser
    {
        public string Key => HtmlAdapters.PrishtinaEvents;

        public IReadOnlyList<IElement> FindListings(IDocument page, CancellationToken ct) => page.QuerySelectorAll("li.card").ToList();

        public HtmlListing Map(IElement listing, Uri pageUrl, DateTimeOffset now) =>
            new(listing.QuerySelector("a")?.GetAttribute("href"), new CandidateFields
            {
                Title = listing.QuerySelector("h3")?.TextContent.Trim(),
                CityText = "Prishtinë",
                CategorySlug = "muzike",
                StartsAt = now.AddDays(2),
            });
    }

    private HttpClient CreateClient(bool realParsers = false, IHtmlSiteParser? parser = null, int maxPages = 5)
    {
        var factory = app.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            services.RemoveAll<IngestionOptions>();
            services.AddSingleton(new IngestionOptions { AllowFixtureSources = true, HttpRetries = 0, HttpTimeoutSeconds = 5, PerHostDelaySeconds = 0, HtmlMaxPages = maxPages });
            if (!realParsers)
            {
                services.RemoveAll<IHtmlSiteParser>();
                services.AddSingleton(parser ?? new CardParser());
            }
            services.AddHttpClient<SourceHttpFetcher>(SourceHttpFetcher.ClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new StubHandler(request =>
                {
                    var url = request.RequestUri!.AbsoluteUri;
                    _requested.Add(url);
                    if (url.EndsWith("/robots.txt")) return new HttpResponseMessage(HttpStatusCode.NotFound);
                    if (_serverErrors.Contains(url)) return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                    return _pages.TryGetValue(url, out var html)
                        ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) }
                        : throw new InvalidOperationException($"No page for {url}");
                }));
        }));
        _factories.Add(factory);
        return factory.CreateClient();
    }

    private static async Task<HttpClient> LoginAsync(HttpClient client)
    {
        var res = await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@kosovaradar.local", password = "Radar-admin-dev-2026" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return client;
    }

    private static async Task<(Guid Id, JsonElement Run)> FetchAsync(
        HttpClient admin, string url, string adapter = HtmlAdapters.PrishtinaEvents, bool autoPublish = false)
    {
        var created = await admin.PostAsJsonAsync("/api/admin/sources", new
        {
            name = $"Html {Guid.NewGuid():N}",
            type = "html",
            adapter,
            baseUrl = url,
            pollIntervalMinutes = 180,
            isActive = false,
            isUnreliable = false,
            autoPublish,
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
        return (id, await RunAsync(admin, id));
    }

    private static async Task<JsonElement> RunAsync(HttpClient admin, Guid id)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/sources/{id}/fetch");
        req.Headers.Add("X-Test-Now", "2026-10-08T07:00:00Z"); // the day the pages were saved: horizon and expiry as on that day
        var run = await admin.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, run.StatusCode);
        return JsonDocument.Parse(await run.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static string SavedPage(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Html", name));

    private static string NewUrl() => $"https://html-{Guid.NewGuid():N}.example/events";

    [Fact]
    public async Task An_html_page_becomes_candidates_with_one_listing_as_evidence()
    {
        var url = NewUrl();
        _pages[url] = """
            <html><body><nav><a href="/menu">Menu</a></nav>
            <ul>
              <li class="card"><a href="/events/jazz-1"><h3>Jazz natë</h3></a> <span>20:00</span> <span>Klubi</span></li>
              <li class="card"><a href="/events/rock-2"><h3>Rock</h3></a></li>
            </ul><script>var x = 1;</script></body></html>
            """;
        var admin = await LoginAsync(CreateClient());

        var (id, run) = await FetchAsync(admin, url);

        Assert.Equal("succeeded", run.GetProperty("status").GetString());
        Assert.Equal(2, run.GetProperty("created").GetInt32());
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var candidates = await db.Candidates.AsNoTracking().Where(c => c.SourceId == id).OrderBy(c => c.SourceUrl).ToListAsync();
        Assert.Equal(2, candidates.Count);
        var jazz = candidates.Single(c => c.SourceUrl.EndsWith("/jazz-1"));
        Assert.Equal("Jazz natë 20:00 Klubi\nURL: " + jazz.SourceUrl, jazz.Evidence);
        Assert.DoesNotContain("Menu", jazz.Evidence);
        Assert.DoesNotContain("var x", jazz.Evidence);
        Assert.Equal("Jazz natë", jazz.Fields.Title);
        Assert.Equal(jazz.SourceUrl, jazz.Fields.Url);
    }

    [Fact]
    public async Task A_page_without_listings_fails_the_run_with_MalformedPayload()
    {
        var url = NewUrl();
        _pages[url] = "<html><body><p>Redesigned site</p></body></html>";
        var admin = await LoginAsync(CreateClient());

        var (_, run) = await FetchAsync(admin, url);

        Assert.Equal("failed", run.GetProperty("status").GetString());
        Assert.Equal("MalformedPayload", run.GetProperty("errorCode").GetString());
        Assert.Equal(0, run.GetProperty("created").GetInt32());
    }

    [Fact]
    public async Task Seeding_creates_the_inactive_known_sources_once_and_leaves_edits_alone()
    {
        await using var factory = app.WithWebHostBuilder(b => b.UseSetting("Seed:KnownSources", "true"));
        using var _ = factory.CreateClient(); // starts the host, which seeds

        async Task<List<Source>> KnownAsync(IServiceProvider services)
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Sources.AsNoTracking()
                .Where(s => s.BaseUrl == "https://prishtina.com/events" || s.BaseUrl == "https://kapodalim.com/ks"
                    || s.BaseUrl == "https://www.foleja.com/foleja-Deals/" || s.BaseUrl == "https://gjirafamall.com/oferta-speciale").ToListAsync();
        }

        var known = await KnownAsync(factory.Services);
        Assert.Equal(4, known.Count);
        Assert.All(known, s =>
        {
            Assert.Equal(SourceType.Html, s.Type);
            Assert.False(s.IsActive);
            Assert.False(s.IsUnreliable);
            Assert.Equal(180, s.PollIntervalMinutes);
        });
        Assert.Equal(HtmlAdapters.PrishtinaEvents, known.Single(s => s.BaseUrl == "https://prishtina.com/events").Adapter);
        Assert.Equal(HtmlAdapters.Kapodalim, known.Single(s => s.BaseUrl == "https://kapodalim.com/ks").Adapter);
        var foleja = known.Single(s => s.BaseUrl == "https://www.foleja.com/foleja-Deals/");
        Assert.Equal((HtmlAdapters.Foleja, ItemKind.Deal), (foleja.Adapter, foleja.DefaultKind));
        var gjirafa = known.Single(s => s.BaseUrl == "https://gjirafamall.com/oferta-speciale");
        Assert.Equal((HtmlAdapters.GjirafaMall, ItemKind.Deal), (gjirafa.Adapter, gjirafa.DefaultKind));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Sources.Where(s => s.BaseUrl == "https://kapodalim.com/ks").ExecuteUpdateAsync(s => s.SetProperty(x => x.Name, "Ka Po Dalim (riemëruar)"));
            await scope.ServiceProvider.GetRequiredService<KnownSourcesSeeder>().SeedAsync();
        }

        var again = await KnownAsync(factory.Services);
        Assert.Equal(4, again.Count);
        Assert.Equal("Ka Po Dalim (riemëruar)", again.Single(s => s.BaseUrl == "https://kapodalim.com/ks").Name);
    }

    [Fact]
    public async Task Seeding_moves_a_gjirafa_mall_row_from_the_site_root_to_the_listing_and_keeps_its_settings()
    {
        await using var factory = app.WithWebHostBuilder(b => b.UseSetting("Seed:KnownSources", "true"));
        using var _ = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Sources.Where(s => s.BaseUrl == "https://gjirafamall.com/oferta-speciale")
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.BaseUrl, "https://gjirafamall.com").SetProperty(x => x.Name, "Gjirafa (emri im)").SetProperty(x => x.IsActive, true));

        await scope.ServiceProvider.GetRequiredService<KnownSourcesSeeder>().SeedAsync();

        var rows = await db.Sources.AsNoTracking().Where(s => s.Adapter == HtmlAdapters.GjirafaMall).ToListAsync();
        var row = rows.Count == 1 ? rows[0] : null;
        // The database is shared with the other tests of the class: put the row back whatever the asserts say.
        try
        {
            Assert.NotNull(row);
            Assert.Equal("https://gjirafamall.com/oferta-speciale", row.BaseUrl);
            Assert.Equal("Gjirafa (emri im)", row.Name);
            Assert.True(row.IsActive);
        }
        finally
        {
            await db.Sources.Where(s => s.Adapter == HtmlAdapters.GjirafaMall)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Name, "Gjirafa Mall — Oferta").SetProperty(x => x.IsActive, false)
                    .SetProperty(x => x.BaseUrl, "https://gjirafamall.com/oferta-speciale"));
        }
    }

    [Theory]
    [InlineData(HtmlAdapters.PrishtinaEvents, "prishtina-events-2026-10-08.html", "/events/")]
    [InlineData(HtmlAdapters.Kapodalim, "kapodalim-ks-2026-10-08.html", "/event/")]
    public async Task A_saved_page_runs_end_to_end_through_the_real_parser_and_a_second_fetch_is_unchanged(
        string adapter, string page, string eventPath)
    {
        var url = NewUrl();
        _pages[url] = SavedPage(page);
        var admin = await LoginAsync(CreateClient(realParsers: true));

        var (id, run) = await FetchAsync(admin, url, adapter);

        Assert.Equal("succeeded", run.GetProperty("status").GetString());
        Assert.Equal(0, run.GetProperty("failed").GetInt32());
        var created = run.GetProperty("created").GetInt32();
        // Items beyond the event horizon are counted in `received` only (REQ-026), so the page size is asserted on that.
        var received = run.GetProperty("received").GetInt32();
        Assert.True(received >= 10 && created >= 5, $"received {received}, created {created}");

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var candidates = await db.Candidates.AsNoTracking().Where(c => c.SourceId == id).ToListAsync();
            Assert.Equal(created, candidates.Count);
            Assert.All(candidates, c =>
            {
                Assert.Contains(eventPath, c.SourceUrl);
                Assert.DoesNotContain("?date=", c.SourceUrl);
                Assert.EndsWith("\nURL: " + c.SourceUrl, c.Evidence);
                Assert.Equal(c.SourceUrl, c.Fields.Url);
            });
            // Reference resolution keeps what the parser mapped (T044 review: the slug used to be overwritten).
            Assert.Contains(candidates, c => c.Fields.CitySlug == "prishtine" && c.Fields.CategorySlug == "muzike");
        }

        // The same page again, as on the next day for Ka' Po Dalim's running series: nothing new, nothing changed.
        _pages[url] = SavedPage(page).Replace("?date=2026-10-08", "?date=2026-10-09", StringComparison.Ordinal);
        var again = await RunAsync(admin, id);
        Assert.Equal("succeeded", again.GetProperty("status").GetString());
        Assert.Equal(0, again.GetProperty("created").GetInt32());
        Assert.Equal(0, again.GetProperty("updated").GetInt32());
    }

    /// <summary>
    /// Shop deals (ADR-045): <c>li.p</c> with an <c>a</c>, an <c>h3</c> and <c>data-old</c>/<c>data-new</c> prices; the
    /// pagination is the <c>a[rel=next]</c> link, which sits in a <c>nav</c> the page cleaning removes.
    /// </summary>
    private sealed class ShopParser : IHtmlSiteParser
    {
        public string Key => HtmlAdapters.Foleja;

        public ItemKind Kind => ItemKind.Deal;

        public IReadOnlyList<IElement> FindListings(IDocument page, CancellationToken ct) => page.QuerySelectorAll("li.p").ToList();

        public HtmlListing Map(IElement listing, Uri pageUrl, DateTimeOffset now) =>
            new(listing.QuerySelector("a")?.GetAttribute("href"), new CandidateFields
            {
                Title = listing.QuerySelector("h3")?.TextContent.Trim(),
                OldPrice = decimal.Parse(listing.GetAttribute("data-old")!, System.Globalization.CultureInfo.InvariantCulture),
                NewPrice = decimal.Parse(listing.GetAttribute("data-new")!, System.Globalization.CultureInfo.InvariantCulture),
                Currency = "EUR",
                MerchantName = "Dyqani",
            });

        public Uri? NextPage(IDocument page, Uri pageUrl, CancellationToken ct) =>
            page.QuerySelector("a[rel=next]")?.GetAttribute("href") is { } href ? new Uri(pageUrl, href) : null;
    }

    private static string ShopPage(string? next, params (string Title, string Slug, decimal Old, decimal New)[] products) =>
        "<html><body>" + (next is null ? "" : $"<nav><a rel='next' href='{next}'>Next</a></nav>") + "<ul>"
        + string.Concat(products.Select(p =>
            FormattableString.Invariant($"<li class='p' data-old='{p.Old}' data-new='{p.New}'><a href='/p/{p.Slug}'><h3>{p.Title}</h3></a></li>")))
        + "</ul></body></html>";

    [Fact]
    public async Task A_paginated_deal_listing_is_followed_page_by_page_and_never_goes_to_the_AI()
    {
        var url = NewUrl();
        var g = Guid.NewGuid().ToString("N")[..8];
        _pages[url] = ShopPage("?p=2", ($"Aspirator {g}", $"a-{g}", 200m, 120m), ($"Televizor {g}", $"t-{g}", 500m, 400m));
        _pages[url + "?p=2"] = ShopPage("?p=3", ($"Hekur {g}", $"h-{g}", 80m, 40m));
        _pages[url + "?p=3"] = ShopPage(null, ($"Mikser {g}", $"m-{g}", 60m, 30m));
        var admin = await LoginAsync(CreateClient(parser: new ShopParser(), maxPages: 2));

        var (id, run) = await FetchAsync(admin, url, HtmlAdapters.Foleja);

        // Two pages allowed (maxPages: 2): the third is not requested.
        Assert.Equal("succeeded", run.GetProperty("status").GetString());
        Assert.Equal(3, run.GetProperty("received").GetInt32());
        Assert.Equal(3, run.GetProperty("created").GetInt32());
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var candidates = await db.Candidates.AsNoTracking().Where(c => c.SourceId == id).ToListAsync();
        Assert.All(candidates, c =>
        {
            Assert.Equal(ItemKind.Deal, c.Kind);
            Assert.Null(c.Fields.ValidUntil);
            Assert.Equal("Dyqani", c.Fields.MerchantName);
            Assert.Empty(c.AiSuggestedFields);
            Assert.Null(c.AiModel);
        });
        Assert.DoesNotContain(candidates, c => c.Fields.Title!.StartsWith("Mikser"));
    }

    [Fact]
    public async Task A_later_page_that_cannot_be_read_keeps_the_pages_already_read()
    {
        var url = NewUrl();
        var g = Guid.NewGuid().ToString("N")[..8];
        _pages[url] = ShopPage("/elsewhere", ($"Aspirator {g}", $"a-{g}", 200m, 120m));
        _pages[url.Replace("/events", "/elsewhere")] = "<html><body><p>Faqja u ndryshua</p></body></html>";
        var admin = await LoginAsync(CreateClient(parser: new ShopParser()));

        var (_, run) = await FetchAsync(admin, url, HtmlAdapters.Foleja);

        Assert.Equal("succeeded", run.GetProperty("status").GetString());
        Assert.Equal(1, run.GetProperty("created").GetInt32());
    }

    private async Task<(Deal Deal, bool Active)> DealStateAsync(string title)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = new DateTimeOffset(2026, 10, 8, 7, 0, 0, TimeSpan.Zero); // the fetch time of RunAsync
        var deal = await db.Deals.AsNoTracking().SingleAsync(d => d.Title == title);
        var active = await db.Deals.AsNoTracking().Where(d => d.Id == deal.Id).Where(ActiveItems.Deal(now)).AnyAsync();
        return (deal, active);
    }

    [Fact]
    public async Task A_listing_deal_ends_when_it_falls_below_35_percent_and_returns_with_the_discount()
    {
        var url = NewUrl();
        var g = Guid.NewGuid().ToString("N")[..8];
        var title = $"Lavatriçe {g}";
        _pages[url] = ShopPage(null, (title, $"l-{g}", 100m, 60m));
        var admin = await LoginAsync(CreateClient(parser: new ShopParser()));

        var (id, first) = await FetchAsync(admin, url, HtmlAdapters.Foleja, autoPublish: true);
        Assert.Equal("succeeded", first.GetProperty("status").GetString());
        var (published, active) = await DealStateAsync(title);
        Assert.Equal(ItemStatus.Published, published.Status);
        Assert.True(active);

        // The shop still lists it, now with 20 % off: no longer today's deal, at once; the status is not touched.
        _pages[url] = ShopPage(null, (title, $"l-{g}", 100m, 80m));
        Assert.Equal("succeeded", (await RunAsync(admin, id)).GetProperty("status").GetString());
        var (delisted, stillActive) = await DealStateAsync(title);
        Assert.Equal(ItemStatus.Published, delisted.Status);
        Assert.False(stillActive);

        // Back to 40 % off: the same item is active again, not a second one.
        _pages[url] = ShopPage(null, (title, $"l-{g}", 100m, 60m));
        Assert.Equal("succeeded", (await RunAsync(admin, id)).GetProperty("status").GetString());
        var (back, activeAgain) = await DealStateAsync(title);
        Assert.Equal(published.Id, back.Id);
        Assert.True(activeAgain);
    }

    [Fact]
    public async Task A_listing_deal_comes_back_when_a_person_rejected_the_lower_priced_change()
    {
        var url = NewUrl();
        var g = Guid.NewGuid().ToString("N")[..8];
        var title = $"Mikrovalë {g}";
        _pages[url] = ShopPage(null, (title, $"v-{g}", 100m, 60m));
        var admin = await LoginAsync(CreateClient(parser: new ShopParser()));
        var (id, _) = await FetchAsync(admin, url, HtmlAdapters.Foleja, autoPublish: true);

        _pages[url] = ShopPage(null, (title, $"v-{g}", 100m, 80m));
        await RunAsync(admin, id);
        Assert.False((await DealStateAsync(title)).Active);

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // What a reviewer's "Refuzo" leaves behind: the change is rejected by a person.
            await db.Candidates.Where(c => c.SourceId == id && c.Status == CandidateStatus.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, CandidateStatus.Rejected).SetProperty(c => c.RejectedByUserId, Guid.NewGuid()));
        }

        _pages[url] = ShopPage(null, (title, $"v-{g}", 100m, 60m));
        await RunAsync(admin, id);

        Assert.True((await DealStateAsync(title)).Active);
    }

    [Fact]
    public async Task A_deal_a_person_expired_is_not_published_again_by_a_later_fetch()
    {
        var url = NewUrl();
        var g = Guid.NewGuid().ToString("N")[..8];
        var title = $"Frigorifer {g}";
        _pages[url] = ShopPage(null, (title, $"f-{g}", 100m, 60m));
        var admin = await LoginAsync(CreateClient(parser: new ShopParser()));
        var (id, _) = await FetchAsync(admin, url, HtmlAdapters.Foleja, autoPublish: true);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Deals.Where(d => d.Title == title).ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, ItemStatus.Expired));
        }

        // A price change that is still 35 % off would be published onto the same item by the system.
        _pages[url] = ShopPage(null, (title, $"f-{g}", 100m, 55m));
        await RunAsync(admin, id);

        var (deal, active) = await DealStateAsync(title);
        Assert.Equal(ItemStatus.Expired, deal.Status);
        Assert.False(active);
        await using var check = app.Services.CreateAsyncScope();
        var pending = await check.ServiceProvider.GetRequiredService<AppDbContext>().Candidates.AsNoTracking()
            .Where(c => c.SourceId == id && c.Status == CandidateStatus.Pending).ToListAsync();
        Assert.Single(pending); // waits for a person
    }

    [Fact]
    public async Task Pagination_never_leaves_the_site_repeats_a_page_or_goes_beyond_the_page_limit()
    {
        var url = NewUrl();
        var g = Guid.NewGuid().ToString("N")[..8];
        _pages[url] = ShopPage("https://elsewhere.example/events?p=2", ($"Aspirator {g}", $"a-{g}", 200m, 120m));
        var admin = await LoginAsync(CreateClient(parser: new ShopParser()));
        var (_, foreign) = await FetchAsync(admin, url, HtmlAdapters.Foleja);
        Assert.Equal(1, foreign.GetProperty("created").GetInt32());
        Assert.DoesNotContain(_requested, r => r.Contains("elsewhere.example"));

        // A loop: page 2 points back to page 1 (with a fragment).
        var loop = NewUrl();
        _pages[loop] = ShopPage("?p=2", ($"Hekur {g}", $"h-{g}", 80m, 40m));
        _pages[loop + "?p=2"] = ShopPage($"{loop}#top", ($"Mikser {g}", $"m-{g}", 60m, 30m));
        _requested.Clear();
        var (_, looped) = await FetchAsync(admin, loop, HtmlAdapters.Foleja);
        Assert.Equal(2, looped.GetProperty("created").GetInt32());
        Assert.Equal(1, _requested.Count(r => r == loop));

        // maxPages 1 turns pagination off.
        var single = NewUrl();
        _pages[single] = ShopPage("?p=2", ($"Televizor {g}", $"t-{g}", 500m, 300m));
        var onePage = await LoginAsync(CreateClient(parser: new ShopParser(), maxPages: 1));
        _requested.Clear();
        var (_, one) = await FetchAsync(onePage, single, HtmlAdapters.Foleja);
        Assert.Equal(1, one.GetProperty("created").GetInt32());
        Assert.DoesNotContain(_requested, r => r.Contains("?p=2"));
    }

    [Fact]
    public async Task A_later_page_with_a_server_error_keeps_the_first_page()
    {
        var url = NewUrl();
        var g = Guid.NewGuid().ToString("N")[..8];
        _pages[url] = ShopPage("?p=2", ($"Aspirator {g}", $"a-{g}", 200m, 120m));
        _serverErrors.Add(url + "?p=2");
        var admin = await LoginAsync(CreateClient(parser: new ShopParser()));

        var (_, run) = await FetchAsync(admin, url, HtmlAdapters.Foleja);

        Assert.Equal("succeeded", run.GetProperty("status").GetString());
        Assert.Equal(1, run.GetProperty("created").GetInt32());
    }


    public void Dispose() => _factories.ForEach(f => f.Dispose());
}
