using System.Text.Json;
using System.Text.Json.Nodes;

namespace App.AcceptanceTests;

[Collection(AcceptanceCollection.Name)]
public class ValidationTests(AppFactory app)
{
    private async Task<JsonElement> IngestOneAsync(JsonObject item, bool unreliable = false)
    {
        var admin = await Radar.AdminAsync(app);
        return await Radar.IngestOneAsync(admin, item, isUnreliable: unreliable);
    }

    private static JsonObject Event(Action<JsonObject>? change = null)
    {
        var item = Radar.EventItem(Radar.Title(Radar.Token()));
        change?.Invoke(item);
        return item;
    }

    private static JsonObject Deal(Action<JsonObject>? change = null)
    {
        var item = Radar.DealItem(Radar.Title(Radar.Token(), "Ofertë"));
        change?.Invoke(item);
        return item;
    }

    private static void AssertReviewWith(JsonElement c, string code)
    {
        Assert.Contains(code, c.IssueCodes());
        Assert.Equal("needsReview", c.Str("validation"));
        Assert.Equal("pending", c.Str("status"));
        var issue = c.GetProperty("issues").Array().First(i => i.Str("code") == code);
        Assert.Equal("review", issue.Str("severity"));
        Assert.False(string.IsNullOrWhiteSpace(issue.Str("message")));
    }

    private static void AssertRejectedWith(JsonElement c, string code)
    {
        Assert.Contains(code, c.IssueCodes());
        Assert.Equal("rejected", c.Str("validation"));
        Assert.Equal("rejected", c.Str("status"));
        Assert.Equal("rejected", c.GetProperty("issues").Array().First(i => i.Str("code") == code).Str("severity"));
    }

    // ---------- REQ-027 ----------

    [Fact, Trait("Req", "REQ-027")]
    public async Task Deal_without_new_price_or_price_text_needs_review_with_MissingPrice()
    {
        var c = await IngestOneAsync(Deal(d => { d.Remove("newPrice"); d.Remove("oldPrice"); }));
        AssertReviewWith(c, "MissingPrice");
        Assert.True(c.GetProperty("fields").IsNull("newPrice"));
        Assert.True(c.GetProperty("fields").IsNull("priceText"));
    }

    [Fact, Trait("Req", "REQ-027")]
    public async Task Deal_with_only_a_price_text_or_only_a_new_price_has_no_MissingPrice()
    {
        var textOnly = await IngestOneAsync(Deal(d => { d.Remove("newPrice"); d.Remove("oldPrice"); d["priceText"] = "Falas"; }));
        Assert.DoesNotContain("MissingPrice", textOnly.IssueCodes());
        var newOnly = await IngestOneAsync(Deal(d => d.Remove("oldPrice")));
        Assert.DoesNotContain("MissingPrice", newOnly.IssueCodes());
        Assert.Equal("valid", newOnly.Str("validation"));
    }

    // ---------- REQ-028 ----------

    [Fact, Trait("Req", "REQ-028")]
    public async Task Valid_and_needs_review_candidates_are_pending()
    {
        var valid = await IngestOneAsync(Event());
        Assert.Equal("valid", valid.Str("validation"));
        Assert.Equal("pending", valid.Str("status"));

        var review = await IngestOneAsync(Event(e => e.Remove("startsAt")));
        Assert.Equal("needsReview", review.Str("validation"));
        Assert.Equal("pending", review.Str("status"));
    }

    [Fact, Trait("Req", "REQ-028")]
    public async Task Rejected_validation_sets_status_rejected_without_a_reviewer()
    {
        var c = await IngestOneAsync(Event(e => e.Remove("title")));
        Assert.Equal("rejected", c.Str("validation"));
        Assert.Equal("rejected", c.Str("status"));
        Assert.True(c.IsNull("rejectionReason"));
        // No person acted on it: there is no candidate.reject audit entry.
        var admin = await Radar.AdminAsync(app);
        Assert.DoesNotContain(await Radar.AuditAsync(admin, c.Id()), a => a.Str("action") == "candidate.reject");
    }

    // ---------- REQ-029 ----------

    [Fact, Trait("Req", "REQ-029")]
    public async Task Event_ending_before_it_starts_needs_review_with_InvalidDateRange()
    {
        var start = Clock.Future(6);
        var c = await IngestOneAsync(Event(e => { e["startsAt"] = Clock.Iso(start); e["endsAt"] = Clock.Iso(start.AddHours(-1)); }));
        AssertReviewWith(c, "InvalidDateRange");
    }

    [Fact, Trait("Req", "REQ-029")]
    public async Task Deal_valid_until_before_valid_from_needs_review_with_InvalidDateRange()
    {
        var c = await IngestOneAsync(Deal(d => { d["validFrom"] = Clock.Iso(Clock.Future(12)); d["validUntil"] = Clock.Iso(Clock.Future(8)); }));
        AssertReviewWith(c, "InvalidDateRange");
    }

    [Fact, Trait("Req", "REQ-029")]
    public async Task Event_ending_exactly_when_it_starts_is_not_an_invalid_range()
    {
        var start = Clock.Future(6);
        var c = await IngestOneAsync(Event(e => { e["startsAt"] = Clock.Iso(start); e["endsAt"] = Clock.Iso(start); }));
        Assert.DoesNotContain("InvalidDateRange", c.IssueCodes());
    }

    // ---------- REQ-030 ----------

    [Fact, Trait("Req", "REQ-030")]
    public async Task Fields_the_source_does_not_provide_stay_null_and_evidence_is_the_original_text()
    {
        var item = new JsonObject
        {
            ["externalId"] = "ev-" + Guid.NewGuid().ToString("N"),
            ["kind"] = "event",
            ["title"] = Radar.Title(Radar.Token()),
            ["url"] = $"https://example.com/minimal/{Guid.NewGuid():N}",
        };
        var c = await IngestOneAsync(item);
        var f = c.GetProperty("fields");

        foreach (var name in new[] { "description", "imageUrl", "cityText", "citySlug", "venueName", "categoryText", "categorySlug",
                     "startsAt", "endsAt", "priceText", "priceAmount", "currency", "merchantName", "oldPrice", "newPrice",
                     "statedDiscountPercent", "discountPercent", "validFrom", "validUntil" })
            Assert.True(f.IsNull(name), $"fields.{name} should be null");
        Assert.Equal(item["title"]!.GetValue<string>(), f.Str("title"));
        Assert.Equal(item.Raw(), c.Str("evidence"));
        Assert.True(c.IsNull("suggestedCategory"));
    }

    [Fact, Trait("Req", "REQ-030")]
    public async Task Deal_without_prices_or_dates_invents_none_and_keeps_evidence()
    {
        var item = new JsonObject
        {
            ["externalId"] = "dl-" + Guid.NewGuid().ToString("N"),
            ["kind"] = "deal",
            ["title"] = Radar.Title(Radar.Token(), "Ofertë"),
            ["url"] = $"https://example.com/minimal/{Guid.NewGuid():N}",
            ["description"] = "Zbritje   e madhe — çdo ditë",
        };
        var c = await IngestOneAsync(item);
        var f = c.GetProperty("fields");
        foreach (var name in new[] { "oldPrice", "newPrice", "priceText", "currency", "discountPercent", "validFrom", "validUntil",
                     "citySlug", "merchantName", "imageUrl", "categorySlug" })
            Assert.True(f.IsNull(name), $"fields.{name} should be null");
        Assert.Equal(item.Raw(), c.Str("evidence"));
    }

    // ---------- REQ-031 ----------

    [Fact, Trait("Req", "REQ-031")]
    public async Task Candidate_without_title_is_rejected_with_MissingTitle()
    {
        AssertRejectedWith(await IngestOneAsync(Event(e => e.Remove("title"))), "MissingTitle");
        AssertRejectedWith(await IngestOneAsync(Deal(d => d["title"] = "   ")), "MissingTitle");
    }

    [Theory, Trait("Req", "REQ-031")]
    [InlineData("ftp://example.com/event")]
    [InlineData("/events/123")]
    [InlineData("javascript:alert(1)")]
    [InlineData("not a url")]
    public async Task Candidate_with_non_http_url_is_rejected_with_InvalidUrl(string url)
    {
        AssertRejectedWith(await IngestOneAsync(Event(e => e["url"] = url)), "InvalidUrl");
    }

    [Fact, Trait("Req", "REQ-031")]
    public async Task Http_and_https_urls_are_accepted()
    {
        var http = await IngestOneAsync(Event(e => e["url"] = $"http://example.com/e/{Guid.NewGuid():N}"));
        Assert.DoesNotContain("InvalidUrl", http.IssueCodes());
        Assert.Equal("valid", http.Str("validation"));
    }

    // ---------- REQ-032 ----------

    [Fact, Trait("Req", "REQ-032")]
    public async Task Event_that_has_already_ended_is_rejected_with_Expired()
    {
        var start = Clock.Now.AddDays(-2);
        AssertRejectedWith(await IngestOneAsync(Event(e => e["startsAt"] = Clock.Iso(start))), "Expired");
        AssertRejectedWith(await IngestOneAsync(Event(e => { e["startsAt"] = Clock.Iso(Clock.Now.AddHours(-3)); e["endsAt"] = Clock.Iso(Clock.Now.AddHours(-1)); })), "Expired");
    }

    [Fact, Trait("Req", "REQ-032")]
    public async Task Event_that_started_less_than_six_hours_ago_without_end_is_not_expired()
    {
        var c = await IngestOneAsync(Event(e => e["startsAt"] = Clock.Iso(Clock.Now.AddHours(-5))));
        Assert.DoesNotContain("Expired", c.IssueCodes());
        Assert.Equal("valid", c.Str("validation"));
    }

    [Fact, Trait("Req", "REQ-032")]
    public async Task Event_with_end_in_the_future_is_not_expired_even_if_it_started_long_ago()
    {
        var c = await IngestOneAsync(Event(e => { e["startsAt"] = Clock.Iso(Clock.Now.AddHours(-10)); e["endsAt"] = Clock.Iso(Clock.Now.AddHours(2)); }));
        Assert.DoesNotContain("Expired", c.IssueCodes());
    }

    [Fact, Trait("Req", "REQ-032")]
    public async Task Deal_whose_valid_until_has_passed_is_rejected_with_Expired()
    {
        AssertRejectedWith(await IngestOneAsync(Deal(d => d["validUntil"] = Clock.Iso(Clock.Now.AddDays(-1)))), "Expired");
    }

    [Fact, Trait("Req", "REQ-032")]
    public async Task Expiry_is_judged_against_the_fetch_time()
    {
        var admin = await Radar.AdminAsync(app);
        var start = Clock.Future(3);
        // Fetched (by the test clock) one day after the event: expired.
        var late = await Radar.IngestOneAsync(admin, Event(e => e["startsAt"] = Clock.Iso(start)), start.AddDays(1));
        AssertRejectedWith(late, "Expired");
        // Fetched two days before it: fine.
        var early = await Radar.IngestOneAsync(admin, Event(e => e["startsAt"] = Clock.Iso(start)), start.AddDays(-2));
        Assert.DoesNotContain("Expired", early.IssueCodes());
    }

    // ---------- REQ-033 ----------

    [Fact, Trait("Req", "REQ-033")]
    public async Task Event_without_start_needs_review_with_MissingStartsAt()
    {
        var c = await IngestOneAsync(Event(e => e.Remove("startsAt")));
        AssertReviewWith(c, "MissingStartsAt");
        Assert.True(c.GetProperty("fields").IsNull("startsAt"));
    }

    // ---------- REQ-034 ----------

    [Fact, Trait("Req", "REQ-034")]
    public async Task Event_without_city_needs_review_with_UnknownCity()
    {
        var c = await IngestOneAsync(Event(e => e.Remove("city")));
        AssertReviewWith(c, "UnknownCity");
        Assert.True(c.GetProperty("fields").IsNull("citySlug"));
    }

    [Theory, Trait("Req", "REQ-034")]
    [InlineData("event", "Tirana")]
    [InlineData("event", "Shkup")]
    [InlineData("deal", "Beograd")]
    public async Task Candidate_with_a_non_Kosovo_city_needs_review_with_UnknownCity(string kind, string city)
    {
        var item = kind == "event" ? Event(e => e["city"] = city) : Deal(d => d["city"] = city);
        var c = await IngestOneAsync(item);
        AssertReviewWith(c, "UnknownCity");
        Assert.True(c.GetProperty("fields").IsNull("citySlug"));
        Assert.Equal(city, c.GetProperty("fields").Str("cityText"));
    }

    [Theory, Trait("Req", "REQ-034")]
    [InlineData("Prishtinë", "prishtine")]
    [InlineData("Pristina", "prishtine")]
    [InlineData("PRISHTINA", "prishtine")]
    [InlineData("prishtine", "prishtine")]
    [InlineData("Peć", "peje")]
    [InlineData("Gjakova", "gjakove")]
    [InlineData("Mitrovicë e Veriut", "mitrovice-veriore")]
    [InlineData("North Mitrovica", "mitrovice-veriore")]
    [InlineData("Fushë Kosovë", "fushe-kosove")]
    [InlineData("Gllogoc", "drenas")]
    [InlineData("Zveçan", "zvecan")]
    public async Task Kosovo_municipality_names_slugs_and_aliases_resolve(string city, string slug)
    {
        var c = await IngestOneAsync(Event(e => e["city"] = city));
        Assert.DoesNotContain("UnknownCity", c.IssueCodes());
        Assert.Equal(slug, c.GetProperty("fields").Str("citySlug"));
    }

    [Fact, Trait("Req", "REQ-034")]
    public async Task Deal_without_city_has_no_UnknownCity()
    {
        var c = await IngestOneAsync(Deal(d => d.Remove("city")));
        Assert.DoesNotContain("UnknownCity", c.IssueCodes());
        Assert.Equal("valid", c.Str("validation"));
    }

    // ---------- REQ-035 ----------

    [Theory, Trait("Req", "REQ-035")]
    [InlineData(null, -5)]
    [InlineData(50, 50)]
    [InlineData(40, 50)]
    [InlineData(-10, 5)]
    public async Task Inconsistent_deal_prices_need_review_with_PriceInconsistent(int? oldPrice, int newPrice)
    {
        var c = await IngestOneAsync(Deal(d => { d["oldPrice"] = oldPrice; d["newPrice"] = newPrice; d.Compact(); }));
        AssertReviewWith(c, "PriceInconsistent");
    }

    [Fact, Trait("Req", "REQ-035")]
    public async Task Old_price_greater_than_new_price_is_consistent()
    {
        var c = await IngestOneAsync(Deal(d => { d["oldPrice"] = 50.01m; d["newPrice"] = 50m; }));
        Assert.DoesNotContain("PriceInconsistent", c.IssueCodes());
        var zero = await IngestOneAsync(Deal(d => { d["oldPrice"] = 10m; d["newPrice"] = 0m; }));
        Assert.DoesNotContain("PriceInconsistent", zero.IssueCodes());
    }

    // ---------- REQ-036 ----------

    [Theory, Trait("Req", "REQ-036")]
    [InlineData("30", "20", 33)]
    [InlineData("8", "7", 13)]
    [InlineData("200", "199", 1)]
    [InlineData("3", "1", 67)]
    [InlineData("100", "80", 20)]
    [InlineData("49.99", "24.99", 50)]
    public async Task Discount_percent_is_computed_and_rounded_half_away_from_zero(string oldPrice, string newPrice, int expected)
    {
        var o = decimal.Parse(oldPrice, System.Globalization.CultureInfo.InvariantCulture);
        var n = decimal.Parse(newPrice, System.Globalization.CultureInfo.InvariantCulture);
        var c = await IngestOneAsync(Deal(d => { d["oldPrice"] = o; d["newPrice"] = n; }));
        Assert.Equal(expected, c.GetProperty("fields").Dec("discountPercent"));
    }

    [Fact, Trait("Req", "REQ-036")]
    public async Task Discount_is_null_without_both_prices_and_shown_on_the_published_deal()
    {
        var single = await IngestOneAsync(Deal(d => d.Remove("oldPrice")));
        Assert.True(single.GetProperty("fields").IsNull("discountPercent"));

        var admin = await Radar.AdminAsync(app);
        var published = await Radar.PublishAsync(admin, Deal(d => { d["oldPrice"] = 8m; d["newPrice"] = 7m; }));
        var deal = await app.CreateClient().Get(published.PublicPath).Json();
        Assert.Equal(13m, deal.Dec("discountPercent"));
    }

    // ---------- REQ-037 ----------

    [Fact, Trait("Req", "REQ-037")]
    public async Task Candidates_without_review_or_rejected_issues_are_valid()
    {
        foreach (var item in new[] { Event(), Deal() })
        {
            var c = await IngestOneAsync(item);
            Assert.Equal("valid", c.Str("validation"));
            Assert.Equal("pending", c.Str("status"));
            Assert.DoesNotContain(c.GetProperty("issues").Array(), i => i.Str("severity") is "review" or "rejected");
        }
    }

    [Fact, Trait("Req", "REQ-037")]
    public async Task Info_issue_alone_keeps_the_candidate_valid()
    {
        var c = await IngestOneAsync(Event(e => e["imageUrl"] = "not-a-url"));
        Assert.Equal("valid", c.Str("validation"));
        Assert.DoesNotContain(c.GetProperty("issues").Array(), i => i.Str("severity") is "review" or "rejected");
    }

    // ---------- REQ-038 ----------

    [Fact, Trait("Req", "REQ-038")]
    public async Task Candidates_from_an_unreliable_source_need_review_with_SourceUnreliable()
    {
        var admin = await Radar.AdminAsync(app);
        var group = Radar.Token();
        var ing = await Radar.IngestAsync(admin, [Radar.EventItem(Radar.Title(group)), Radar.DealItem(Radar.Title(group, "Ofertë"))], isUnreliable: true);
        Assert.Equal(2, ing.Candidates.Count);
        foreach (var c in ing.Candidates) AssertReviewWith(c, "SourceUnreliable");
    }

    [Fact, Trait("Req", "REQ-038")]
    public async Task Marking_a_source_unreliable_affects_new_candidates()
    {
        var admin = await Radar.AdminAsync(app);
        var group = Radar.Token();
        var first = Radar.EventItem(Radar.Title(group));
        var ing = await Radar.IngestAsync(admin, [first]);
        Assert.DoesNotContain("SourceUnreliable", ing.Single.IssueCodes());

        ing.SourceBody["isUnreliable"] = true;
        var second = Radar.EventItem(Radar.Title(group, "Teatër"));
        await Radar.RefetchWithAsync(admin, ing.SourceId, ing.SourceBody, [first, second]);

        var newOne = (await Radar.CandidatesOfSourceAsync(admin, ing.SourceId)).Single(c => c.Str("externalId") == second["externalId"]!.GetValue<string>());
        AssertReviewWith(newOne, "SourceUnreliable");
    }

    // ---------- REQ-039 ----------

    [Theory, Trait("Req", "REQ-039")]
    [InlineData(25, true)]
    [InlineData(22, true)]
    [InlineData(18, true)]
    [InlineData(21, false)]
    [InlineData(19, false)]
    [InlineData(20, false)]
    public async Task Stated_discount_differing_by_more_than_one_point_is_ConflictingData(int stated, bool conflict)
    {
        var c = await IngestOneAsync(Deal(d => { d["oldPrice"] = 100m; d["newPrice"] = 80m; d["discountPercent"] = stated; }));
        Assert.Equal(stated, c.GetProperty("fields").Dec("statedDiscountPercent"));
        Assert.Equal(20m, c.GetProperty("fields").Dec("discountPercent"));
        if (conflict) AssertReviewWith(c, "ConflictingData");
        else
        {
            Assert.DoesNotContain("ConflictingData", c.IssueCodes());
            Assert.Equal("valid", c.Str("validation"));
        }
    }
}
