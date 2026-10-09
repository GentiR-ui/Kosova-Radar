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

    public IReadOnlyList<IElement> FindListings(IDocument page, CancellationToken ct)
    {
        var indexByPath = new Dictionary<string, int>(StringComparer.Ordinal); // linear: a page of repeated links must not rescan cards
        var result = new List<IElement>();
        foreach (var a in page.QuerySelectorAll("a[href]"))
        {
            ct.ThrowIfCancellationRequested();
            var path = PathOf(a.GetAttribute("href")!);
            if (path is null) continue;
            var card = a.Closest("article") ?? (a.QuerySelector("h3") is not null ? a : null);
            if (card is null) continue;
            if (indexByPath.TryGetValue(path, out var at))
            {
                // The grid card (article) is richer than a featured card: let it replace one found earlier.
                if (card.LocalName == "article" && result[at].LocalName != "article") result[at] = card;
                continue;
            }
            indexByPath[path] = result.Count;
            result.Add(card);
        }
        return result;
    }

    public HtmlListing Map(IElement listing, Uri pageUrl, DateTimeOffset now)
    {
        var link = listing.LocalName == "a" ? listing : listing.QuerySelectorAll("a[href]").FirstOrDefault(a => PathOf(a.GetAttribute("href")!) is not null);
        var path = link is null ? null : PathOf(link.GetAttribute("href")!);
        // The canonical event page is the identity: "?date=" names today's occurrence and changes every day.
        var href = path is null ? null : $"/event/{path}";

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

    /// <summary>
    /// A running series shows today's occurrence: the poster day and month, the date block and the weekday before the
    /// first "·" of the date line ("E premte · 9–10 TETOR · ORARI TBA", featured "Fri, Oct 9 · …"). The descriptor stays.
    /// </summary>
    public string? OccurrenceFreeText(IElement element)
    {
        if (element.LocalName == "time") return Descriptor(element);
        if (element.ClassList.Contains("event-date-block")) return "";
        if ((element.LocalName == "span" && element.ParentElement?.ClassList.Contains("event-meta-line") == true && element.PreviousElementSibling is null)
            || (element.LocalName == "b" && element.ParentElement?.LocalName == "p"))
            return OccurrenceDay().Replace(Collapse(element.TextContent), "");
        // Featured cards: "02 / MITROVICË / VRAPIM" above the title; the position shifts when an earlier pick ends.
        if (element.NextElementSibling?.LocalName == "h3" && PickPosition().Match(element.TextContent) is { Success: true } pick)
            return element.TextContent[(pick.Index + pick.Length)..];
        return null;
    }

    /// <summary>The text after the poster's line break: the time or the range ("9–10 TETOR · ORARI TBA").</summary>
    private static string Descriptor(IElement time) =>
        time.QuerySelector("span") is { } span
            ? Collapse(string.Concat(span.ChildNodes.SkipWhile(n => n.NodeName != "BR").Skip(1).Select(n => n.TextContent)))
            : "";

    /// <summary>
    /// No clock time ("ORARI TBA", "PROGRAMI TBA") → no start. A day range ("9–10 TETOR" after the poster date, else the
    /// slug's "-7-16-tetor-2026") sets the start date to its first day and the end to its last day 23:59, so a running
    /// series keeps the same fields while the poster shows each day's occurrence.
    /// </summary>
    private static (DateTimeOffset? Start, DateTimeOffset? End) ParseWhen(IElement listing, string? path)
    {
        var time = listing.QuerySelector("time");
        var descriptor = time is null ? "" : Descriptor(time);

        DateOnly? first = null;
        if (time?.GetAttribute("datetime") is { } dt && DateOnly.TryParseExact(dt, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) first = d;
        else if (path is not null && SlugDate().Match(path) is { Success: true } sd
                 && DateOnly.TryParseExact(sd.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d2)) first = d2;

        var range = (first is null ? null : ReadRange(DayRange().Match(NormalizeText(descriptor)), first.Value))
            ?? (path is null ? null : ReadRange(SlugRange().Match(path), null));
        if (range is { } r) first = r.First;
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

        if (range is { } days) ends = Local(days.Last.ToDateTime(new TimeOnly(23, 59)));
        return (starts, ends);
    }

    /// <summary>
    /// "D1[ month1]–D2 month2" with the year of the poster date (which lies in the range), or the slug's
    /// "-D1[-month1]-D2-month2-YYYY"; a first month after the last month is in the year before the last month's.
    /// </summary>
    private static (DateOnly First, DateOnly Last)? ReadRange(Match m, DateOnly? poster)
    {
        if (!m.Success) return null;
        var lastMonth = Array.IndexOf(Months, m.Groups["m2"].Value) + 1;
        var firstMonth = m.Groups["m1"].Length > 0 ? Array.IndexOf(Months, m.Groups["m1"].Value) + 1 : lastMonth;
        if (lastMonth == 0 || firstMonth == 0) return null;

        var d1 = int.Parse(m.Groups["d1"].Value, CultureInfo.InvariantCulture);
        var d2 = int.Parse(m.Groups["d2"].Value, CultureInfo.InvariantCulture);
        int firstYear, lastYear;
        if (poster is { Year: var y } p)
        {
            // Across New Year the poster day lies in the range: in January it is the last year, not the first.
            (firstYear, lastYear) = lastMonth >= firstMonth ? (y, y) : p.Month <= lastMonth ? (y - 1, y) : (y, y + 1);
        }
        else
        {
            lastYear = int.Parse(m.Groups["y"].Value, CultureInfo.InvariantCulture);
            firstYear = firstMonth > lastMonth ? lastYear - 1 : lastYear;
        }

        if (d1 < 1 || d1 > DateTime.DaysInMonth(firstYear, firstMonth) || d2 < 1 || d2 > DateTime.DaysInMonth(lastYear, lastMonth)) return null;
        var (first, last) = (new DateOnly(firstYear, firstMonth, d1), new DateOnly(lastYear, lastMonth, d2));
        return last > first ? (first, last) : null;
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
            _ when Has(c, "edukim", "konferenc", "workshop", "kurs", "teknolog", "biznes") => "edukim",
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

    [GeneratedRegex(@"(?<!\d)(?<d1>\d{1,2})\s*(?<m1>[a-z]*)\s*[–-]\s*(?<d2>\d{1,2})\s*(?<m2>[a-z]+)")]
    private static partial Regex DayRange();

    [GeneratedRegex(@"-(?<d1>\d{1,2})(?:-(?<m1>[a-z]+))?-(?<d2>\d{1,2})-(?<m2>[a-z]+)-(?<y>\d{4})$")]
    private static partial Regex SlugRange();

    [GeneratedRegex(@"publikua|\bTBA\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NotPublished();

    // "E premte · " (grid) or "Fri, Oct 9 · " (featured) at the start of the date line.
    [GeneratedRegex(@"^\s*(?:E\s+\p{L}+|\p{L}{3},\s*\p{L}{3,9}\.?\s+\d{1,2})\s*·\s*", RegexOptions.CultureInvariant)]
    private static partial Regex OccurrenceDay();

    [GeneratedRegex(@"^\s*\d+\s*/\s*")]
    private static partial Regex PickPosition();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
