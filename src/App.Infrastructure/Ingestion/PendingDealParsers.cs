using AngleSharp.Dom;
using App.Domain.Content;
using App.Domain.Reference;
using App.Domain.Sources;

namespace App.Infrastructure.Ingestion;

/// <summary>
/// Placeholder of the Gjirafa Mall deal parser until its feasibility check and fixtures land (T055, ADR-045). It finds no
/// listings, so a run of such a source fails with <c>MalformedPayload</c> instead of publishing anything; the adapter key
/// already exists so the source can be created and seeded.
/// </summary>
internal abstract class PendingDealParser(string key) : IHtmlSiteParser
{
    public string Key { get; } = key;

    public ItemKind Kind => ItemKind.Deal;

    public IReadOnlyList<IElement> FindListings(IDocument page, CancellationToken ct) => [];

    public HtmlListing Map(IElement listing, Uri pageUrl, DateTimeOffset now) =>
        throw new NotSupportedException("The parser has no listings to map yet.");
}

internal sealed class GjirafaMallDealsParser() : PendingDealParser(HtmlAdapters.GjirafaMall);
