using App.Infrastructure.Ingestion;

namespace App.UnitTests;

public class ShopPriceTests
{
    [Theory]
    [InlineData("89,00 €", 89.00)]
    [InlineData("1.299,00 €", 1299.00)]
    [InlineData("€ 12,5", 12.5)]
    [InlineData("59.50", 59.50)]
    [InlineData("1,299.00 EUR", 1299.00)]
    [InlineData("1.299 €", 1299)]
    [InlineData("1 299,90 €", 1299.90)]
    [InlineData("Çmimi: 7 €", 7)]
    [InlineData("0,99 €", 0.99)]
    public void A_price_as_a_shop_prints_it_is_read(string text, double expected) =>
        Assert.Equal((decimal)expected, ShopPrice.Parse(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Pa çmim")]
    public void Text_without_an_amount_is_null(string? text) => Assert.Null(ShopPrice.Parse(text));
}
