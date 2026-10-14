using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using App.Application.Abstractions;
using App.Application.Candidates;
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

    /// <summary>
    /// AngleSharp is quadratic in nesting depth; a hostile page must not hold the scheduler for minutes. Applies to parsing
    /// and, separately, to cleaning, finding and mapping the listings.
    /// </summary>
    public static readonly TimeSpan ParseTimeout = TimeSpan.FromSeconds(5);

    /// <summary>More scripts, ads and hidden elements than this is not a listing page (removal is quadratic in their number).</summary>
    public const int MaxRemovedElements = 20_000;

    /// <summary>
    /// AngleSharp reads text (<c>TextContent</c>) and clones recursively; a page nested thousands of levels deep would overflow
    /// the stack, which kills the process. Real listing pages stay far below 100 levels.
    /// </summary>
    public const int MaxDepth = 512;

    private const string RemovedElements =
        "script, style, noscript, template, nav, header, footer, form, iframe, ins.adsbygoogle, "
        + "[class*='advert' i], [id*='advert' i], [class*='adsbygoogle' i], [aria-label='advertisement' i], "
        // Text a visitor cannot see must not reach the AI or the evidence (hidden instructions to the model).
        + "[hidden], [style*='display:none' i], [style*='display: none' i], [style*='visibility:hidden' i], [style*='visibility: hidden' i]";

    protected override SourceType Type => SourceType.Html;

    /// <summary>
    /// The configured page, then the pages of the site's own pagination the parser points to (same site, robots.txt checked,
    /// at most <see cref="IngestionOptions.HtmlMaxPages"/> in total). A later page that cannot be fetched ends the listing:
    /// the pages already read are still a valid, if shorter, run.
    /// </summary>
    public override async Task<FetchResult> FetchAsync(Source source, CancellationToken ct)
    {
        var first = await base.FetchAsync(source, ct);
        var parser = parsers.FirstOrDefault(p => p.Key == source.Adapter);
        if (parser is null || options.HtmlMaxPages < 2 || !Uri.TryCreate(source.BaseUrl, UriKind.Absolute, out var start))
            return first;

        var more = new List<FetchedPage>();
        var visited = new HashSet<string>(StringComparer.Ordinal) { start.GetLeftPart(UriPartial.Query) };
        var html = first.Content;
        var current = start;
        while (more.Count + 1 < options.HtmlMaxPages)
        {
            var next = await NextPageAsync(parser, html, current, ct);
            // A relative or credentialed link, another site, or a page already read ends the pagination (fail closed).
            if (next is null || !next.IsAbsoluteUri || !string.IsNullOrEmpty(next.UserInfo) || !IsSameSite(next, start)) break;
            next = new Uri(next.GetLeftPart(UriPartial.Query)); // a fragment never names another page
            if (!visited.Add(next.AbsoluteUri)) break;
            try
            {
                await Robots.EnsureAllowedAsync(next, ct);
                html = await Fetcher.GetStringAsync(next, ct);
            }
            catch (SourceFetchException)
            {
                break;
            }

            more.Add(new FetchedPage(next, html));
            current = next;
        }

        return first with { MorePages = more };
    }

    private static bool IsSameSite(Uri candidate, Uri site) =>
        candidate.Scheme == site.Scheme && candidate.Port == site.Port
        && string.Equals(candidate.IdnHost, site.IdnHost, StringComparison.OrdinalIgnoreCase);

    /// <summary>The parser's next page, read from the page as fetched; null when there is none or the page cannot be read.</summary>
    private static async Task<Uri?> NextPageAsync(IHtmlSiteParser parser, string html, Uri pageUrl, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(ParseTimeout);
        try
        {
            using var page = await new HtmlParser().ParseDocumentAsync(html, deadline.Token);
            return IsTooDeep(page, deadline.Token) ? null : parser.NextPage(page, pageUrl, deadline.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return null; // the layout is not what the parser expects: the pages read so far are the listing
        }
    }

    public override async Task<FeedParseResult> ParseAsync(Source source, FetchResult fetched, CancellationToken ct)
    {
        var parser = parsers.FirstOrDefault(p => p.Key == source.Adapter);
        if (parser is null || !Uri.TryCreate(source.BaseUrl, UriKind.Absolute, out var pageUrl))
            return FeedParseResult.Malformed;

        var now = clock.UtcNow;
        var result = await ParseAsync(source.Id, parser, pageUrl, fetched.Content, now, options.EventHorizonDays, ParseTimeout, ct);
        if (result.ErrorCode is not null || fetched.MorePages is not { Count: > 0 } more) return result;

        var items = result.Items.ToList();
        var failures = result.Failures.ToList();
        var received = result.Received;
        var seen = items.Select(i => i.Fingerprint).ToHashSet(StringComparer.Ordinal);
        foreach (var next in more)
        {
            // Each page resolves its own relative links against its own address.
            var page = await ParseAsync(source.Id, parser, next.Url, next.Content, now, options.EventHorizonDays, ParseTimeout, ct);
            if (page.ErrorCode is not null) break; // an empty or odd later page ends the listing, it does not fail the run

            var fresh = page.Items.Where(i => seen.Add(i.Fingerprint)).ToList();
            failures.AddRange(page.Failures.Select(f => f with { Index = received + f.Index }));
            received += page.Received - (page.Items.Count - fresh.Count); // a product repeated on a later page is one listing

            // The listing cap is per run, not per page: pagination must not multiply it.
            var room = MaxListings - items.Count;
            items.AddRange(fresh.Take(Math.Max(room, 0)));
            if (fresh.Count > room)
            {
                failures.Add(new ItemFailure(received, "TooManyItems"));
                break;
            }
        }

        return new FeedParseResult(received, items, failures);
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

        using var document = page;
        // The same deadline for everything after parsing: a crafted page must fail one run, not hold the scheduler (ADR-039).
        using var work = CancellationTokenSource.CreateLinkedTokenSource(ct);
        work.CancelAfter(parseTimeout);
        try
        {
            if (IsTooDeep(page, work.Token)) return FeedParseResult.Malformed;

            // Removing many siblings is quadratic in AngleSharp: a real page has a few hundred such elements, not hundreds of thousands.
            var removed = page.QuerySelectorAll(RemovedElements);
            if (removed.Length > MaxRemovedElements) return FeedParseResult.Malformed;
            foreach (var element in removed)
            {
                work.Token.ThrowIfCancellationRequested();
                element.Remove();
            }

            IReadOnlyList<IElement> listings;
            try
            {
                listings = parser.FindListings(page, work.Token);
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
                work.Token.ThrowIfCancellationRequested();
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

                if (mapped.Fields.EventStart() is { } start && ContentRules.IsBeyondHorizon(start, now, horizonDays))
                {
                    received++; // beyond the horizon: no candidate, counted only in `received` (REQ-026)
                    continue;
                }

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
                // The AI only fills what the listing did not show (a shown but unmapped category included: the AI may suggest one),
                // and costs nothing when everything is known (ADR-035). Shop deals never go to the AI: a page lists hundreds of
                // products every few hours, and a missing price is a review issue, not a guess (ADR-045).
                var aiText = parser.Kind == ItemKind.Event && (fields.EventStart() is null || fields.CityText is null || fields.CategorySlug is null)
                    ? CleanText(listing, parser.OccurrenceFreeText)
                    : null;
                // The hash leaves out the evidence: card text also shows display-only parts (the poster day of a running
                // series) that would make every fetch a change; the fields and the AI text are what the candidate is built from.
                items.Add(new NormalizedCandidate(parser.Kind, url, url, fingerprint,
                    ItemIdentity.ContentHash(parser.Kind, fields, aiText, url), fields, aiText, evidence));
                received++;
            }

            return new FeedParseResult(received, items, failures);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return FeedParseResult.Malformed;
        }
    }

    // PostgreSQL cannot store NUL or lone surrogates; one listing with such text must not abort the run.
    // Short fields keep the limits of a reviewer's edit, so a huge heading cannot reach every queue response.
    private static bool AreStorable(CandidateFields f) =>
        new[] { f.Title, f.Description, f.CityText, f.VenueName, f.CategoryText, f.PriceText, f.MerchantName }
            .All(s => s is null || RadarFeedParser.IsStorableText(s))
        && new[] { f.Title, f.CityText, f.VenueName, f.CategoryText, f.PriceText, f.MerchantName }.All(s => s is null || s.Length <= CandidateRequestValidator.MaxText)
        && (f.Description is null || f.Description.Length <= CandidateRequestValidator.MaxDescription)
        && (f.ImageUrl is null || (f.ImageUrl.Length <= RadarFeedParser.MaxUrlLength && IsHttpUrl(f.ImageUrl)));

    private static bool IsHttpUrl(string s) => Uri.TryCreate(s, UriKind.Absolute, out var u) && u.Scheme is "http" or "https";

    /// <summary>
    /// The visible text of a listing: one space between text nodes, whitespace collapsed, capped. Stops at the cap, so a
    /// listing with megabytes of text costs no more than one with 16,000 characters.
    /// </summary>
    internal static string CleanText(IElement listing, Func<IElement, string?>? replace = null)
    {
        var sb = new StringBuilder();
        foreach (var text in replace is null ? listing.Descendants<IText>().Select(t => t.Data) : Texts(listing, replace))
        {
            foreach (var c in text)
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

    /// <summary>Whether any node is nested deeper than <see cref="MaxDepth"/>; iterative, linear in the page size.</summary>
    private static bool IsTooDeep(IDocument page, CancellationToken ct)
    {
        var stack = new Stack<(INode Node, int Depth)>();
        stack.Push((page, 0));
        var visited = 0;
        while (stack.Count > 0)
        {
            var (node, depth) = stack.Pop();
            if (depth > MaxDepth) return true;
            if (++visited % 4096 == 0) ct.ThrowIfCancellationRequested();
            for (var child = node.FirstChild; child is not null; child = child.NextSibling) stack.Push((child, depth + 1));
        }

        return false;
    }

    /// <summary>
    /// The text nodes of a listing in document order, where <paramref name="replace"/> may stand in for an element and its
    /// subtree. Iterative: a crafted page nests thousands of levels deep, and recursion would overflow the stack.
    /// </summary>
    private static IEnumerable<string> Texts(IElement listing, Func<IElement, string?> replace)
    {
        var stack = new Stack<INode>();
        stack.Push(listing);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is IText t)
            {
                yield return t.Data;
                continue;
            }

            if (node is IElement e && !ReferenceEquals(e, listing) && replace(e) is { } replaced)
            {
                yield return replaced;
                continue;
            }

            for (var child = node.LastChild; child is not null; child = child.PreviousSibling) stack.Push(child);
        }
    }
}
