using App.Application.Ingestion;
using App.Infrastructure.Ingestion;

namespace App.UnitTests;

/// <summary>Contract tests for the Prishtina.com parser on the page saved on 2026-10-08 (design.md §6) and a hand-made page.</summary>
public class PrishtinaEventsParserTests
{
    private static readonly Guid Source = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Uri Page = new("https://prishtina.com/events");
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);

    private static FeedParseResult Parse(string html, int horizonDays = 100_000) =>
        HtmlSourceAdapter.ParseAsync(Source, new PrishtinaEventsParser(), Page, html, Now, horizonDays, TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();

    private static readonly Lazy<FeedParseResult> Saved = new(() =>
        Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Html", "prishtina-events-2026-10-08.html"))));

    private static NormalizedCandidate Item(string slug) =>
        Saved.Value.Items.Single(i => i.Fields.Url == $"https://prishtina.com/events/{slug}");

    [Fact]
    public void A_card_nested_thousands_of_levels_deep_is_a_malformed_payload_not_a_crash()
    {
        var html = $"<html><body><a href=\"/events/x-2026-10-10-abcdef\"><h3>X</h3>{HtmlSourceAdapterTests.Nested("i", 20_000)}y</a></body></html>";

        Assert.Equal("MalformedPayload", HtmlSourceAdapterTests.OnThreadPoolStack(() => Parse(html)).ErrorCode);
    }

    [Fact]
    public void The_saved_page_yields_all_its_listings_without_failures()
    {
        Assert.Null(Saved.Value.ErrorCode);
        Assert.Equal(78, Saved.Value.Items.Count);
        Assert.Empty(Saved.Value.Failures);
    }

    [Fact]
    public void A_card_with_time_gets_the_start_from_url_date_and_card_time_in_Pristina_time()
    {
        var f = Item("gjindemi-mire-2026-10-08-6d3800").Fields;

        Assert.Equal("Gjindemi Mirë", f.Title);
        Assert.Equal(new DateTimeOffset(2026, 10, 8, 19, 0, 0, TimeSpan.FromHours(2)), f.StartsAt);
        Assert.Equal("Ish Biblioteka ‘Hivzi Sulejmani’ (Te Qafa)", f.VenueName);
        Assert.Equal("Prishtinë", f.CityText);
        Assert.Equal("Culture & Arts", f.CategoryText);
        Assert.Equal("art", f.CategorySlug);
        Assert.StartsWith("https://", f.ImageUrl);
    }

    [Fact]
    public void The_winter_offset_is_used_after_the_clock_change()
    {
        var f = Item("the-fall-gathering-26-2026-10-31-ea049b").Fields;

        Assert.Equal(new DateTimeOffset(2026, 10, 31, 21, 0, 0, TimeSpan.FromHours(1)), f.StartsAt); // DST ends on 25 Oct
        Assert.Equal("nate", f.CategorySlug);
        Assert.Equal("Defy Them", f.VenueName);
    }

    [Fact]
    public void A_card_without_time_has_no_start_and_its_venue_is_kept_and_the_text_goes_to_the_AI()
    {
        var item = Item("prishtina-mall-street-fest-2026-10-09-694553");

        Assert.Null(item.Fields.StartsAt);
        Assert.Equal("Prishtina Mall", item.Fields.VenueName);
        Assert.NotNull(item.Text);
    }

    [Fact]
    public void Music_clubs_food_tech_and_other_map_per_the_design()
    {
        Assert.Equal("muzike", Item("feel-the-groove-2026-10-08-d13e4d").Fields.CategorySlug);
        Assert.Equal("nate", Item("quiz-night-2026-10-08-065492").Fields.CategorySlug);
        Assert.Equal("ushqim", Item("beerfest-kosova-2027-2027-07-09-a8e5a1").Fields.CategorySlug);
        Assert.Equal("edukim", Item("gameher-s-workshop-game-jam-2026-10-09-f8da8b").Fields.CategorySlug);
        Assert.Null(Item("echo-e-gjirit-falas-2026-10-10-04b97f").Fields.CategorySlug);
        Assert.Equal("Other", Item("echo-e-gjirit-falas-2026-10-10-04b97f").Fields.CategoryText);
        Assert.NotNull(Item("echo-e-gjirit-falas-2026-10-10-04b97f").Text); // shown but unmapped: the AI may suggest a category
    }

    [Fact]
    public void Culture_and_arts_is_teater_when_the_card_mentions_a_theatre_and_the_festival_label_is_not_the_category()
    {
        var play = Item("gerrdia-2026-10-09-535200");
        Assert.Equal("teater", play.Fields.CategorySlug);
        Assert.Equal("National Theatre of Kosovo", play.Fields.VenueName);

        var fest = Item("beerfest-kosova-2027-2027-07-09-a8e5a1").Fields;
        Assert.Equal("Food & Drinks", fest.CategoryText);
        Assert.Equal("BeerFest Kosova 2027", fest.Title);
    }

    [Fact]
    public void Evidence_is_the_one_card_with_the_link()
    {
        var item = Item("quiz-night-2026-10-08-065492");

        Assert.Contains("Quiz Night", item.Evidence);
        Assert.Contains("Retroverse", item.Evidence);
        Assert.EndsWith("URL: https://prishtina.com/events/quiz-night-2026-10-08-065492", item.Evidence);
        Assert.DoesNotContain("Rovena", item.Evidence);
    }

    [Fact]
    public void A_hand_made_card_with_missing_time_venue_and_category_keeps_those_fields_null_and_the_plus_more_title()
    {
        const string html = """
            <html><body><main><section>
            <a href="/events/mini-2026-11-02-abcdef"><div><div>Mon, 2 Nov</div></div><div><h3>Mini Fest + 3 more</h3></div></a>
            <a href="/about">About</a>
            </section></main></body></html>
            """;

        var result = Parse(html);

        var item = Assert.Single(result.Items);
        Assert.Equal("Mini Fest + 3 more", item.Fields.Title);
        Assert.Null(item.Fields.StartsAt);
        Assert.Null(item.Fields.VenueName);
        Assert.Null(item.Fields.CategoryText);
        Assert.Null(item.Fields.CategorySlug);
        Assert.Null(item.Fields.ImageUrl);
        Assert.Equal("Prishtinë", item.Fields.CityText);
    }

    [Fact]
    public void A_page_without_event_links_is_malformed()
    {
        Assert.Equal("MalformedPayload", Parse("<html><body><a href='/about'>About</a></body></html>").ErrorCode);
    }

    [Fact]
    public void Culture_and_arts_is_teater_for_a_film_card_and_art_otherwise()
    {
        static string Card(string title) =>
            $"<a href='/events/x-2026-11-02-abcdef'><div>Mon, 2 Nov</div><p>Culture &amp; Arts</p><h3>{title}</h3><p>20:00</p><p>Hall</p></a>";

        Assert.Equal("teater", Parse(Card("Kino Night")).Items.Single().Fields.CategorySlug);
        Assert.Equal("teater", Parse(Card("Film Premiere")).Items.Single().Fields.CategorySlug);
        Assert.Equal("art", Parse(Card("Exhibition")).Items.Single().Fields.CategorySlug);
    }
}
