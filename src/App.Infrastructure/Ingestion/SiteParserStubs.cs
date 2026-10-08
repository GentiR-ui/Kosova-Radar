using AngleSharp.Dom;
using App.Domain.Sources;

namespace App.Infrastructure.Ingestion;

// Placeholder until T045 adds the real parser: it finds no listing, so a run of this source fails with MalformedPayload.

internal sealed class KapodalimParser : IHtmlSiteParser
{
    public string Key => HtmlAdapters.Kapodalim;

    public IReadOnlyList<IElement> FindListings(IDocument page) => [];

    public HtmlListing Map(IElement listing, Uri pageUrl, DateTimeOffset now) => throw new NotSupportedException();
}
