using AngleSharp.Dom;
using App.Domain.Content;
using App.Domain.Reference;
using App.Domain.Sources;

namespace App.Infrastructure.Ingestion;

/// <summary>
/// Placeholders of the two shop deal parsers until their feasibility checks and fixtures land (T054 Foleja, T055 Gjirafa
/// Mall, ADR-045). They find no listings, so a run of such a source fails with <c>MalformedPayload</c> instead of
/// publishing anything; the adapter keys already exist so the sources can be created and seeded.
/// </summary>
internal abstract class PendingDealParser(string key) : IHtmlSiteParser
{
    public string Key { get; } = key;

    public ItemKind Kind => ItemKind.Deal;

    public IReadOnlyList<IElement> FindListings(IDocument page, CancellationToken ct) => [];

    public HtmlListing Map(IElement listing, Uri pageUrl, DateTimeOffset now) =>
        throw new NotSupportedException("The parser has no listings to map yet.");
}

internal sealed class FolejaDealsParser() : PendingDealParser(HtmlAdapters.Foleja);

internal sealed class GjirafaMallDealsParser() : PendingDealParser(HtmlAdapters.GjirafaMall);
