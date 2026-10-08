using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AngleSharp.Dom;
using App.Application.Sources;
using App.Domain.Content;
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

    private HttpClient CreateClient(bool realParsers = false)
    {
        var factory = app.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            services.RemoveAll<IngestionOptions>();
            services.AddSingleton(new IngestionOptions { AllowFixtureSources = true, HttpRetries = 0, HttpTimeoutSeconds = 5, PerHostDelaySeconds = 0 });
            if (!realParsers)
            {
                services.RemoveAll<IHtmlSiteParser>();
                services.AddSingleton<IHtmlSiteParser, CardParser>();
            }
            services.AddHttpClient<SourceHttpFetcher>(SourceHttpFetcher.ClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new StubHandler(request =>
                {
                    var url = request.RequestUri!.AbsoluteUri;
                    if (url.EndsWith("/robots.txt")) return new HttpResponseMessage(HttpStatusCode.NotFound);
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

    private static async Task<(Guid Id, JsonElement Run)> FetchAsync(HttpClient admin, string url, string adapter = HtmlAdapters.PrishtinaEvents)
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
    public async Task Seeding_creates_the_two_inactive_known_sources_once_and_leaves_edits_alone()
    {
        await using var factory = app.WithWebHostBuilder(b => b.UseSetting("Seed:KnownSources", "true"));
        using var _ = factory.CreateClient(); // starts the host, which seeds

        async Task<List<Source>> KnownAsync(IServiceProvider services)
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Sources.AsNoTracking()
                .Where(s => s.BaseUrl == "https://prishtina.com/events" || s.BaseUrl == "https://kapodalim.com/ks").ToListAsync();
        }

        var known = await KnownAsync(factory.Services);
        Assert.Equal(2, known.Count);
        Assert.All(known, s =>
        {
            Assert.Equal(SourceType.Html, s.Type);
            Assert.False(s.IsActive);
            Assert.False(s.IsUnreliable);
            Assert.Equal(180, s.PollIntervalMinutes);
        });
        Assert.Equal(HtmlAdapters.PrishtinaEvents, known.Single(s => s.BaseUrl == "https://prishtina.com/events").Adapter);
        Assert.Equal(HtmlAdapters.Kapodalim, known.Single(s => s.BaseUrl == "https://kapodalim.com/ks").Adapter);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Sources.Where(s => s.BaseUrl == "https://kapodalim.com/ks").ExecuteUpdateAsync(s => s.SetProperty(x => x.Name, "Ka Po Dalim (riemëruar)"));
            await scope.ServiceProvider.GetRequiredService<KnownSourcesSeeder>().SeedAsync();
        }

        var again = await KnownAsync(factory.Services);
        Assert.Equal(2, again.Count);
        Assert.Equal("Ka Po Dalim (riemëruar)", again.Single(s => s.BaseUrl == "https://kapodalim.com/ks").Name);
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
        Assert.True(created >= 10, $"created {created}");

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

    public void Dispose() => _factories.ForEach(f => f.Dispose());
}
