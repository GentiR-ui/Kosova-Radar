using App.Application.Ingestion;
using App.Infrastructure.Ingestion;

namespace App.UnitTests;

/// <summary>Contract tests for the Ka' Po Dalim parser on the page saved on 2026-10-08 (design.md §6) and hand-made cards.</summary>
public class KapodalimParserTests
{
    private static readonly Guid Source = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Uri Page = new("https://kapodalim.com/ks");
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);

    private static FeedParseResult Parse(string html) =>
        HtmlSourceAdapter.ParseAsync(Source, new KapodalimParser(), Page, html, Now, 100_000, TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();

    private static readonly Lazy<FeedParseResult> Saved = new(() =>
        Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Html", "kapodalim-ks-2026-10-08.html"))));

    private static NormalizedCandidate Item(string slug) =>
        Saved.Value.Items.Single(i => i.Fields.Url!.StartsWith($"https://kapodalim.com/event/{slug}", StringComparison.Ordinal));

    [Fact]
    public void The_saved_page_yields_the_server_rendered_listings_once_each()
    {
        // 9 grid cards + 2 featured cards not in the grid; KosICT15 is in both and counted once. The other events load on demand (V1 takes what is rendered).
        Assert.Null(Saved.Value.ErrorCode);
        Assert.Equal(11, Saved.Value.Items.Count);
        Assert.Empty(Saved.Value.Failures);
    }

    [Fact]
    public void A_grid_card_with_time_city_venue_category_and_free_entry()
    {
        var f = Item("orkestra-simfonike-e-korces-prishtine-2026-10-08").Fields;

        Assert.Equal("Orkestra Simfonike e Korçës", f.Title);
        Assert.Equal(new DateTimeOffset(2026, 10, 8, 19, 30, 0, TimeSpan.FromHours(2)), f.StartsAt);
        Assert.Null(f.EndsAt);
        Assert.Equal("Prishtinë", f.CityText);
        Assert.Equal("Atelje - Pallati i Rinisë dhe Sporteve", f.VenueName);
        Assert.Equal("Muzikë", f.CategoryText);
        Assert.Equal("muzike", f.CategorySlug);
        Assert.Equal("Hyrja falas", f.PriceText);
        Assert.Equal("https://kapodalim.com/event/orkestra-simfonike-e-korces-prishtine-2026-10-08", f.Url); // "?date=" is dropped
    }

    [Fact]
    public void A_time_range_sets_the_end_the_same_day_and_a_price_is_kept()
    {
        var f = Item("nate-me-yje-fondacioni-akili-defy-them-prishtine-2026-10-08").Fields;

        Assert.Equal(new DateTimeOffset(2026, 10, 8, 20, 30, 0, TimeSpan.FromHours(2)), f.StartsAt);
        Assert.Equal(new DateTimeOffset(2026, 10, 8, 22, 30, 0, TimeSpan.FromHours(2)), f.EndsAt);
        Assert.Equal("Falas", f.PriceText);
        Assert.Equal("Defy Them Community", f.VenueName);
    }

    [Fact]
    public void A_ticket_price_is_price_text_and_other_cities_are_read_from_the_badge()
    {
        var f = Item("dua-kinema-jusuf-gervalla-peje-7-16-tetor-2026").Fields;

        Assert.Equal("3 €", f.PriceText);
        Assert.Equal("Pejë", f.CityText);
        Assert.Equal("teater", f.CategorySlug); // Kinema
        // The poster shows today's screening; the series runs 7–16 TETOR (slug), so the fields stay the same every day.
        Assert.Equal(new DateTimeOffset(2026, 10, 7, 19, 0, 0, TimeSpan.FromHours(2)), f.StartsAt);
        Assert.Equal(new DateTimeOffset(2026, 10, 16, 23, 59, 0, TimeSpan.FromHours(2)), f.EndsAt);
    }

    [Fact]
    public void The_same_event_on_another_day_keeps_its_identity_and_content()
    {
        static string Card(string date) => $"""
            <html><body><main><article>
            <a href="/event/dua-kinema-peje-7-16-tetor-2026?date={date}"><h3>Dua Kinema</h3></a>
            <time datetime="{date}"><span>{date[8..]}<br>19:00</span></time>
            <span class="event-card-location">PEJË</span><span class="category-chip">Kinema</span>
            </article></main></body></html>
            """;

        var today = Assert.Single(Parse(Card("2026-10-08")).Items);
        var tomorrow = Assert.Single(Parse(Card("2026-10-09")).Items);

        Assert.Equal("https://kapodalim.com/event/dua-kinema-peje-7-16-tetor-2026", today.Fields.Url);
        Assert.Equal(today.Fingerprint, tomorrow.Fingerprint);
        Assert.Equal(today.ContentHash, tomorrow.ContentHash);
        Assert.Equal(new DateTimeOffset(2026, 10, 7, 19, 0, 0, TimeSpan.FromHours(2)), today.Fields.StartsAt);
    }

    [Theory]
    [InlineData("ylber-osmani-sunshine-prishtine-2026-10-08")]   // "HYRJA TBA", ORARI TBA
    [InlineData("imazhe-migruese-bora-baboci-gkk-qafa-8-tetor-2026")] // "Hyrja nuk është publikuar"
    [InlineData("quiz-night-retroverse-prishtine-8-tetor-2026")] // "Çmimi s’u publikua"
    public void An_unpublished_entry_line_is_not_a_price(string slug) => Assert.Null(Item(slug).Fields.PriceText);

    [Fact]
    public void Time_TBA_leaves_the_start_null_and_the_text_goes_to_the_AI()
    {
        var item = Item("ylber-osmani-sunshine-prishtine-2026-10-08");

        Assert.Null(item.Fields.StartsAt);
        Assert.Equal("muzike", item.Fields.CategorySlug); // Muzikë live
        Assert.NotNull(item.Text);
    }

    [Fact]
    public void A_day_range_ends_on_the_last_day_at_2359_and_a_range_across_months_is_read()
    {
        var kos = Item("kosict15-klan-kosova-9-10-tetor-2026").Fields;
        Assert.Null(kos.StartsAt); // ORARI TBA
        Assert.Equal(new DateTimeOffset(2026, 10, 10, 23, 59, 0, TimeSpan.FromHours(2)), kos.EndsAt);
        Assert.Equal("Fushë Kosovë", kos.CityText);
        Assert.Equal("Klan Kosova - Fushë Kosovë", kos.VenueName);
        Assert.Equal("Teknologji & biznes", kos.CategoryText);

        var remusica = Item("remusica-edition-xxv-26-tetor-1-nentor-2026").Fields;
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 23, 59, 0, TimeSpan.FromHours(1)), remusica.EndsAt); // winter offset
        Assert.Equal("Prishtinë - lokacionet TBA", remusica.VenueName);
    }

    [Fact]
    public void Open_ended_start_text_and_featured_cards_map_category_and_city()
    {
        var mitro = Item("mitrorun-mitrovice-18-tetor-2026").Fields;

        Assert.Equal("Mitrovicë", mitro.CityText);
        Assert.Equal("Sheshi Adem Jashari", mitro.VenueName);
        Assert.Equal("VRAPIM", mitro.CategoryText);
        Assert.Null(mitro.CategorySlug);
        Assert.Equal("MitroRun 2026", mitro.Title);
        Assert.Equal(new DateTimeOffset(2026, 10, 18, 10, 0, 0, TimeSpan.FromHours(2)), mitro.StartsAt); // "NGA 10:00"
    }

    [Fact]
    public void Evidence_is_the_one_card_with_the_link()
    {
        var item = Item("quiz-night-retroverse-prishtine-8-tetor-2026");

        Assert.Contains("Quiz Night", item.Evidence);
        Assert.Contains("Retroverse", item.Evidence);
        Assert.EndsWith("URL: https://kapodalim.com/event/quiz-night-retroverse-prishtine-8-tetor-2026", item.Evidence);
        Assert.DoesNotContain("MitroRun", item.Evidence);
    }

    [Fact]
    public void A_hand_made_card_with_only_a_title_and_a_link_keeps_the_other_fields_null()
    {
        const string html = """
            <html><body><main>
            <article><a href="/event/mini-2026-11-02"><h3>Mini Event</h3></a></article>
            <a href="/about">About</a>
            </main></body></html>
            """;

        var item = Assert.Single(Parse(html).Items);

        Assert.Equal("Mini Event", item.Fields.Title);
        Assert.Null(item.Fields.StartsAt);
        Assert.Null(item.Fields.EndsAt);
        Assert.Null(item.Fields.CityText);
        Assert.Null(item.Fields.VenueName);
        Assert.Null(item.Fields.CategoryText);
        Assert.Null(item.Fields.PriceText);
        Assert.Null(item.Fields.ImageUrl);
    }

    [Fact]
    public void A_grid_card_wins_over_a_featured_card_and_its_venue_is_the_poster_venue()
    {
        const string html = """
            <a href="/event/e-2026-11-02" class="pick"><h3>E</h3><span>01 / PRISHTINË / SPORT / MEET</span></a>
            <article><a href="/event/e-2026-11-02"><div class="poster"><span class="poster-venue">Hall A</span><time datetime="2026-11-02"><b>02</b><span>NËNTOR<br/>22:00–02:00</span></time></div></a>
            <div class="event-card-location"><i></i>Prishtinë</div><h3>E</h3><p>Hall A · Prishtinë</p></article>
            """;

        var item = Assert.Single(Parse(html).Items);

        Assert.Equal("Hall A", item.Fields.VenueName);
        Assert.Equal("Prishtinë", item.Fields.CityText);
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 22, 0, 0, TimeSpan.FromHours(1)), item.Fields.StartsAt);
        Assert.Null(item.Fields.EndsAt); // overnight clock range: end ignored
    }

    [Fact]
    public void A_day_range_across_the_new_year_ends_in_the_next_year()
    {
        const string html = """
            <article><a href="/event/y"><time datetime="2026-12-28"><b>28</b><span>DHJETOR<br/>28 DHJETOR-3 JANAR · 18:00</span></time><h3>Y</h3></a></article>
            """;

        var f = Assert.Single(Parse(html).Items).Fields;

        Assert.Equal(new DateTimeOffset(2026, 12, 28, 18, 0, 0, TimeSpan.FromHours(1)), f.StartsAt);
        Assert.Equal(new DateTimeOffset(2027, 1, 3, 23, 59, 0, TimeSpan.FromHours(1)), f.EndsAt);
    }

    [Theory]
    [InlineData("Koncert", "muzike")]
    [InlineData("Teatër", "teater")]
    [InlineData("Ekspozitë", "art")]
    [InlineData("Sport", "sport")]
    [InlineData("Fëmijë", "familje")]
    [InlineData("Food & Drinks", "ushqim")]
    [InlineData("DJ Night", "nate")]
    [InlineData("Party", "nate")]
    [InlineData("Workshop", "edukim")]
    [InlineData("Komunitet", null)]
    [InlineData("Lojëra", null)]
    public void Categories_map_per_the_design(string text, string? slug) => Assert.Equal(slug, KapodalimParser.MapCategory(text));

    [Fact]
    public void A_page_without_event_links_is_malformed()
    {
        Assert.Equal("MalformedPayload", Parse("<html><body><a href='/about'>About</a></body></html>").ErrorCode);
    }

    [Fact]
    public void Many_links_to_the_same_event_in_one_card_are_found_in_linear_time()
    {
        // Security review (release): each repeated link used to rescan the whole card (820 KB took 125 s).
        var links = string.Concat(Enumerable.Repeat("<a href=\"/event/a\">x</a>", 40_000));
        var html = $"<html><body><main><article><h3>A</h3>{links}</article></main></body></html>";

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = Parse(html);

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"took {watch.Elapsed}");
        Assert.Single(result.Items);
    }

    [Fact]
    public async Task A_page_that_takes_too_long_to_map_fails_as_malformed_within_the_deadline()
    {
        // 1000 nested cards sharing one large subtree: every card's text and selectors walk all of it.
        var cards = string.Concat(Enumerable.Range(0, 1000).Select(i => $"<article><a href=\"/event/e{i}\"><h3>E{i}</h3></a>"));
        var html = $"<html><body><main>{cards}{string.Concat(Enumerable.Repeat("<p>x</p>", 100_000))}"
                   + string.Concat(Enumerable.Repeat("</article>", 1000)) + "</main></body></html>";

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = await HtmlSourceAdapter.ParseAsync(Source, new KapodalimParser(), Page, html, Now, 100_000, TimeSpan.FromSeconds(1));

        // Parsing alone may use its own 1 s, then cleaning, finding and mapping get 1 s more.
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(6), $"took {watch.Elapsed}");
        Assert.Equal(FeedParseResult.Malformed.ErrorCode, result.ErrorCode);
    }
}
