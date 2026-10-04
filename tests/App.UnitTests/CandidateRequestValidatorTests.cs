using App.Application.Candidates;
using App.Domain.Content;
using App.Domain.Reference;

namespace App.UnitTests;

public class CandidateRequestValidatorTests
{
    private static CandidateFieldsRequest Ok() => new(
        "Titulli", null, "https://example.test/a", null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);

    [Fact]
    public void A_minimal_request_is_valid_and_blank_text_becomes_null()
    {
        var (req, errors) = CandidateRequestValidator.Validate(Ok() with { Description = "  ", VenueName = "" });

        Assert.Empty(errors);
        Assert.Null(req!.Description);
        Assert.Null(req.VenueName);
    }

    [Theory]
    [InlineData(300, true)]
    [InlineData(301, false)]
    public void Title_length_boundary(int length, bool valid) =>
        Assert.Equal(valid, CandidateRequestValidator.Validate(Ok() with { Title = new string('x', length) }).Errors.Count == 0);

    [Fact]
    public void A_missing_title_is_an_error() =>
        Assert.True(CandidateRequestValidator.Validate(Ok() with { Title = " " }).Errors.ContainsKey("title"));

    [Theory]
    [InlineData("ftp://example.test/x", false)]
    [InlineData("/relative", false)]
    [InlineData("https://example.test/x", true)]
    public void Urls_must_be_absolute_http(string url, bool valid)
    {
        Assert.Equal(valid, !CandidateRequestValidator.Validate(Ok() with { Url = url }).Errors.ContainsKey("url"));
        Assert.Equal(valid, !CandidateRequestValidator.Validate(Ok() with { ImageUrl = url }).Errors.ContainsKey("imageUrl"));
    }

    [Theory]
    [InlineData("EUR", true)]
    [InlineData("eur", false)]
    [InlineData("EURO", false)]
    public void Currency_is_three_capitals(string currency, bool valid) =>
        Assert.Equal(valid, !CandidateRequestValidator.Validate(Ok() with { Currency = currency }).Errors.ContainsKey("currency"));

    [Theory]
    [InlineData("0", true)]
    [InlineData("1000000", true)]
    [InlineData("-0.01", false)]
    [InlineData("1000000.01", false)]
    public void Prices_are_between_0_and_a_million(string amount, bool valid) =>
        Assert.Equal(valid, !CandidateRequestValidator.Validate(Ok() with { NewPrice = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture) }).Errors.ContainsKey("newPrice"));

    [Theory]
    [InlineData("a", true)]
    [InlineData("  a  ", true)]
    [InlineData(null, false)]
    [InlineData("   ", false)]
    public void Reason_is_required_after_trim(string? reason, bool valid) =>
        Assert.Equal(valid, CandidateRequestValidator.ValidateReason(new RejectRequest(reason)).Reason is not null);

    [Theory]
    [InlineData(500, true)]
    [InlineData(501, false)]
    public void Reason_length_boundary(int length, bool valid) =>
        Assert.Equal(valid, CandidateRequestValidator.ValidateReason(new RejectRequest(new string('x', length))).Reason is not null);

    [Fact]
    public void Apply_keeps_the_source_text_and_clears_fields_of_the_other_kind()
    {
        var current = new CandidateFields { CityText = "Prishtina", CategoryText = "Music", Title = "old" };
        var req = Ok() with { VenueName = "Hall", OldPrice = 5, NewPrice = 4 };

        var deal = CandidateRequestValidator.Apply(ItemKind.Deal, current, req);
        var ev = CandidateRequestValidator.Apply(ItemKind.Event, current, req);

        Assert.Equal("Prishtina", deal.CityText);
        Assert.Equal("Music", deal.CategoryText);
        Assert.Null(deal.VenueName);
        Assert.Equal(4, deal.NewPrice);
        Assert.Null(ev.NewPrice);
        Assert.Equal("Hall", ev.VenueName);
    }
}
