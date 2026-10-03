using System.Text.Json;
using System.Text.Json.Nodes;

namespace App.AcceptanceTests;

/// <summary>Relies on the deterministic Fake AI provider of design.md §6 (Testing environment).</summary>
[Collection(AcceptanceCollection.Name)]
public class AiTests(AppFactory app)
{
    private async Task<JsonElement> IngestOneAsync(JsonObject item)
    {
        var admin = await Radar.AdminAsync(app);
        return await Radar.IngestOneAsync(admin, item);
    }

    private static JsonObject BareEvent(string text) => new()
    {
        ["externalId"] = "ev-" + Guid.NewGuid().ToString("N"),
        ["kind"] = "event",
        ["title"] = Radar.Title(Radar.Token(), "Mbrëmje"),
        ["url"] = $"https://example.com/ai/{Guid.NewGuid():N}",
        ["text"] = text,
    };

    private static List<string> SuggestedFields(JsonElement c) =>
        c.GetProperty("ai").GetProperty("suggestedFields").Array().Select(x => x.GetString()!).ToList();

    // ---------- REQ-040 ----------

    [Fact, Trait("Req", "REQ-040")]
    public async Task Ai_fills_only_missing_fields_and_records_its_metadata()
    {
        var start = Clock.Future(20);
        var item = BareEvent($"Koncert live më {Clock.Iso(start)} në Prizren. Bileta 12,50 € në hyrje.");
        var c = await IngestOneAsync(item);
        var f = c.GetProperty("fields");

        Assert.Equal(start, f.Time("startsAt"));
        Assert.Equal(12.5m, f.Dec("priceAmount"));
        Assert.Equal("EUR", f.Str("currency"));
        Assert.Equal("prizren", f.Str("citySlug"));
        Assert.Equal(item["title"]!.GetValue<string>(), f.Str("title"));

        var ai = c.GetProperty("ai");
        Assert.Equal("fake-1", ai.Str("model"));
        Assert.StartsWith("fake-", ai.Str("requestId"));
        Assert.Equal(0.9m, ai.Dec("confidence"));
        Assert.False(ai.IsNull("processedAt"));
        var suggested = SuggestedFields(c);
        foreach (var name in new[] { "startsAt", "priceAmount", "currency", "cityText", "citySlug" })
            Assert.Contains(name, suggested);
        Assert.DoesNotContain("title", suggested);
        Assert.DoesNotContain("url", suggested);
        Assert.DoesNotContain("AiUnavailable", c.IssueCodes());
        Assert.Equal(item.Raw(), c.Str("evidence"));
    }

    [Fact, Trait("Req", "REQ-040")]
    public async Task Ai_does_not_touch_fields_the_source_provides()
    {
        var start = Clock.Future(21);
        var sameInstantOtherOffset = start.ToOffset(start.Offset + TimeSpan.FromHours(1));
        var item = BareEvent($"Koncert më {Clock.Iso(sameInstantOtherOffset)} në Prizren, 12,50 €.");
        item["startsAt"] = Clock.Iso(start);
        item["city"] = "Prizren";
        item["price"] = 12.5m;
        item["currency"] = "EUR";

        var c = await IngestOneAsync(item);

        var suggested = SuggestedFields(c);
        foreach (var name in new[] { "startsAt", "priceAmount", "currency", "cityText", "citySlug" })
            Assert.DoesNotContain(name, suggested);
        Assert.Equal(start, c.GetProperty("fields").Time("startsAt"));
        Assert.DoesNotContain("ConflictingData", c.IssueCodes());
        Assert.Equal("valid", c.Str("validation"));
    }

    [Fact, Trait("Req", "REQ-040")]
    public async Task Ai_fills_a_deal_new_price_not_the_event_price()
    {
        var item = Radar.DealItem(Radar.Title(Radar.Token(), "Ofertë"));
        item.Remove("oldPrice");
        item.Remove("newPrice");
        item.Remove("currency");
        item["text"] = "Laptop me vetëm 499 € këtë javë.";

        var c = await IngestOneAsync(item);
        var f = c.GetProperty("fields");

        Assert.Equal(499m, f.Dec("newPrice"));
        Assert.Equal("EUR", f.Str("currency"));
        Assert.True(f.IsNull("priceAmount"));
        var suggested = SuggestedFields(c);
        Assert.Contains("newPrice", suggested);
        Assert.Contains("currency", suggested);
        Assert.DoesNotContain("MissingPrice", c.IssueCodes());
    }

    [Fact, Trait("Req", "REQ-040")]
    public async Task Without_text_no_ai_is_used()
    {
        var c = await IngestOneAsync(Radar.EventItem(Radar.Title(Radar.Token())));
        Assert.True(c.IsNull("ai"));
    }

    // ---------- REQ-041 ----------

    [Theory, Trait("Req", "REQ-041")]
    [InlineData("#ai-fail")]
    [InlineData("#ai-invalid")]
    public async Task Ai_failure_or_invalid_output_needs_review_and_invents_nothing(string marker)
    {
        var start = Clock.Future(15);
        var c = await IngestOneAsync(BareEvent($"{marker} Koncert më {Clock.Iso(start)} në Prizren, bileta 10 €."));
        var f = c.GetProperty("fields");

        Assert.Contains("AiUnavailable", c.IssueCodes());
        Assert.Equal("needsReview", c.Str("validation"));
        Assert.Equal("pending", c.Str("status"));
        foreach (var name in new[] { "startsAt", "endsAt", "citySlug", "venueName", "priceAmount", "priceText", "currency", "categorySlug" })
            Assert.True(f.IsNull(name), $"fields.{name} should be null");
        Assert.False(string.IsNullOrEmpty(f.Str("title")));
    }

    [Fact, Trait("Req", "REQ-041")]
    public async Task Ai_failure_keeps_the_values_the_source_provided()
    {
        var start = Clock.Future(16);
        var item = BareEvent("#ai-fail Koncert në Pejë, 99 €.");
        item["startsAt"] = Clock.Iso(start);
        item["city"] = "Prizren";

        var c = await IngestOneAsync(item);
        var f = c.GetProperty("fields");

        Assert.Contains("AiUnavailable", c.IssueCodes());
        Assert.Equal("needsReview", c.Str("validation"));
        Assert.Equal(start, f.Time("startsAt"));
        Assert.Equal("prizren", f.Str("citySlug"));
        Assert.True(f.IsNull("priceAmount"));
    }

    // ---------- REQ-042 ----------

    [Theory, Trait("Req", "REQ-042")]
    [InlineData("startsAt")]
    [InlineData("city")]
    [InlineData("price")]
    [InlineData("newPrice")]
    public async Task Ai_value_conflicting_with_the_source_keeps_the_source_value(string conflict)
    {
        var start = Clock.Future(25);
        JsonObject item;
        switch (conflict)
        {
            case "startsAt":
                item = Radar.EventItem(Radar.Title(Radar.Token()), start);
                item["text"] = $"Fillon më {Clock.Iso(start.AddHours(1))}.";
                break;
            case "city":
                item = Radar.EventItem(Radar.Title(Radar.Token()), start, city: "Prizren");
                item["text"] = "Shfaqja mbahet në Pejë.";
                break;
            case "price":
                item = Radar.EventItem(Radar.Title(Radar.Token()), start);
                item["price"] = 10m;
                item["currency"] = "EUR";
                item["text"] = "Bileta kushton 15 €.";
                break;
            default:
                item = Radar.DealItem(Radar.Title(Radar.Token(), "Ofertë"), oldPrice: 100m, newPrice: 80m);
                item["text"] = "Tani vetëm 79 €.";
                break;
        }

        var c = await IngestOneAsync(item);
        var f = c.GetProperty("fields");

        Assert.Contains("ConflictingData", c.IssueCodes());
        Assert.Equal("needsReview", c.Str("validation"));
        switch (conflict)
        {
            case "startsAt": Assert.Equal(start, f.Time("startsAt")); break;
            case "city": Assert.Equal("prizren", f.Str("citySlug")); break;
            case "price": Assert.Equal(10m, f.Dec("priceAmount")); break;
            default: Assert.Equal(80m, f.Dec("newPrice")); break;
        }
    }

    // ---------- REQ-043 ----------

    [Theory, Trait("Req", "REQ-043")]
    [InlineData("event", "Koncert i madh në qendër.", "muzike")]
    [InlineData("event", "Premiera e filmit të ri.", "teater")]
    [InlineData("event", "Ndeshje futbolli e javës.", "sport")]
    [InlineData("deal", "Telefon i ri me zbritje.", "teknologji")]
    public async Task Ai_category_becomes_the_suggested_category(string kind, string text, string slug)
    {
        var item = kind == "event"
            ? Radar.EventItem(Radar.Title(Radar.Token(), "Mbrëmje"))
            : Radar.DealItem(Radar.Title(Radar.Token(), "Ofertë"));
        item["text"] = text;

        var c = await IngestOneAsync(item);

        var suggested = c.GetProperty("suggestedCategory");
        Assert.Equal(JsonValueKind.Object, suggested.ValueKind);
        Assert.Equal(slug, suggested.Str("slug"));
        Assert.Equal(kind, suggested.Str("kind"));
        Assert.False(string.IsNullOrEmpty(suggested.Str("name")));
        Assert.True(c.GetProperty("fields").IsNull("categorySlug"));
    }

    [Fact, Trait("Req", "REQ-043")]
    public async Task Ai_category_does_not_change_the_category_the_source_states()
    {
        var item = Radar.EventItem(Radar.Title(Radar.Token(), "Mbrëmje"));
        item["category"] = "sport";
        item["text"] = "Koncert i madh në qendër.";

        var c = await IngestOneAsync(item);

        Assert.Equal("sport", c.GetProperty("fields").Str("categorySlug"));
    }

    [Fact, Trait("Req", "REQ-043")]
    public async Task No_suggested_category_when_the_ai_has_none_for_the_kind()
    {
        var item = Radar.EventItem(Radar.Title(Radar.Token(), "Mbrëmje"));
        item["text"] = "Laptop dhe telefon për fëmijë.";
        var c = await IngestOneAsync(item);
        Assert.True(c.IsNull("suggestedCategory"));
    }
}
