using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using App.Domain.Content;
using App.Domain.Sources;

namespace App.Infrastructure.Ingestion;

/// <summary>
/// https://kapodalim.com/ks (design.md §6, ADR-035). Listings are the server-rendered grid cards (<c>article</c> with a
/// link to <c>/event/{slug}</c>) and the featured "pick" cards (a link containing an <c>h3</c>); the same event may appear
/// in both, so listings are de-duplicated by path. The start date is the <c>datetime</c> of the poster's <c>time</c>
/// element; the text after its <c>br</c> carries the time or the range ("9–10 TETOR · ORARI TBA"). Generated class
/// names are never used.
/// </summary>
internal sealed partial class KapodalimParser : IHtmlSiteParser
{
    private static readonly TimeZoneInfo Pristina = TimeZoneInfo.FindSystemTimeZoneById("Europe/Belgrade");

    private static readonly string[] Months = ["janar", "shkurt", "mars", "prill", "maj", "qershor", "korrik", "gusht", "shtator", "tetor", "nentor", "dhjetor"];

    public string Key => HtmlAdapters.Kapodalim;

    public IReadOnlyList<IElement> FindListings(IDocument page)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<IElement>();
        foreach (var a in page.QuerySelectorAll("a[href]"))
        {
            var path = PathOf(a.GetAttribute("href")!);
            if (path is null) continue;
            var card = a.Closest("article") ?? (a.QuerySelector("h3") is not null ? a : null);
            if (card is null) continue;
            if (!seen.Add(path))
            {
                // The grid card (article) is richer than a featured card: let it replace one found earlier.
                var at = result.FindIndex(r => PathIn(r) == path);
                if (at >= 0 && card.LocalName == "article" && result[at].LocalName != "article") result[at] = card;
                continue;
            }
            result.Add(card);
        }
        return result;
    }

    private static string? PathIn(IElement card) =>
        card.LocalName == "a" ? PathOf(card.GetAttribute("href")!) : card.QuerySelectorAll("a[href]").Select(a => PathOf(a.GetAttribute("href")!)).FirstOrDefault(p => p is not null);

    public HtmlListing Map(IElement listing, Uri pageUrl, DateTimeOffset now)
    {
        var link = listing.LocalName == "a" ? listing : listing.QuerySelectorAll("a[href]").FirstOrDefault(a => PathOf(a.GetAttribute("href")!) is not null);
        var href = link?.GetAttribute("href");
        var path = href is null ? null : PathOf(href);

        var title = NullIfEmpty(Collapse(listing.QuerySelector("h3")?.TextContent ?? ""));
        var posterVenue = NullIfEmpty(Collapse(listing.QuerySelector(".poster-venue")?.TextContent ?? ""));

        string? city, venue, category, price = null;
        if (listing.QuerySelector(".event-card-location") is { } loc)
        {
            city = NullIfEmpty(Collapse(loc.TextContent));
            venue = posterVenue ?? NullIfEmpty(Collapse(listing.QuerySelector("h3 + p")?.TextContent ?? "")); // the paragraph is "venue · city"
            category = NullIfEmpty(Collapse(listing.QuerySelector(".category-chip")?.TextContent ?? ""));
            var spans = listing.QuerySelectorAll(".event-meta-line > span").Select(s => Collapse(s.TextContent)).ToList();
            if (spans.Count > 1 && spans[1].Length > 0 && !NotPublished().IsMatch(spans[1])) price = spans[1];
        }
        else
        {
            // Featured card: "01 / CITY / CATEGORY" above the title, the venue on the poster.
            var parts = (listing.QuerySelector("h3")?.PreviousElementSibling?.TextContent ?? "").Split('/').Select(Collapse).ToList();
            city = parts.Count > 1 ? NullIfEmpty(parts[1]) : null;
            category = parts.Count > 2 ? NullIfEmpty(string.Join(" / ", parts.Skip(2))) : null;
            venue = posterVenue;
        }

        var (startsAt, endsAt) = ParseWhen(listing, path);
        var image = listing.QuerySelector("img")?.GetAttribute("src");
        return new HtmlListing(href, new CandidateFields
        {
            Title = title,
            StartsAt = startsAt,
            EndsAt = endsAt,
            CityText = city is null ? null : CityCase(city),
            VenueName = venue,
            CategoryText = category,
            CategorySlug = MapCategory(category),
            PriceText = price,
            ImageUrl = image is not null && Uri.TryCreate(pageUrl, image, out var img) && img.Scheme is "http" or "https" ? img.AbsoluteUri : null,
        });
    }

    /// <summary>No clock time ("ORARI TBA", "PROGRAMI TBA") → no start; a day range sets the end to the last day 23:59.</summary>
    private static (DateTimeOffset? Start, DateTimeOffset? End) ParseWhen(IElement listing, string? path)
    {
        var time = listing.QuerySelector("time");
        var descriptor = "";
        if (time?.QuerySelector("span") is { } span)
        {
            var afterBreak = span.ChildNodes.SkipWhile(n => n.NodeName != "BR").Skip(1);
            descriptor = Collapse(string.Concat(afterBreak.Select(n => n.TextContent)));
        }

        DateOnly? first = null;
        if (time?.GetAttribute("datetime") is { } dt && DateOnly.TryParseExact(dt, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) first = d;
        else if (path is not null && SlugDate().Match(path) is { Success: true } sd
                 && DateOnly.TryParseExact(sd.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d2)) first = d2;
        if (first is null) return (null, null);

        DateTimeOffset? starts = null, ends = null;
        if (ClockRange().Match(descriptor) is { Success: true } c)
        {
            starts = Local(first.Value.ToDateTime(Clock(c.Groups["h"].Value, c.Groups["m"].Value)));
            if (c.Groups["eh"].Success)
            {
                var end = first.Value.ToDateTime(Clock(c.Groups["eh"].Value, c.Groups["em"].Value));
                if (end > starts.Value.DateTime) ends = Local(end);
            }
        }

        if (DayRange().Match(NormalizeText(descriptor)) is { Success: true } r
            && Array.IndexOf(Months, r.Groups["m2"].Value) + 1 is var endMonth and > 0)
        {
            var endDay = int.Parse(r.Groups["d2"].Value, CultureInfo.InvariantCulture);
            var year = endMonth < first.Value.Month ? first.Value.Year + 1 : first.Value.Year;
            if (endDay >= 1 && endDay <= DateTime.DaysInMonth(year, endMonth))
            {
                var last = new DateTime(year, endMonth, endDay, 23, 59, 0);
                if (last > first.Value.ToDateTime(TimeOnly.MinValue)) ends = Local(last);
            }
        }
        return (starts, ends);
    }

    private static TimeOnly Clock(string h, string m) => new(int.Parse(h, CultureInfo.InvariantCulture), int.Parse(m, CultureInfo.InvariantCulture));

    private static DateTimeOffset Local(DateTime local) => new(local, Pristina.GetUtcOffset(local));

    internal static string? MapCategory(string? category)
    {
        if (category is null) return null;
        var c = NormalizeText(category);
        return c switch
        {
            _ when Has(c, "muzik", "koncert", "live") => "muzike",
            _ when Has(c, "teat", "film", "kinem", "kino") => "teater",
            _ when Has(c, "art", "ekspozit") => "art",
            _ when Has(c, "sport") => "sport",
            _ when Has(c, "familj", "femij") => "familje",
            _ when Has(c, "ushqim", "pije", "food") => "ushqim",
            _ when Has(c, "party", "nate", "dj", "club") => "nate",
            _ when Has(c, "edukim", "konferenc", "workshop", "kurs") => "edukim",
            _ => null,
        };
    }

    private static bool Has(string text, params string[] words) => words.Any(w => Regex.IsMatch(text, @"\b" + w, RegexOptions.CultureInvariant)); // word prefixes: "party" is not "art"

    private static string? PathOf(string href) => EventPath().Match(href) is { Success: true } m ? m.Groups["path"].Value : null;

    private static string CityCase(string city) =>
        city.Equals(city.ToUpperInvariant(), StringComparison.Ordinal) ? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(city.ToLowerInvariant()) : city;

    private static string NormalizeText(string s) => s.ToLowerInvariant().Replace('ë', 'e').Replace('ç', 'c');

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;

    private static string Collapse(string s) => Whitespace().Replace(s, " ").Trim();

    [GeneratedRegex(@"^/event/(?<path>[^/?#]+)(?:\?.*)?$")]
    private static partial Regex EventPath();

    [GeneratedRegex(@"(?<!\d)\d{4}-\d{2}-\d{2}(?!\d)")]
    private static partial Regex SlugDate();

    [GeneratedRegex(@"(?<!\d)(?<h>\d{1,2}):(?<m>\d{2})(?:\s*[–-]\s*(?<eh>\d{1,2}):(?<em>\d{2}))?")]
    private static partial Regex ClockRange();

    [GeneratedRegex(@"\d{1,2}\s*[a-z]*\s*[–-]\s*(?<d2>\d{1,2})\s*(?<m2>[a-z]+)")]
    private static partial Regex DayRange();

    [GeneratedRegex(@"publikua|\bTBA\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NotPublished();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
