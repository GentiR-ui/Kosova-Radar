using AngleSharp.Dom;
using App.Domain.Content;
using App.Domain.Reference;

namespace App.Infrastructure.Ingestion;

/// <summary>What one listing shows, mapped deterministically. <see cref="Href"/> is the raw link, resolved by the adapter.</summary>
public sealed record HtmlListing(string? Href, CandidateFields Fields);

/// <summary>
/// The site-specific part of an HTML source (design.md §6, ADR-035). <see cref="HtmlSourceAdapter"/> fetches and cleans
/// the page and builds the candidate; the parser only locates the listings and maps what each one shows.
/// </summary>
public interface IHtmlSiteParser
{
    /// <summary>The <c>Source.Adapter</c> key (<see cref="App.Domain.Sources.HtmlAdapters"/>).</summary>
    string Key { get; }

    /// <summary>What the listings are: events (the default) or deals of an online shop (ADR-045).</summary>
    ItemKind Kind => ItemKind.Event;

    /// <summary>
    /// The next page of the site's own server-rendered pagination ("?p=2" links), or null when this is the last page or the
    /// site only offers "load more" buttons. Reads the page as fetched, navigation included. The adapter follows it only on
    /// the same site, after the robots.txt check, up to <c>Ingestion__HtmlMaxPages</c> pages.
    /// </summary>
    Uri? NextPage(IDocument page, Uri pageUrl, CancellationToken ct) => null;

    /// <summary>
    /// The listing elements of the cleaned page, in page order. Locate by stable signals, never by generated class names;
    /// stay linear in the page size and observe <paramref name="ct"/> (the adapter's deadline).
    /// </summary>
    IReadOnlyList<IElement> FindListings(IDocument page, CancellationToken ct);

    /// <summary>
    /// Maps one listing; anything it does not show stays null. <paramref name="now"/> is the fetch time: it may resolve a year,
    /// but must not reach the fields of a deal (<c>validFrom</c>), which would make every fetch a change (design.md §2 content hash).
    /// </summary>
    HtmlListing Map(IElement listing, Uri pageUrl, DateTimeOffset now);

    /// <summary>
    /// The AI text of <paramref name="element"/> and everything inside it, or null to read it as usual. A site whose cards
    /// show the fetch day's occurrence of a running series (poster day, weekday) leaves that out here, so the AI text and
    /// with it the content hash stay the same from day to day (ADR-042). Must not recurse into the subtree.
    /// </summary>
    string? OccurrenceFreeText(IElement element) => null;
}
