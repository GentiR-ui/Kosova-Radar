using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using App.Application.Abstractions;
using App.Application.Ingestion;
using App.Application.Sources;
using App.Domain.Content;
using App.Domain.Reference;
using App.Domain.Sources;

namespace App.Infrastructure.Ingestion;

/// <summary>
/// HTML pages (design.md §6, ADR-035): fetches the configured page over the guarded client, removes what is not
/// content, and turns every listing the site parser finds into one raw item whose evidence is that listing only (REQ-022).
/// </summary>
internal sealed class HtmlSourceAdapter(
    SourceHttpFetcher fetcher, RobotsChecker robots, IEnumerable<IHtmlSiteParser> parsers, IClock clock, IngestionOptions options)
    : HttpSourceAdapter(fetcher, robots)
{
    public const int MaxEvidenceChars = 16_000;

    /// <summary>Like <see cref="RssFeedParser.MaxItems"/>: one page cannot flood the review queue or the AI budget.</summary>
    public const int MaxListings = 1000;

    /// <summary>AngleSharp is quadratic in nesting depth; a hostile page must not hold the scheduler for minutes.</summary>
    public static readonly TimeSpan ParseTimeout = TimeSpan.FromSeconds(5);

    private const string RemovedElements =
        "script, style, noscript, template, nav, header, footer, form, iframe, ins.adsbygoogle, "
        + "[class*='advert' i], [id*='advert' i], [class*='adsbygoogle' i], [aria-label='advertisement' i]";

    protected override SourceType Type => SourceType.Html;

    public override Task<FeedParseResult> ParseAsync(Source source, FetchResult fetched, CancellationToken ct)
    {
        var parser = parsers.FirstOrDefault(p => p.Key == source.Adapter);
        if (parser is null || !Uri.TryCreate(source.BaseUrl, UriKind.Absolute, out var pageUrl))
            return Task.FromResult(FeedParseResult.Malformed);
        return ParseAsync(source.Id, parser, pageUrl, fetched.Content, clock.UtcNow, options.HtmlHorizonDays, ParseTimeout, ct);
    }

    internal static async Task<FeedParseResult> ParseAsync(
        Guid sourceId, IHtmlSiteParser parser, Uri pageUrl, string html, DateTimeOffset now, int horizonDays,
        TimeSpan parseTimeout, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(html)) return FeedParseResult.Malformed;

        IDocument page;
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            deadline.CancelAfter(parseTimeout);
            try
            {
                page = await new HtmlParser().ParseDocumentAsync(html, deadline.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return FeedParseResult.Malformed; // a page that takes this long to parse is not a normal listing page
            }
        }

        foreach (var element in page.QuerySelectorAll(RemovedElements).ToList()) element.Remove();

        IReadOnlyList<IElement> listings;
        try
        {
            listings = parser.FindListings(page);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return FeedParseResult.Malformed; // the layout is not what the parser expects
        }

        if (listings.Count == 0) return FeedParseResult.Malformed; // a 2xx page without listings: the layout probably changed

        var items = new List<NormalizedCandidate>();
        var failures = new List<ItemFailure>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var received = 0;
        foreach (var listing in listings)
        {
            if (received >= MaxListings)
            {
                failures.Add(new ItemFailure(received, "TooManyItems"));
                received++;
                break;
            }

            HtmlListing mapped;
            try
            {
                mapped = parser.Map(listing, pageUrl, now);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // A parser reads uncontrolled markup: whatever it trips over is this listing's failure, not the run's.
                failures.Add(new ItemFailure(received++, "InvalidListing"));
                continue;
            }

            if (mapped.Fields.StartsAt is { } start && start > now.AddDays(horizonDays)) continue; // beyond the horizon: skipped, not counted
            if (mapped.Href is null)
            {
                failures.Add(new ItemFailure(received++, "MissingIdentity"));
                continue;
            }

            if (!Uri.TryCreate(pageUrl, mapped.Href, out var link) || link.Scheme is not ("http" or "https")
                || !string.IsNullOrEmpty(link.UserInfo) || link.AbsoluteUri.Length > RadarFeedParser.MaxUrlLength)
            {
                failures.Add(new ItemFailure(received++, "InvalidField:url"));
                continue;
            }

            var url = link.GetLeftPart(UriPartial.Query); // a fragment never identifies another page
            if (url.Length > RadarFeedParser.MaxExternalIdLength) // the url is the externalId, whose column is shorter than a url
            {
                failures.Add(new ItemFailure(received++, "InvalidField:externalId"));
                continue;
            }

            if (!seen.Add(url)) continue; // the same event shown twice on the page is one listing

            var text = CleanText(listing);
            var fields = mapped.Fields with { Url = url };
            if (!RadarFeedParser.IsStorableText(text) || !AreStorable(fields))
            {
                failures.Add(new ItemFailure(received++, "InvalidField:text"));
                continue;
            }

            var evidence = $"{text}\nURL: {url}";
            var fingerprint = ItemIdentity.Fingerprint(sourceId, url, url)!;
            // The AI only fills what the listing did not show, and costs nothing when everything is known (ADR-035).
            var aiText = fields.StartsAt is null || fields.CityText is null || (fields.CategoryText is null && fields.CategorySlug is null)
                ? text
                : null;
            items.Add(new NormalizedCandidate(ItemKind.Event, url, url, fingerprint,
                ItemIdentity.ContentHash(ItemKind.Event, fields, aiText, evidence), fields, aiText, evidence));
            received++;
        }

        return new FeedParseResult(received, items, failures);
    }

    // PostgreSQL cannot store NUL or lone surrogates; one listing with such text must not abort the run.
    private static bool AreStorable(CandidateFields f) =>
        new[] { f.Title, f.Description, f.CityText, f.VenueName, f.CategoryText, f.PriceText }
            .All(s => s is null || RadarFeedParser.IsStorableText(s))
        && (f.ImageUrl is null || (f.ImageUrl.Length <= RadarFeedParser.MaxUrlLength && IsHttpUrl(f.ImageUrl)));

    private static bool IsHttpUrl(string s) => Uri.TryCreate(s, UriKind.Absolute, out var u) && u.Scheme is "http" or "https";

    /// <summary>
    /// The visible text of a listing: one space between text nodes, whitespace collapsed, capped. Stops at the cap, so a
    /// listing with megabytes of text costs no more than one with 16,000 characters.
    /// </summary>
    internal static string CleanText(IElement listing)
    {
        var sb = new StringBuilder();
        foreach (var node in listing.Descendants<IText>())
        {
            foreach (var c in node.Data)
            {
                if (char.IsWhiteSpace(c))
                {
                    if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
                }
                else
                {
                    sb.Append(c);
                }
            }

            if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
            if (sb.Length > MaxEvidenceChars) break;
        }

        var length = Math.Min(sb.Length, MaxEvidenceChars);
        if (length > 0 && char.IsHighSurrogate(sb[length - 1])) length--;
        return sb.ToString(0, length).TrimEnd();
    }
}
