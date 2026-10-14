using System.Text.RegularExpressions;
using AngleSharp.Dom;
using App.Domain.Content;
using App.Domain.Reference;
using App.Domain.Sources;

namespace App.Infrastructure.Ingestion;

/// <summary>
/// https://www.foleja.com/foleja-Deals/ (design.md §6, ADR-045). A Shopware 6 landing page whose product sliders show the
/// discounted products server-side. Listings are the product boxes (Shopware's own <c>product-box</c> class, never a
/// generated one) that hold a <c>product-name</c> link to <c>/{SKU}/{slug}</c>; sliders repeat boxes, so listings are
/// de-duplicated by path. The current price is printed as a whole part and a separate decimal element ("€ 1,341" + "30"),
/// the old price and the badge ("-54%") sit in the list-price block; a box without it is a product with no discount. The
/// page has no pagination (<c>?p=N</c> shows the same page), so <see cref="IHtmlSiteParser.NextPage"/> stays null.
/// </summary>
internal sealed partial class FolejaDealsParser : IHtmlSiteParser
{
    public string Key => HtmlAdapters.Foleja;

    public ItemKind Kind => ItemKind.Deal;

    public IReadOnlyList<IElement> FindListings(IDocument page, CancellationToken ct)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<IElement>();
        foreach (var box in page.QuerySelectorAll(".product-box"))
        {
            ct.ThrowIfCancellationRequested();
            var path = PathOf(box.QuerySelector("a.product-name")?.GetAttribute("href"), null); // the host is checked in Map
            if (path is null || !seen.Add(path)) continue;
            result.Add(box);
        }
        return result;
    }

    public HtmlListing Map(IElement listing, Uri pageUrl, DateTimeOffset now)
    {
        var link = listing.QuerySelector("a.product-name");
        var path = PathOf(link?.GetAttribute("href"), pageUrl); // the SKU path is the identity: externalId = url
        var title = Collapse(link?.TextContent ?? "");

        var newPrice = CurrentPrice(listing.QuerySelector(".whole-original-price"));
        var oldPrice = ShopPrice.Parse(listing.QuerySelector(".list-price-price")?.TextContent);
        var stated = Percent().Match(listing.QuerySelector(".list-price-percentage")?.TextContent ?? "");
        var image = listing.QuerySelector("img.product-image")?.GetAttribute("src");

        return new HtmlListing(path, new CandidateFields
        {
            Title = title.Length == 0 ? null : title,
            OldPrice = oldPrice,
            NewPrice = newPrice,
            StatedDiscountPercent = stated.Success ? int.Parse(stated.Groups[1].Value) : null,
            Currency = "EUR",
            MerchantName = "Foleja",
            ImageUrl = image is not null && Uri.TryCreate(pageUrl, image, out var img) && (img.Scheme is "http" or "https") ? img.AbsoluteUri : null,
        });
    }

    /// <summary>"€ 1,341" followed by the decimal element "30" → 1341.30; the whole part's separators are thousands groups.</summary>
    private static decimal? CurrentPrice(IElement? price)
    {
        if (price is null) return null;
        var cents = price.QuerySelector(".decimal-rounded-price");
        // Only the bare text nodes are the whole part: the currency sign and the decimals are elements of their own.
        var whole = new string(price.ChildNodes.Where(n => n.NodeType == NodeType.Text).SelectMany(n => n.TextContent).Where(char.IsDigit).ToArray());
        if (whole.Length == 0) return null;
        var digits = (cents?.TextContent ?? "").Trim();
        return ShopPrice.Parse(digits.Length > 0 && digits.All(char.IsDigit) ? $"{whole},{digits}" : whole);
    }

    private static string? PathOf(string? href, Uri? pageUrl) => ShopLinks.ProductPath(href, pageUrl, ProductPath());

    private static string Collapse(string s) => Whitespace().Replace(s, " ").Trim();

    [GeneratedRegex(@"^/[A-Za-z0-9][A-Za-z0-9._-]*/[^/]+/?$")]
    private static partial Regex ProductPath();

    [GeneratedRegex(@"(\d{1,3})\s*%")]
    private static partial Regex Percent();

    [GeneratedRegex(@"[\s ]+")]
    private static partial Regex Whitespace();
}
