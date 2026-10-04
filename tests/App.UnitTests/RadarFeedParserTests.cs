using App.Application.Ingestion;
using App.Domain.Reference;

namespace App.UnitTests;

public class RadarFeedParserTests
{
    private static readonly Guid Source = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static FeedParseResult Parse(string? payload) => RadarFeedParser.Parse(Source, payload!);

    private static FeedParseResult One(string itemJson) => Parse($"{{\"items\":[{itemJson}]}}");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"items\":{}}")]
    [InlineData("{\"items\":\"x\"}")]
    public void Unusable_payload_is_malformed(string? payload)
    {
        var result = Parse(payload);

        Assert.Equal("MalformedPayload", result.ErrorCode);
        Assert.Empty(result.Items);
    }

    [Fact]
    public void Empty_items_array_is_a_valid_empty_feed()
    {
        var result = Parse("{\"items\":[]}");

        Assert.Null(result.ErrorCode);
        Assert.Equal(0, result.Received);
    }

    [Theory]
    [InlineData("42", "NotAnObject")]
    [InlineData("null", "NotAnObject")]
    [InlineData("\"text\"", "NotAnObject")]
    [InlineData("{\"url\":\"https://a.example/1\"}", "InvalidKind")]
    [InlineData("{\"kind\":\"concert\",\"url\":\"https://a.example/1\"}", "InvalidKind")]
    [InlineData("{\"kind\":5,\"url\":\"https://a.example/1\"}", "InvalidKind")]
    [InlineData("{\"kind\":\"event\"}", "MissingIdentity")]
    [InlineData("{\"kind\":\"event\",\"externalId\":\"  \",\"url\":null}", "MissingIdentity")]
    [InlineData("{\"kind\":\"event\",\"externalId\":\"a\",\"title\":12}", "InvalidField:title")]
    [InlineData("{\"kind\":\"event\",\"externalId\":\"a\",\"price\":\"12\"}", "InvalidField:price")]
    [InlineData("{\"kind\":\"event\",\"externalId\":\"a\",\"startsAt\":\"tomorrow\"}", "InvalidField:startsAt")]
    [InlineData("{\"kind\":\"event\",\"externalId\":\"a\",\"startsAt\":\"2026-10-04T20:00:00\"}", "InvalidField:startsAt")]
    [InlineData("{\"kind\":\"event\",\"externalId\":\"a\",\"startsAt\":\"10:30Z\"}", "InvalidField:startsAt")]
    [InlineData("{\"kind\":\"event\",\"externalId\":\"a\",\"startsAt\":\"5/10/2026 10:00 +02:00\"}", "InvalidField:startsAt")]
    [InlineData("{\"kind\":\"event\",\"externalId\":\"a\",\"startsAt\":\"Oct 5 2026 Z\"}", "InvalidField:startsAt")]
    [InlineData("{\"kind\":\"deal\",\"externalId\":\"a\",\"price\":\"3\"}", "InvalidField:price")]
    [InlineData("{\"kind\":\"event\",\"externalId\":\"a\",\"endsAt\":\"2026-13-40T20:00:00Z\"}", "InvalidField:endsAt")]
    [InlineData("{\"kind\":\"deal\",\"externalId\":\"a\",\"discountPercent\":12.5}", "InvalidField:discountPercent")]
    [InlineData("{\"kind\":\"deal\",\"externalId\":\"a\",\"oldPrice\":true}", "InvalidField:oldPrice")]
    [InlineData("{\"kind\":\"deal\",\"externalId\":\"a\",\"validUntil\":20261004}", "InvalidField:validUntil")]
    [InlineData("{\"kind\":\"event\",\"externalId\":\"a\",\"city\":[\"x\"]}", "InvalidField:city")]
    public void Each_per_item_failure_rule_is_reported(string item, string reason)
    {
        var result = One(item);

        Assert.Empty(result.Items);
        var failure = Assert.Single(result.Failures);
        Assert.Equal(reason, failure.Reason);
        Assert.Equal(1, result.Received);
    }

    [Fact]
    public void Overlong_external_id_fails()
    {
        var ok = One($"{{\"kind\":\"event\",\"externalId\":\"{new string('a', 200)}\"}}");
        var tooLong = One($"{{\"kind\":\"event\",\"externalId\":\"{new string('a', 201)}\"}}");

        Assert.Single(ok.Items);
        Assert.Equal("InvalidField:externalId", Assert.Single(tooLong.Failures).Reason);
    }

    [Fact]
    public void Overlong_url_fails()
    {
        var ok = One($"{{\"kind\":\"event\",\"url\":\"https://a.example/{new string('a', 2000 - 18)}\"}}");
        var tooLong = One($"{{\"kind\":\"event\",\"url\":\"https://a.example/{new string('a', 2000 - 17)}\"}}");

        Assert.Single(ok.Items);
        Assert.Equal("InvalidField:url", Assert.Single(tooLong.Failures).Reason);
    }

    [Fact]
    public void Item_size_limit_counts_UTF8_bytes()
    {
        var text = new string('ë', 33_000); // 33k characters, 66k bytes
        var result = One($"{{\"kind\":\"event\",\"externalId\":\"a\",\"text\":\"{text}\"}}");

        Assert.Equal("TooLarge", Assert.Single(result.Failures).Reason);
    }

    [Fact]
    public void Raw_item_over_64_KB_fails_but_exactly_64_KB_is_accepted()
    {
        const string prefix = "{\"kind\":\"event\",\"externalId\":\"a\",\"text\":\"";
        const string suffix = "\"}";
        string Item(int bytes) => prefix + new string('x', bytes - prefix.Length - suffix.Length) + suffix;

        var exact = One(Item(RadarFeedParser.MaxItemBytes));
        var over = One(Item(RadarFeedParser.MaxItemBytes + 1));

        Assert.Single(exact.Items);
        Assert.Equal("TooLarge", Assert.Single(over.Failures).Reason);
    }

    [Fact]
    public void A_bad_item_does_not_stop_the_others()
    {
        var result = Parse("""
            {"items":[
              {"kind":"event","externalId":"1","title":"A"},
              7,
              {"kind":"deal","externalId":"2","title":"B"},
              {"kind":"event"}
            ]}
            """);

        Assert.Equal(4, result.Received);
        Assert.Equal(["A", "B"], result.Items.Select(i => i.Fields.Title));
        Assert.Equal([1, 3], result.Failures.Select(f => f.Index));
    }

    [Fact]
    public void Missing_fields_stay_null_and_nothing_is_invented()
    {
        var c = Assert.Single(One("""{"kind":"event","url":"https://a.example/1"}""").Items);

        Assert.Equal(ItemKind.Event, c.Kind);
        Assert.Null(c.ExternalId);
        Assert.Null(c.Text);
        Assert.Equal("https://a.example/1", c.SourceUrl);
        Assert.Equal("https://a.example/1", c.Fields.Url);
        Assert.Null(c.Fields.Title);
        Assert.Null(c.Fields.StartsAt);
        Assert.Null(c.Fields.PriceAmount);
        Assert.Null(c.Fields.PriceText);
        Assert.Null(c.Fields.Currency);
        Assert.Null(c.Fields.CityText);
        Assert.Null(c.Fields.VenueName);
        Assert.Null(c.Fields.ImageUrl);
    }

    [Fact]
    public void Explicit_nulls_and_blank_strings_are_unknown()
    {
        var c = Assert.Single(One("""
            {"kind":"event","externalId":"1","title":"  ","price":null,"startsAt":null,"venue":"","city":null}
            """).Items);

        Assert.Null(c.Fields.Title);
        Assert.Null(c.Fields.PriceAmount);
        Assert.Null(c.Fields.StartsAt);
        Assert.Null(c.Fields.VenueName);
        Assert.Null(c.Fields.CityText);
    }

    [Fact]
    public void Event_mapping_follows_the_table()
    {
        var c = Assert.Single(One("""
            {"kind":"Event","externalId":"e-1","title":" Koncert ","description":"Pershkrim","text":"free text",
             "url":"https://a.example/e","imageUrl":"https://a.example/i.jpg","city":"Prishtina","venue":"Sheshi",
             "category":"muzike","startsAt":"2026-10-04T20:00:00+02:00","endsAt":"2026-10-04T23:00:00Z",
             "priceText":"5 euro","price":5.5,"currency":"EUR",
             "merchant":"ignored","oldPrice":9,"newPrice":8,"discountPercent":10,
             "validFrom":"2026-01-01T00:00:00Z","validUntil":"2026-02-01T00:00:00Z"}
            """).Items);
        var f = c.Fields;

        Assert.Equal("e-1", c.ExternalId);
        Assert.Equal("free text", c.Text);
        Assert.Equal("Koncert", f.Title);
        Assert.Equal("Pershkrim", f.Description);
        Assert.Equal("https://a.example/e", f.Url);
        Assert.Equal("https://a.example/e", c.SourceUrl);
        Assert.Equal("https://a.example/i.jpg", f.ImageUrl);
        Assert.Equal("Prishtina", f.CityText);
        Assert.Null(f.CitySlug);
        Assert.Equal("Sheshi", f.VenueName);
        Assert.Equal("muzike", f.CategoryText);
        Assert.Null(f.CategorySlug);
        Assert.Equal(DateTimeOffset.Parse("2026-10-04T18:00:00Z"), f.StartsAt);
        Assert.Equal(TimeSpan.FromHours(2), f.StartsAt!.Value.Offset);
        Assert.Equal(DateTimeOffset.Parse("2026-10-04T23:00:00Z"), f.EndsAt);
        Assert.Equal("5 euro", f.PriceText);
        Assert.Equal(5.5m, f.PriceAmount);
        Assert.Equal("EUR", f.Currency);
        Assert.Null(f.MerchantName);
        Assert.Null(f.OldPrice);
        Assert.Null(f.NewPrice);
        Assert.Null(f.StatedDiscountPercent);
        Assert.Null(f.ValidFrom);
        Assert.Null(f.ValidUntil);
    }

    [Fact]
    public void Deal_mapping_follows_the_table()
    {
        var c = Assert.Single(One("""
            {"kind":"deal","url":"https://a.example/d","title":"Ofert","merchant":"Shop","oldPrice":100,"newPrice":80.5,
             "discountPercent":20,"currency":"EUR","validFrom":"2026-01-01T00:00:00Z","validUntil":"2026-02-01T00:00:00+01:00",
             "price":3,"venue":"ignored","startsAt":"2026-10-04T20:00:00Z"}
            """).Items);
        var f = c.Fields;

        Assert.Equal(ItemKind.Deal, c.Kind);
        Assert.Equal("Shop", f.MerchantName);
        Assert.Equal(100m, f.OldPrice);
        Assert.Equal(80.5m, f.NewPrice);
        Assert.Equal(20, f.StatedDiscountPercent);
        Assert.Equal(DateTimeOffset.Parse("2026-01-01T00:00:00Z"), f.ValidFrom);
        Assert.Equal(DateTimeOffset.Parse("2026-01-31T23:00:00Z"), f.ValidUntil);
        Assert.Null(f.PriceAmount);
        Assert.Null(f.VenueName);
        Assert.Null(f.StartsAt);
    }

    [Fact]
    public void Evidence_is_the_raw_item_text_exactly()
    {
        const string raw = "{ \"kind\" : \"event\",\n  \"externalId\":\"1\" ,\"title\":\"Sh\\u00ebndet\" }";
        var c = Assert.Single(One(raw).Items);

        Assert.Equal(raw, c.Evidence);
    }

    [Fact]
    public void Fingerprint_uses_external_id_before_url_and_depends_on_the_source()
    {
        var byId = Assert.Single(One("""{"kind":"event","externalId":"x","url":"https://a.example/1"}""").Items);
        var sameId = Assert.Single(One("""{"kind":"event","externalId":"x","url":"https://a.example/2"}""").Items);
        var byUrl = Assert.Single(One("""{"kind":"event","url":"https://a.example/1"}""").Items);
        var other = Assert.Single(RadarFeedParser.Parse(Guid.NewGuid(), """{"items":[{"kind":"event","externalId":"x"}]}""").Items);

        Assert.Equal(byId.Fingerprint, sameId.Fingerprint);
        Assert.NotEqual(byId.Fingerprint, byUrl.Fingerprint);
        Assert.NotEqual(byId.Fingerprint, other.Fingerprint);
        Assert.Equal(64, byId.Fingerprint.Length);
    }

    [Fact]
    public void Content_hash_is_stable_and_changes_with_content()
    {
        const string item = """{"kind":"event","externalId":"1","title":"A","startsAt":"2026-10-04T20:00:00Z"}""";
        var first = Assert.Single(One(item).Items);
        var again = Assert.Single(One(item).Items);
        var title = Assert.Single(One(item.Replace("\"A\"", "\"B\"")).Items);
        var text = Assert.Single(One(item.Replace("}", ",\"text\":\"more\"}")).Items);

        Assert.Equal(first.ContentHash, again.ContentHash);
        Assert.NotEqual(first.ContentHash, title.ContentHash);
        Assert.NotEqual(first.ContentHash, text.ContentHash);
        Assert.Equal(first.Fingerprint, title.Fingerprint);
    }
}
