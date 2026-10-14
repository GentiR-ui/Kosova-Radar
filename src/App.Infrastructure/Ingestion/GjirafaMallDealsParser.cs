using System.Text.RegularExpressions;
using AngleSharp.Dom;
using App.Domain.Content;
using App.Domain.Reference;
using App.Domain.Sources;

namespace App.Infrastructure.Ingestion;

/// <summary>
/// https://gjirafamall.com/oferta-speciale (design.md §6, ADR-045). A server-rendered nopCommerce category page; the home
/// page's "Oferta speciale" slider is filled by JavaScript and is not used. Listings are the product boxes (<c>product-item</c>
/// with a <c>data-productid</c>) that carry an old price: the page also lists products at their regular price, which are not
/// deals and are skipped. The identity is the product link's path (<c>/{slug}</c>); the badge ("-38%") sits in the prices
/// block. "Shfaq më shumë" needs JavaScript and <c>?pagenumber=2</c> shows no products here, so
/// <see cref="IHtmlSiteParser.NextPage"/> stays null.
/// </summary>
internal sealed partial class GjirafaMallDealsParser : IHtmlSiteParser
{
    public string Key => HtmlAdapters.GjirafaMall;

    public ItemKind Kind => ItemKind.Deal;

    public IReadOnlyList<IElement> FindListings(IDocument page, CancellationToken ct)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<IElement>();
        foreach (var box in page.QuerySelectorAll(".product-item[data-productid]"))
        {
            ct.ThrowIfCancellationRequested();
            if (box.QuerySelector(".prices .old-price") is null) continue; // not on sale
            var path = PathOf(box.QuerySelector("h3.product-title a")?.GetAttribute("href"), null); // the host is checked in Map
            if (path is null || !seen.Add(path)) continue;
            result.Add(box);
        }
        return result;
    }

    public HtmlListing Map(IElement listing, Uri pageUrl, DateTimeOffset now)
    {
        var link = listing.QuerySelector("h3.product-title a");
        var path = PathOf(link?.GetAttribute("href"), pageUrl); // the product path is the identity: externalId = url
        var title = Collapse(link?.TextContent ?? "");

        var prices = listing.QuerySelector(".prices");
        var newPrice = ShopPrice.Parse(prices?.QuerySelector(".price:not(.old-price)")?.TextContent);
        var oldPrice = ShopPrice.Parse(prices?.QuerySelector(".old-price")?.TextContent);
        // The badge is the prices block's own div that shows only "-38%" (the first div wraps the two prices).
        var badge = prices?.Children.Where(c => c.LocalName == "div").Select(c => Badge().Match(c.TextContent)).FirstOrDefault(m => m.Success);
        var image = listing.QuerySelector(".picture img")?.GetAttribute("src");

        return new HtmlListing(path, new CandidateFields
        {
            Title = title.Length == 0 ? null : title,
            OldPrice = oldPrice,
            NewPrice = newPrice,
            StatedDiscountPercent = badge is not null ? int.Parse(badge.Groups[1].Value) : null,
            Currency = "EUR",
            MerchantName = "Gjirafa Mall",
            ImageUrl = image is not null && Uri.TryCreate(pageUrl, image, out var img) && (img.Scheme is "http" or "https") ? img.AbsoluteUri : null,
        });
    }

    private static string? PathOf(string? href, Uri? pageUrl) => ShopLinks.ProductPath(href, pageUrl, ProductPath());

    private static string Collapse(string s) => Whitespace().Replace(s, " ").Trim();

    [GeneratedRegex(@"^/[A-Za-z0-9][A-Za-z0-9._-]*$")]
    private static partial Regex ProductPath();

    [GeneratedRegex(@"^\s*-?\s*(\d{1,3})\s*%\s*$")]
    private static partial Regex Badge();

    [GeneratedRegex(@"[\s ]+")]
    private static partial Regex Whitespace();
}
