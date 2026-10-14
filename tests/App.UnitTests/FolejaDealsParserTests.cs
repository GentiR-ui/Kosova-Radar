using App.Application.Ingestion;
using App.Domain.Content;
using App.Domain.Reference;
using App.Infrastructure.Ingestion;

namespace App.UnitTests;

/// <summary>Contract tests for the Foleja deals parser on the page saved on 2026-10-14 (design.md §6) and hand-made boxes.</summary>
public class FolejaDealsParserTests
{
    private static readonly Guid Source = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Uri Page = new("https://www.foleja.com/foleja-Deals/");
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

    private static FeedParseResult Parse(string html, DateTimeOffset? now = null) =>
        HtmlSourceAdapter.ParseAsync(Source, new FolejaDealsParser(), Page, html, now ?? Now, 3, TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();

    private static readonly string SavedHtml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Html", "foleja-deals-2026-10-14.html"));

    private static readonly Lazy<FeedParseResult> Saved = new(() => Parse(SavedHtml));

    private static CandidateFields Item(string sku) =>
        Saved.Value.Items.Single(i => i.Fields.Url!.StartsWith($"https://www.foleja.com/{sku}/", StringComparison.Ordinal)).Fields;

    private static string Box(string sku, string name, string whole, string cents, string? oldPrice, string? badge) =>
        $"""
        <div class="card product-box"><div class="product-info"><span class="product-price">
          <span class="whole-original-price"><span class="currency-symbol">€</span>{whole}<span class="decimal-rounded-price">{cents}</span></span>
          {(oldPrice is null ? "" : $"<span class=\"list-price-price\">{oldPrice}</span><span class=\"list-price-percentage default_btn\">{badge}</span>")}
        </span><a href="https://www.foleja.com/{sku}/slug" class="product-name">{name}</a></div></div>
        """;

    [Fact]
    public void The_saved_page_yields_each_product_once()
    {
        // 207 boxes in the sliders, 122 distinct products; one has no discount.
        Assert.Null(Saved.Value.ErrorCode);
        Assert.Equal(122, Saved.Value.Items.Count);
        Assert.Empty(Saved.Value.Failures);
        Assert.All(Saved.Value.Items, i => Assert.Equal(ItemKind.Deal, i.Kind));
    }

    [Fact]
    public void A_product_with_cents_has_exact_prices_badge_image_and_merchant()
    {
        var f = Item("YLL-200016988");

        Assert.Equal("Bazë metalike e zezë matte '40x72cm FH437", f.Title);
        Assert.Equal(27.10m, f.NewPrice);
        Assert.Equal(59.00m, f.OldPrice);
        Assert.Equal(54, f.StatedDiscountPercent);
        Assert.Equal("EUR", f.Currency);
        Assert.Equal("Foleja", f.MerchantName);
        Assert.Equal("https://www.foleja.com/YLL-200016988/Baze-metalike-e-zeze-matte-40x72cm-FH437", f.Url);
        Assert.StartsWith("https://images.foleja.com/media/", f.ImageUrl);
        Assert.Null(f.ValidUntil); // a listing deal has no end date (ADR-045)
    }

    [Fact]
    public void A_sku_with_a_variant_suffix_is_kept()
    {
        var f = Item("YLL-200005503.1");

        Assert.StartsWith("Atlete për femra Furla", f.Title);
        Assert.NotNull(f.NewPrice);
    }

    [Fact]
    public void Round_prices_and_a_space_grouped_old_price_are_read()
    {
        var f = Item("YLL-200009557");

        Assert.Equal("Divan treshe Atelier del Sofa, Kapri, hiri", f.Title);
        Assert.Equal(683.00m, f.NewPrice);
        Assert.Equal(1248.00m, f.OldPrice);
        Assert.Equal(45, f.StatedDiscountPercent);
    }

    [Fact]
    public void Every_product_on_the_saved_page_is_complete_and_the_badge_matches_the_prices()
    {
        var items = Saved.Value.Items.Select(i => i.Fields).ToList();

        Assert.All(items, f =>
        {
            Assert.False(string.IsNullOrWhiteSpace(f.Title));
            Assert.True(f.NewPrice > 0);
            Assert.NotNull(f.ImageUrl);
        });
        Assert.Single(items, f => f.OldPrice is null); // the one product without a discount
        Assert.All(items.Where(f => f.OldPrice is not null), f =>
        {
            var computed = (int)Math.Round((f.OldPrice!.Value - f.NewPrice!.Value) / f.OldPrice.Value * 100);
            Assert.InRange(computed, f.StatedDiscountPercent!.Value - 1, f.StatedDiscountPercent.Value + 1);
        });
        Assert.Equal(58, items.Count(f => f.StatedDiscountPercent >= 35));
    }

    [Fact]
    public void More_named_products_are_exact()
    {
        var a = Item("YLL-200014944");
        Assert.Equal((52.10m, 139.00m, 63), (a.NewPrice, a.OldPrice, a.StatedDiscountPercent));
        var b = Item("YLL-200016114");
        Assert.Equal((314.00m, 619.00m, 49), (b.NewPrice, b.OldPrice, b.StatedDiscountPercent));
    }

    [Fact]
    public void A_list_price_without_a_badge_and_foreign_or_data_links_are_handled()
    {
        var noBadge = Box("YLL-4", "B", "5", "00", "10,00 €", "").Replace("list-price-percentage default_btn\"></span>", "list-price-percentage\"></span>");
        var foreign = Box("YLL-5", "C", "5", "00", null, null).Replace("https://www.foleja.com/YLL-5", "https://other.example/YLL-5");
        var data = Box("YLL-6", "D", "5", "00", null, null).Replace("<div class=\"product-info\">", "<img class=\"product-image\" src=\"data:image/png;base64,AAAA\"><div class=\"product-info\">");

        var result = Parse($"<html><body>{noBadge}{foreign}{data}</body></html>");

        Assert.Equal(2, result.Items.Count);
        Assert.Null(result.Items[0].Fields.StatedDiscountPercent);
        Assert.Equal(10.00m, result.Items[0].Fields.OldPrice);
        Assert.Null(result.Items[1].Fields.ImageUrl);
        Assert.Null(((IHtmlSiteParser)new FolejaDealsParser()).NextPage(null!, Page, CancellationToken.None));
    }

    [Fact]
    public void A_thousands_group_in_the_current_price_is_not_a_decimal()
    {
        var f = Parse($"<html><body>{Box("YLL-1", "Laptop", "1,341", "30", "1 727,00 €", "-22%")}</body></html>").Items.Single().Fields;

        Assert.Equal(1341.30m, f.NewPrice);
        Assert.Equal(1727.00m, f.OldPrice);
        Assert.Equal(22, f.StatedDiscountPercent);
    }

    [Fact]
    public void A_box_without_a_list_price_has_no_old_price_or_badge()
    {
        var f = Parse($"<html><body>{Box("YLL-2", "Kabllo", "9", "10", null, null)}</body></html>").Items.Single().Fields;

        Assert.Equal(9.10m, f.NewPrice);
        Assert.Null(f.OldPrice);
        Assert.Null(f.StatedDiscountPercent);
    }

    [Fact]
    public void A_repeated_slider_box_is_one_listing_and_boxes_without_a_product_link_are_ignored()
    {
        var html = $"<html><body>{Box("YLL-3", "A", "5", "00", "10,00 €", "-50%")}{Box("YLL-3", "A", "5", "00", "10,00 €", "-50%")}<div class='product-box'><a href='/cart'>x</a></div></body></html>";

        Assert.Single(Parse(html).Items);
    }

    [Fact]
    public void The_same_page_on_another_day_changes_no_hash()
    {
        var later = Parse(SavedHtml, Now.AddDays(1));

        Assert.Equal(Saved.Value.Items.Select(i => i.ContentHash), later.Items.Select(i => i.ContentHash));
    }

    [Fact]
    public void A_page_without_products_is_malformed()
    {
        Assert.Equal(FeedParseResult.Malformed.ErrorCode, Parse("<html><body><p>Mirë se vini</p></body></html>").ErrorCode);
    }
}
