using App.Domain.Reference;
using App.Domain.Text;

namespace App.UnitTests;

public class TextNormalizerTests
{
    [Theory]
    [InlineData("Prishtinë", "prishtine")]
    [InlineData("  PRISHTINA ", "prishtina")]
    [InlineData("Fushë   Kosovë", "fushe kosove")]
    [InlineData("Deçan", "decan")]
    [InlineData("Đakovica / Uroševac, Peć!", "dakovica urosevac pec")]
    [InlineData("Žabljak Šabac Čačak", "zabljak sabac cacak")]
    [InlineData("Koncert: \"Rock-Nata\" 2026", "koncert rock nata 2026")]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    public void Folds_case_diacritics_punctuation_and_whitespace(string? input, string expected) =>
        Assert.Equal(expected, TextNormalizer.Fold(input));

    [Fact]
    public void Slug_joins_folded_words_with_dashes() => Assert.Equal("hani-i-elezit", TextNormalizer.Slug("Hani i Elezit"));
}

public class CityMatchingTests
{
    private static readonly List<City> Cities = ReferenceData.Cities
        .Select(c => new City { Slug = c.Slug, Name = c.Name, Aliases = [.. c.Aliases] }).ToList();

    [Theory]
    [InlineData("Prishtinë", "prishtine")]
    [InlineData("Pristina", "prishtine")]
    [InlineData("PRISHTINA", "prishtine")]
    [InlineData("Priština", "prishtine")]
    [InlineData("prishtine", "prishtine")]
    [InlineData("Mitrovicë e Veriut", "mitrovice-veriore")]
    [InlineData("Mitrovica", "mitrovice")]
    [InlineData("hani i elezit", "hani-i-elezit")]
    [InlineData("Peć", "peje")]
    public void Finds_cities_by_name_slug_or_alias(string text, string slug) =>
        Assert.Equal(slug, new CityMatcher(Cities).Find(text)?.Slug);

    [Theory]
    [InlineData("Tirana")]
    [InlineData("Skopje")]
    [InlineData("")]
    [InlineData(null)]
    public void Unknown_places_do_not_match(string? text) => Assert.Null(new CityMatcher(Cities).Find(text));

    [Fact]
    public void Reference_data_has_38_unique_city_slugs_and_14_categories()
    {
        Assert.Equal(38, ReferenceData.Cities.Select(c => c.Slug).Distinct().Count());
        Assert.Equal(14, ReferenceData.Categories.Select(c => (c.Slug, c.Kind)).Distinct().Count());
    }
}
