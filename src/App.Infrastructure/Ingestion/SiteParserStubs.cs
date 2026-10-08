using AngleSharp.Dom;
using App.Domain.Sources;

namespace App.Infrastructure.Ingestion;

// Placeholders until T044/T045 add the real parsers: they find no listing, so a run of these sources fails with MalformedPayload.

internal sealed class PrishtinaEventsParser : IHtmlSiteParser
{
    public string Key => HtmlAdapters.PrishtinaEvents;

    public IReadOnlyList<IElement> FindListings(IDocument page) => [];

    public HtmlListing Map(IElement listing, Uri pageUrl, DateTimeOffset now) => throw new NotSupportedException();
}

internal sealed class KapodalimParser : IHtmlSiteParser
{
    public string Key => HtmlAdapters.Kapodalim;

    public IReadOnlyList<IElement> FindListings(IDocument page) => [];

    public HtmlListing Map(IElement listing, Uri pageUrl, DateTimeOffset now) => throw new NotSupportedException();
}
