using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using App.Domain.Content;
using App.Domain.Sources;

namespace App.Infrastructure.Ingestion;

/// <summary>
/// https://prishtina.com/events (design.md §6, ADR-035). Cards are the links <c>/events/{slug}-{YYYY-MM-DD}-{6 hex}</c>;
/// inside a card the category is the paragraph before the <c>h3</c> title, the time (optional) and the venue are the
/// paragraphs after it. Generated CSS class names are never used.
/// </summary>
internal sealed partial class PrishtinaEventsParser : IHtmlSiteParser
{
    private static readonly TimeZoneInfo Pristina = TimeZoneInfo.FindSystemTimeZoneById("Europe/Belgrade");

    public string Key => HtmlAdapters.PrishtinaEvents;

    public IReadOnlyList<IElement> FindListings(IDocument page) =>
        page.QuerySelectorAll("a[href]").Where(a => EventPath().IsMatch(a.GetAttribute("href")!)).ToList();

    public HtmlListing Map(IElement listing, Uri pageUrl, DateTimeOffset now)
    {
        var href = listing.GetAttribute("href")!;
        var title = NullIfEmpty(Collapse(listing.QuerySelector("h3")?.TextContent ?? "")) ?? NullIfEmpty(Collapse(listing.QuerySelector("img")?.GetAttribute("alt") ?? ""));

        var paragraphs = listing.QuerySelectorAll("p").ToList();
        var h3 = listing.QuerySelector("h3");
        string? category = null, time = null, venue = null;
        foreach (var p in paragraphs)
        {
            var text = Collapse(p.TextContent);
            if (text.Length == 0) continue;
            if (h3 is not null && (p.CompareDocumentPosition(h3) & DocumentPositions.Following) != 0) category = text; // before the title
            else if (time is null && ClockTime().IsMatch(text)) time = text;
            else venue ??= text;
        }

        DateTimeOffset? startsAt = null;
        var m = EventPath().Match(href);
        if (m.Success && DateOnly.TryParseExact(m.Groups["date"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            var local = time is not null && TimeOnly.TryParseExact(time, ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
                ? date.ToDateTime(t)
                : (DateTime?)null;
            if (local is { } l) startsAt = new DateTimeOffset(l, Pristina.GetUtcOffset(l));
        }

        var image = listing.QuerySelector("img")?.GetAttribute("src");
        return new HtmlListing(href, new CandidateFields
        {
            Title = title,
            StartsAt = startsAt,
            CityText = "Prishtinë",
            VenueName = venue,
            CategoryText = category,
            CategorySlug = MapCategory(category, Collapse(listing.TextContent)),
            ImageUrl = image is not null && Uri.TryCreate(pageUrl, image, out var img) && img.Scheme is "http" or "https" ? img.AbsoluteUri : null,
        });
    }

    internal static string? MapCategory(string? category, string cardText) => category switch
    {
        "Music" => "muzike",
        "Clubs" => "nate",
        "Food & Drinks" => "ushqim",
        "Tech & Business" => "edukim",
        "Culture & Arts" => TheatreWords().IsMatch(cardText) ? "teater" : "art",
        _ => null,
    };

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;

    private static string Collapse(string s) => Whitespace().Replace(s, " ").Trim();

    [GeneratedRegex(@"^/events/[^/?#]+-(?<date>\d{4}-\d{2}-\d{2})-[0-9a-f]{6}$")]
    private static partial Regex EventPath();

    [GeneratedRegex(@"^\d{1,2}:\d{2}$")]
    private static partial Regex ClockTime();

    [GeneratedRegex(@"film|kino|theat|teat", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TheatreWords();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
