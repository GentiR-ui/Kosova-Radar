using AngleSharp.Dom;
using App.Application.Ingestion;
using App.Domain.Content;
using App.Domain.Reference;
using App.Infrastructure.Ingestion;

namespace App.UnitTests;

/// <summary>The cleaning, evidence, horizon and zero-listings rules of design.md §6 with a stub site parser.</summary>
public class HtmlSourceAdapterTests
{
    private static readonly Guid Source = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Uri Page = new("https://events.example/list");
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Listings are <c>li.card</c>; title in <c>h3</c>, link in <c>a</c>, optional <c>data-start</c>/<c>data-city</c>/<c>data-cat</c> attributes.</summary>
    private sealed class StubParser : IHtmlSiteParser
    {
        public string Key => "stub";

        public IReadOnlyList<IElement> FindListings(IDocument page, CancellationToken ct) => page.QuerySelectorAll("li.card").ToList();

        public HtmlListing Map(IElement listing, Uri pageUrl, DateTimeOffset now)
        {
            if (listing.HasAttribute("data-boom")) throw new FormatException("bad card");
            if (listing.HasAttribute("data-null")) throw new NullReferenceException();
            if (listing.HasAttribute("data-nul")) return new HtmlListing("/e/nul", new CandidateFields { Title = "A\0B" });
            if (listing.HasAttribute("data-img")) return new HtmlListing("/e/img", new CandidateFields { Title = "Img", ImageUrl = "javascript:alert(1)" });
            return new HtmlListing(listing.QuerySelector("a")?.GetAttribute("href"), new CandidateFields
            {
                Title = listing.QuerySelector("h3")?.TextContent.Trim(),
                StartsAt = listing.GetAttribute("data-start") is { } s ? DateTimeOffset.Parse(s) : null,
                CityText = listing.GetAttribute("data-city"),
                CategorySlug = listing.GetAttribute("data-cat"),
            });
        }
    }

    private static FeedParseResult Parse(string body, int horizonDays = 30) =>
        ParseHtml($"<html><body><ul>{body}</ul></body></html>", horizonDays);

    private static FeedParseResult ParseHtml(string html, int horizonDays = 30, TimeSpan? timeout = null) =>
        HtmlSourceAdapter.ParseAsync(Source, new StubParser(), Page, html, Now, horizonDays, timeout ?? TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();

    private static string Card(string title, string href, string? start = "2026-10-20T19:00:00+02:00", string? city = "Prishtinë", string? cat = "muzike", string extra = "")
    {
        var attrs = string.Concat(
            start is null ? "" : $" data-start='{start}'",
            city is null ? "" : $" data-city='{city}'",
            cat is null ? "" : $" data-cat='{cat}'");
        return $"<li class='card'{attrs}><a href='{href}'><h3>{title}</h3></a><span>Salla e madhe</span>{extra}</li>";
    }

    [Fact]
    public void A_listing_becomes_one_event_with_its_own_cleaned_text_and_url_as_evidence()
    {
        var result = Parse(Card("Koncert", "/e/1", extra: "<script>alert(1)</script><style>x{}</style>"));

        Assert.Null(result.ErrorCode);
        Assert.Equal(1, result.Received);
        var item = Assert.Single(result.Items);
        Assert.Equal(ItemKind.Event, item.Kind);
        Assert.Equal("https://events.example/e/1", item.ExternalId);
        Assert.Equal("https://events.example/e/1", item.SourceUrl);
        Assert.Equal("https://events.example/e/1", item.Fields.Url);
        Assert.Equal("Koncert", item.Fields.Title);
        Assert.Equal("Koncert Salla e madhe\nURL: https://events.example/e/1", item.Evidence);
        Assert.DoesNotContain("alert", item.Evidence);
    }

    [Fact]
    public void Text_nodes_of_neighbouring_elements_are_separated_and_whitespace_is_collapsed()
    {
        var result = Parse("<li class='card'><a href='/e/1'><h3>Fri,  11 Sep</h3></a><b>14:30</b><i>Klubi</i>" + "\n\n  <p>   Nata   </p></li>");

        Assert.Equal("Fri, 11 Sep 14:30 Klubi Nata\nURL: https://events.example/e/1", result.Items.Single().Evidence);
    }

    [Fact]
    public void Navigation_header_footer_forms_and_ads_are_removed_before_the_parser_runs()
    {
        const string html = """
            <html><body><header><li class="card"><a href="/h">H</a></li></header>
            <nav><li class="card"><a href="/n">N</a></li></nav>
            <footer><li class="card"><a href="/f">F</a></li></footer>
            <div class="ad-advert"><li class="card"><a href="/a">A</a></li></div>
            <form><li class="card"><a href="/x">X</a></li></form>
            <ul><li class="card" data-start="2026-10-20T19:00:00+02:00" data-city="Prishtinë" data-cat="muzike"><a href="/ok"><h3>Ok</h3></a></li></ul>
            </body></html>
            """;

        var result = ParseHtml(html);

        Assert.Equal(["https://events.example/ok"], result.Items.Select(i => i.SourceUrl));
    }

    [Fact]
    public void Everything_known_means_no_ai_text()
    {
        var item = Parse(Card("Koncert", "/e/1")).Items.Single();

        Assert.Null(item.Text);
    }

    [Theory]
    [InlineData(null, "Prishtinë", "muzike")]
    [InlineData("2026-10-20T19:00:00+02:00", null, "muzike")]
    [InlineData("2026-10-20T19:00:00+02:00", "Prishtinë", null)]
    public void A_missing_start_city_or_category_sends_the_listing_text_to_the_ai(string? start, string? city, string? category)
    {
        var item = Parse(Card("Koncert", "/e/1", start, city, category)).Items.Single();

        Assert.Equal("Koncert Salla e madhe", item.Text);
    }

    [Fact]
    public void Listings_beyond_the_horizon_are_skipped_but_counted_as_received_and_past_ones_are_kept()
    {
        // The horizon is a local date: all of the last day (23:30 Pristina) is inside, the next day (00:30) is not.
        var result = Parse(
            Card("Sot", "/e/1", "2026-10-08T20:00:00+02:00")
            + Card("Dje", "/e/2", "2026-10-01T20:00:00+02:00")
            + Card("Brenda", "/e/3", "2026-11-07T23:30:00+01:00")
            + Card("Larg", "/e/4", "2026-11-08T00:30:00+01:00"));

        Assert.Equal(4, result.Received);
        Assert.Equal(["Sot", "Dje", "Brenda"], result.Items.Select(i => i.Fields.Title));
    }

    [Fact]
    public void The_horizon_comes_from_the_setting()
    {
        var result = Parse(Card("A", "/e/1", "2026-10-12T19:00:00+02:00") + Card("B", "/e/2", "2026-10-20T19:00:00+02:00"), horizonDays: 7);

        Assert.Equal(["A"], result.Items.Select(i => i.Fields.Title));
        Assert.Equal(2, result.Received);
    }

    [Fact]
    public void A_page_without_listings_is_a_malformed_payload()
    {
        var result = ParseHtml("<html><body><p>Hello</p></body></html>");

        Assert.Equal("MalformedPayload", result.ErrorCode);
    }

    [Fact]
    public void A_blank_page_is_a_malformed_payload() =>
        Assert.Equal("MalformedPayload", ParseHtml("  ").ErrorCode);

    [Fact]
    public void Per_item_failures_are_counted_and_the_other_listings_are_kept()
    {
        var result = Parse(
            Card("Ok", "/e/1")
            + """<li class="card"><h3>Pa lidhje</h3></li>"""
            + Card("Skema", "javascript:alert(1)")
            + """<li class="card" data-boom="1"><a href="/e/9">Boom</a></li>"""
            + Card("Ok 2", "https://other.example/e/2"));

        Assert.Equal(5, result.Received);
        Assert.Equal(["Ok", "Ok 2"], result.Items.Select(i => i.Fields.Title));
        Assert.Equal(["MissingIdentity", "InvalidField:url", "InvalidListing"], result.Failures.Select(f => f.Reason));
        Assert.Equal("https://other.example/e/2", result.Items[1].SourceUrl);
    }

    [Fact]
    public void A_parser_crash_or_unstorable_text_fails_that_listing_only()
    {
        var result = Parse(
            Card("Ok", "/e/1")
            + "<li class='card' data-null='1'><a href='/e/2'>x</a></li>"
            + "<li class='card' data-nul='1'><a href='/e/nul'>x</a></li>"
            + "<li class='card' data-img='1'><a href='/e/img'>x</a></li>");

        Assert.Equal(["Ok"], result.Items.Select(i => i.Fields.Title));
        Assert.Equal(4, result.Received);
        Assert.Equal(["InvalidListing", "InvalidField:text", "InvalidField:text"], result.Failures.Select(f => f.Reason));
    }

    [Fact]
    public void A_url_longer_than_the_external_id_limit_fails_that_listing_only()
    {
        var result = Parse(Card("Ok", "/e/1") + Card("Gjatë", "/e/" + new string('x', 250)));

        Assert.Equal(["Ok"], result.Items.Select(i => i.Fields.Title));
        Assert.Equal(["InvalidField:externalId"], result.Failures.Select(f => f.Reason));
        Assert.Equal(2, result.Received);
    }

    [Fact]
    public void A_page_that_takes_too_long_to_parse_is_a_malformed_payload()
    {
        var nested = string.Concat(Enumerable.Repeat("<div>", 60_000));

        var result = ParseHtml(nested, timeout: TimeSpan.FromMilliseconds(200));

        Assert.Equal("MalformedPayload", result.ErrorCode);
    }

    [Fact]
    public void A_page_with_too_many_elements_to_remove_is_a_malformed_payload_without_removing_them()
    {
        var hidden = string.Concat(Enumerable.Repeat("<br hidden>", HtmlSourceAdapter.MaxRemovedElements + 1));
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var result = Parse(hidden + Card("Koncert", "/e/1"));

        Assert.Equal("MalformedPayload", result.ErrorCode);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"took {watch.Elapsed}");
    }

    /// <summary>
    /// Runs <paramref name="parse"/> on a 1.5 MB stack (the Linux thread-pool default). .NET cannot catch a stack overflow:
    /// a recursive AngleSharp call on a deeply nested page would kill the test host, as it would kill the API.
    /// </summary>
    internal static FeedParseResult OnThreadPoolStack(Func<FeedParseResult> parse)
    {
        FeedParseResult? result = null;
        var thread = new Thread(() => result = parse(), 1536 * 1024);
        thread.Start();
        thread.Join();
        return result!;
    }

    internal static string Nested(string tag, int depth) => string.Concat(Enumerable.Repeat($"<{tag}>", depth));

    [Fact]
    public void A_page_nested_deeper_than_the_limit_is_a_malformed_payload()
    {
        var result = OnThreadPoolStack(() => Parse(Card("Koncert", "/e/1", city: null, extra: Nested("i", 20_000) + "Thellë")));

        Assert.Equal("MalformedPayload", result.ErrorCode);
    }

    [Fact]
    public void A_page_nested_below_the_limit_is_read_and_its_text_goes_to_the_AI()
    {
        var result = OnThreadPoolStack(() => Parse(Card("Koncert", "/e/1", city: null, extra: Nested("i", HtmlSourceAdapter.MaxDepth - 20) + "Thellë")));

        Assert.Contains("Thellë", Assert.Single(result.Items).Text);
    }

    [Fact]
    public void A_page_at_the_removal_cap_is_still_read()
    {
        var hidden = string.Concat(Enumerable.Repeat("<br hidden>", HtmlSourceAdapter.MaxRemovedElements - 1));

        Assert.Equal("Koncert", Parse(hidden + Card("Koncert", "/e/1")).Items.Single().Fields.Title);
    }

    [Fact]
    public void Listings_past_the_cap_are_not_read_and_reported_once()
    {
        var result = Parse(string.Concat(Enumerable.Range(0, HtmlSourceAdapter.MaxListings + 50).Select(i => Card($"E{i}", $"/e/{i}"))));

        Assert.Equal(HtmlSourceAdapter.MaxListings, result.Items.Count);
        Assert.Equal(["TooManyItems"], result.Failures.Select(f => f.Reason));
    }

    [Theory]
    [InlineData("https://user:pw@evil.example/x")]
    [InlineData("https://prishtina.com@evil.example/")]
    public void Links_with_credentials_fail_that_listing(string href)
    {
        var result = Parse(Card("Ok", "/e/1") + Card("Keq", href));

        Assert.Equal(["Ok"], result.Items.Select(i => i.Fields.Title));
        Assert.Equal(["InvalidField:url"], result.Failures.Select(f => f.Reason));
    }

    [Fact]
    public void A_listing_exactly_at_the_horizon_is_kept()
    {
        var result = Parse(Card("Kufi", "/e/1", Now.AddDays(30).ToString("O")));

        Assert.Single(result.Items);
    }

    [Fact]
    public void The_same_event_link_twice_is_one_listing()
    {
        var result = Parse(Card("Koncert", "/e/1") + Card("Koncert", "/e/1#again"));

        Assert.Equal("https://events.example/e/1", Assert.Single(result.Items).SourceUrl);
        Assert.Equal(1, result.Received);
    }

    [Fact]
    public void Same_input_gives_the_same_fingerprint_and_hash_and_a_changed_card_a_new_hash()
    {
        var a = Parse(Card("Koncert", "/e/1")).Items.Single();
        var b = Parse(Card("Koncert", "/e/1")).Items.Single();
        var changed = Parse(Card("Koncert", "/e/1", "2026-10-21T19:00:00+02:00")).Items.Single();

        Assert.Equal(a.Fingerprint, b.Fingerprint);
        Assert.Equal(a.ContentHash, b.ContentHash);
        Assert.Equal(a.Fingerprint, changed.Fingerprint);
        Assert.NotEqual(a.ContentHash, changed.ContentHash);
    }

    [Fact]
    public void Display_only_card_text_does_not_change_the_hash()
    {
        var a = Parse(Card("Koncert", "/e/1", extra: "<span>08 TETOR</span>")).Items.Single();
        var b = Parse(Card("Koncert", "/e/1", extra: "<span>09 TETOR</span>")).Items.Single();

        Assert.NotEqual(a.Evidence, b.Evidence);
        Assert.Equal(a.ContentHash, b.ContentHash); // fields and AI text decide (ADR-042)
    }

    [Fact]
    public void Hidden_text_reaches_neither_the_evidence_nor_the_ai()
    {
        var item = Parse(Card("Koncert", "/e/1", city: null, extra:
            "<span hidden>H1</span><span style='display: none'>H2</span><span style='VISIBILITY:hidden'>H3</span><span>Shown</span>")).Items.Single();

        Assert.Contains("Shown", item.Evidence);
        Assert.NotNull(item.Text);
        Assert.DoesNotContain("H1", item.Text);
        Assert.DoesNotContain("H2", item.Text);
        Assert.DoesNotContain("H3", item.Text);
    }

    [Fact]
    public void A_shown_but_unmapped_category_sends_the_text_to_the_ai()
    {
        Assert.NotNull(Parse(Card("Koncert", "/e/1", cat: null)).Items.Single().Text);
    }

    [Fact]
    public void A_field_longer_than_an_edit_allows_fails_that_listing_only()
    {
        var result = Parse(Card(new string('T', 301), "/e/long") + Card("Koncert", "/e/1"));

        Assert.Equal("Koncert", Assert.Single(result.Items).Fields.Title);
        Assert.Equal("InvalidField:text", Assert.Single(result.Failures).Reason);
    }

    [Fact]
    public void Evidence_text_is_capped()
    {
        var item = Parse(Card("Gjatë", "/e/1", extra: $"<p>{new string('a', 50_000)}</p>")).Items.Single();

        Assert.True(item.Evidence.Length < 17_000);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(item.Evidence) <= 64 * 1024);
        Assert.EndsWith("\nURL: https://events.example/e/1", item.Evidence);
    }
}
