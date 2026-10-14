using App.Application.Ingestion;
using App.Domain.Content;
using App.Domain.Reference;
using App.Infrastructure.Ingestion;

namespace App.UnitTests;

/// <summary>Contract tests for the Gjirafa Mall deals parser on the page saved on 2026-10-14 (design.md §6) and hand-made boxes.</summary>
public class GjirafaMallDealsParserTests
{
    private static readonly Guid Source = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Uri Page = new("https://gjirafamall.com/oferta-speciale");
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

    private static FeedParseResult Parse(string html, DateTimeOffset? now = null) =>
        HtmlSourceAdapter.ParseAsync(Source, new GjirafaMallDealsParser(), Page, html, now ?? Now, 3, TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();

    private static readonly string SavedHtml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Html", "gjirafamall-oferta-speciale-2026-10-14.html"));

    private static readonly Lazy<FeedParseResult> Saved = new(() => Parse(SavedHtml));

    private static CandidateFields Item(string slug) =>
        Saved.Value.Items.Single(i => i.Fields.Url == $"https://gjirafamall.com/{slug}").Fields;

    private static string Box(string slug, string title, string price, string? oldPrice, string? badge, string host = "") =>
        $"""
        <div class="item-box"><div class="product-item" data-productid="1">
          <section class="picture"><a href="{host}/{slug}"><img src="https://mallcdn.gjirafamall.tech/images/x/x.webp?width=196?quality=80" /></a></section>
          <h3 class="product-title"><a href="{host}/{slug}">{title}</a></h3>
          <section class="prices"><div class="flex flex-col"><span class="price font-bold">{price}</span>
          {(oldPrice is null ? "" : $"<span class=\"price old-price line-through\">{oldPrice}</span>")}</div>
          {(badge is null ? "" : $"<div class=\"bg-primary font-semibold\">{badge}</div>")}</section>
        </div></div>
        """;

    [Fact]
    public void The_saved_page_yields_only_the_products_with_an_old_price()
    {
        // 20 boxes: 10 on sale, 10 at the regular price (not deals).
        Assert.Null(Saved.Value.ErrorCode);
        Assert.Equal(10, Saved.Value.Items.Count);
        Assert.Empty(Saved.Value.Failures);
        Assert.All(Saved.Value.Items, i => Assert.Equal(ItemKind.Deal, i.Kind));
    }

    [Fact]
    public void A_discounted_product_has_exact_prices_badge_image_and_merchant()
    {
        var f = Item("monitor-dell-se2426hg-24-ips-fhd-240hz-i-zi");

        Assert.Equal("Monitor Dell SE2426HG, 24\" IPS, FHD, 240Hz, i zi", f.Title);
        Assert.Equal(99.50m, f.NewPrice);
        Assert.Equal(159.50m, f.OldPrice);
        Assert.Equal(38, f.StatedDiscountPercent);
        Assert.Equal("EUR", f.Currency);
        Assert.Equal("Gjirafa Mall", f.MerchantName);
        Assert.StartsWith("https://mallcdn.gjirafamall.tech/images/f4a2b4f5-", f.ImageUrl);
        Assert.Null(f.ValidUntil); // a listing deal has no end date (ADR-045)
    }

    [Fact]
    public void More_named_products_are_exact()
    {
        var a = Item("diesel-21-gratis");
        Assert.Equal(("Diesel 2+1 Gratis", 10.90m, 36.50m, 70), (a.Title, a.NewPrice, a.OldPrice, a.StatedDiscountPercent));
        var b = Item("rreshire-3d-anycubic-standard-resin-white-1kg-e-bardhe");
        Assert.Equal(("Rrëshirë 3D ANYCUBIC Standard Resin White, 1kg, e bardhë", 16.50m, 28.50m, 42), (b.Title, b.NewPrice, b.OldPrice, b.StatedDiscountPercent));
        var c = Item("pin-rezniqi-stema-e-shqiperise-i-ngjyrosur-me-te-zeze-dhe-te-kuqe");
        Assert.Equal((1.50m, 3.00m, 50), (c.NewPrice, c.OldPrice, c.StatedDiscountPercent));
        var d = Item("pluhur-stilimi-per-floke-zenix-natural-curly-20g");
        Assert.Equal("Pluhur stilimi për flokë Zenix Natural Curly, 20g", d.Title); // leading space collapsed
    }

    [Fact]
    public void Every_listing_on_the_saved_page_is_complete_and_the_badge_matches_the_prices()
    {
        var items = Saved.Value.Items.Select(i => i.Fields).ToList();

        Assert.All(items, f =>
        {
            Assert.False(string.IsNullOrWhiteSpace(f.Title));
            Assert.True(f.NewPrice > 0);
            Assert.NotNull(f.OldPrice);
            Assert.NotNull(f.ImageUrl);
            var computed = (int)Math.Round((f.OldPrice!.Value - f.NewPrice!.Value) / f.OldPrice.Value * 100);
            Assert.InRange(computed, f.StatedDiscountPercent!.Value - 1, f.StatedDiscountPercent.Value + 1);
        });
        Assert.Equal(5, items.Count(f => f.StatedDiscountPercent >= 35));
    }

    [Fact]
    public void A_thousands_group_and_a_missing_badge_are_handled()
    {
        var f = Parse($"<html><body>{Box("laptop-1", "Laptop", "1,299.00 €", "1,599.00 €", null)}</body></html>").Items.Single().Fields;

        Assert.Equal(1299.00m, f.NewPrice);
        Assert.Equal(1599.00m, f.OldPrice);
        Assert.Null(f.StatedDiscountPercent);
    }

    [Fact]
    public void A_box_without_an_old_price_is_not_a_listing()
    {
        var html = $"<html><body>{Box("a", "A", "5.00 €", null, null)}{Box("b", "B", "5.00 €", "10.00 €", "-50%")}</body></html>";

        Assert.Equal("https://gjirafamall.com/b", Parse(html).Items.Single().Fields.Url);
        Assert.Equal(FeedParseResult.Malformed.ErrorCode, Parse($"<html><body>{Box("a", "A", "5.00 €", null, null)}</body></html>").ErrorCode);
    }

    [Fact]
    public void A_repeated_box_is_one_listing_and_foreign_or_nested_links_are_ignored()
    {
        var html = "<html><body>" + Box("a", "A", "5.00 €", "10.00 €", "-50%") + Box("a", "A", "5.00 €", "10.00 €", "-50%")
            + Box("c", "C", "5.00 €", "10.00 €", "-50%", "https://other.example")
            + Box("x/y", "D", "5.00 €", "10.00 €", "-50%") + "</body></html>";

        var result = Parse(html);

        Assert.Single(result.Items);
        Assert.Single(result.Failures); // the foreign-host box; the nested path is never a listing
    }

    [Fact]
    public void Links_are_judged_the_same_on_every_os_and_there_is_no_next_page()
    {
        var parser = new GjirafaMallDealsParser();
        Assert.Equal("/x", ShopLinks.ProductPath("/x", Page, new System.Text.RegularExpressions.Regex("^/[a-z]+$")));
        Assert.Null(ShopLinks.ProductPath("//evil.com/x", Page, new System.Text.RegularExpressions.Regex("^/[a-z]+$")));
        Assert.Null(ShopLinks.ProductPath("file:///x", Page, new System.Text.RegularExpressions.Regex("^/[a-z]+$")));
        Assert.Null(ShopLinks.ProductPath("javascript:x", Page, new System.Text.RegularExpressions.Regex("^/[a-z]+$")));
        Assert.Null(((IHtmlSiteParser)parser).NextPage(null!, Page, CancellationToken.None));
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
