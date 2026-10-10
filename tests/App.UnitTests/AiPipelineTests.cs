using App.Application.Ai;
using App.Application.Ingestion;
using App.Domain.Content;
using App.Domain.Reference;

namespace App.UnitTests;

public class AiPipelineTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 20, 20, 0, 0, TimeSpan.FromHours(2));

    private static readonly ReferenceResolver Resolver = new(
        new CityMatcher(
        [
            new City { Slug = "prizren", Name = "Prizren", Aliases = [] },
            new City { Slug = "peje", Name = "Pejë", Aliases = ["Peja"] },
        ]),
        [
            new Category { Slug = "muzike", Name = "Muzikë", Kind = ItemKind.Event },
            new Category { Slug = "teknologji", Name = "Teknologji", Kind = ItemKind.Deal },
        ]);

    private static AiMergeResult Merge(ItemKind kind, CandidateFields source, AiExtractedFields ai) =>
        AiFieldMerger.Merge(kind, source, ai, Resolver);

    // ---------- AiFieldMerger ----------

    [Fact]
    public void Null_fields_are_filled_and_listed_with_the_candidate_field_names()
    {
        var r = Merge(ItemKind.Event, new CandidateFields(), new AiExtractedFields
        {
            StartsAt = Start,
            EndsAt = Start.AddHours(2),
            City = "Peja",
            Venue = "Sheshi",
            PriceText = "10 €",
            PriceAmount = 10,
            Currency = "EUR",
        });

        Assert.Equal(Start, r.Fields.StartsAt);
        Assert.Equal("peje", r.Fields.CitySlug);
        Assert.Equal("Peja", r.Fields.CityText);
        Assert.Equal(10m, r.Fields.PriceAmount);
        Assert.Empty(r.Conflicts);
        Assert.Equal(
            ["cityText", "citySlug", "startsAt", "endsAt", "venueName", "priceText", "priceAmount", "currency"], r.Suggested);
    }

    [Fact]
    public void An_ai_time_on_the_source_date_replaces_the_date_only_state()
    {
        var source = new CandidateFields { CitySlug = "prizren", StartDate = new DateOnly(2026, 10, 20) };
        var r = Merge(ItemKind.Event, source, new AiExtractedFields { StartsAt = Start });

        Assert.Equal(Start, r.Fields.StartsAt);
        Assert.Null(r.Fields.StartDate);
        Assert.Contains("startsAt", r.Suggested);
        Assert.Empty(r.Conflicts);
    }

    [Fact]
    public void An_ai_time_on_another_date_than_the_source_is_a_conflict_and_the_date_stays()
    {
        var source = new CandidateFields { CitySlug = "prizren", StartDate = new DateOnly(2026, 10, 18) };
        var r = Merge(ItemKind.Event, source, new AiExtractedFields { StartsAt = Start });

        Assert.Null(r.Fields.StartsAt);
        Assert.Equal(new DateOnly(2026, 10, 18), r.Fields.StartDate);
        Assert.Equal("startDate", Assert.Single(r.Conflicts).Field);
    }

    [Fact]
    public void A_city_the_ai_names_but_nobody_knows_is_text_without_a_slug()
    {
        var r = Merge(ItemKind.Event, new CandidateFields(), new AiExtractedFields { City = "Atlantis" });

        Assert.Equal("Atlantis", r.Fields.CityText);
        Assert.Null(r.Fields.CitySlug);
        Assert.Equal(["cityText"], r.Suggested);
    }

    [Fact]
    public void Values_the_source_states_are_kept_and_the_same_instant_in_another_offset_is_no_conflict()
    {
        var source = new CandidateFields { StartsAt = Start, CityText = "Prizren", CitySlug = "prizren", PriceAmount = 12.5m, Currency = "EUR" };
        var r = Merge(ItemKind.Event, source, new AiExtractedFields
        {
            StartsAt = Start.ToOffset(TimeSpan.Zero),
            City = "prizren",
            PriceAmount = 12.50m,
            Currency = "EUR",
        });

        Assert.Equal(source, r.Fields);
        Assert.Empty(r.Suggested);
        Assert.Empty(r.Conflicts);
    }

    [Fact]
    public void A_different_ai_value_is_a_conflict_per_field_and_the_source_value_stays()
    {
        var source = new CandidateFields { StartsAt = Start, CityText = "Prizren", CitySlug = "prizren", PriceAmount = 10m };
        var r = Merge(ItemKind.Event, source, new AiExtractedFields
        {
            StartsAt = Start.AddHours(1),
            City = "Pejë",
            PriceAmount = 15m,
            Currency = "EUR",
        });

        Assert.Equal(source, r.Fields);
        Assert.Equal(["citySlug", "startsAt", "priceAmount"], r.Conflicts.Select(c => c.Field));
        Assert.All(r.Conflicts, c => Assert.Equal((IssueCodes.ConflictingData, IssueSeverity.Review), (c.Code, c.Severity)));
        Assert.Empty(r.Suggested); // not even the currency: the price is in doubt
    }

    [Fact]
    public void Free_text_values_are_never_compared()
    {
        var source = new CandidateFields { VenueName = "Sheshi", PriceText = "falas" };
        var r = Merge(ItemKind.Event, source, new AiExtractedFields { Venue = "Qendra", PriceText = "10 €" });

        Assert.Equal(source, r.Fields);
        Assert.Empty(r.Conflicts);
    }

    [Fact]
    public void A_deal_takes_deal_fields_and_ignores_event_fields()
    {
        var r = Merge(ItemKind.Deal, new CandidateFields { OldPrice = 100 }, new AiExtractedFields
        {
            NewPrice = 80,
            Merchant = "Shop",
            ValidUntil = Start,
            StartsAt = Start,
            PriceAmount = 80,
            Currency = "EUR",
        });

        Assert.Equal((100m, 80m, "Shop", Start), (r.Fields.OldPrice, r.Fields.NewPrice, r.Fields.MerchantName, r.Fields.ValidUntil));
        Assert.Null(r.Fields.StartsAt);
        Assert.Null(r.Fields.PriceAmount);
        Assert.Equal(["merchantName", "newPrice", "validUntil", "currency"], r.Suggested);
    }

    [Fact]
    public void A_deal_price_that_differs_from_the_source_is_a_conflict()
    {
        var r = Merge(ItemKind.Deal, new CandidateFields { NewPrice = 80 }, new AiExtractedFields { NewPrice = 79 });

        Assert.Equal(80m, r.Fields.NewPrice);
        Assert.Equal("newPrice", Assert.Single(r.Conflicts).Field);
    }

    // ---------- AiOutputValidator ----------

    private static AiExtractionResult Good() => new("m", "r", 0.5, new AiExtractedFields { StartsAt = Start, PriceAmount = 5, Currency = "EUR", City = "Prizren" }, "muzike");

    private static readonly string[] EventCategories = ["muzike", "sport"];

    [Fact]
    public void A_good_answer_passes() => Assert.Null(AiOutputValidator.Validate(Good(), EventCategories));

    [Theory]
    [InlineData("confidence")]
    [InlineData("nan")]
    [InlineData("category")]
    [InlineData("price")]
    [InlineData("tooExpensive")]
    [InlineData("currency")]
    [InlineData("year")]
    [InlineData("city")]
    [InlineData("control")]
    [InlineData("surrogate")]
    [InlineData("badModel")]
    [InlineData("badRequestId")]
    [InlineData("model")]
    public void An_answer_that_breaks_the_schema_or_a_domain_rule_is_rejected(string what)
    {
        var good = Good();
        var bad = what switch
        {
            "confidence" => good with { Confidence = 1.01 },
            "nan" => good with { Confidence = double.NaN },
            "category" => good with { CategorySlug = "teknologji" },
            "price" => good with { Fields = good.Fields with { PriceAmount = -0.01m } },
            "tooExpensive" => good with { Fields = good.Fields with { OldPrice = 1_000_001m } },
            "currency" => good with { Fields = good.Fields with { Currency = "eur" } },
            "year" => good with { Fields = good.Fields with { StartsAt = new DateTimeOffset(1800, 1, 1, 0, 0, 0, TimeSpan.Zero) } },
            "city" => good with { Fields = good.Fields with { City = new string('x', 301) } },
            "control" => good with { Fields = good.Fields with { Venue = "a\u0000b" } },
            "surrogate" => good with { Fields = good.Fields with { Merchant = "a\ud800b" } },
            "badModel" => good with { Model = "m\ud800" },
            "badRequestId" => good with { RequestId = "r\u0000" },
            _ => good with { Model = "" },
        };

        Assert.NotNull(AiOutputValidator.Validate(bad, EventCategories));
    }

    // ---------- ValidationResult.WithIssues ----------

    [Fact]
    public void Extra_issues_change_the_outcome_but_not_the_fields()
    {
        var valid = CandidateValidator.Validate(ItemKind.Deal,
            new CandidateFields { Title = "T", Url = "https://example.com/d", OldPrice = 10, NewPrice = 5 }, Start, false);
        Assert.Equal(ValidationOutcome.Valid, valid.Outcome);

        var flagged = valid.WithIssues([new(IssueCodes.AiUnavailable, IssueSeverity.Review, null, "x")]);

        Assert.Equal(ValidationOutcome.NeedsReview, flagged.Outcome);
        Assert.Equal(CandidateStatus.Pending, flagged.Status);
        Assert.Equal(valid.Fields, flagged.Fields);
        Assert.Single(flagged.Issues);
        Assert.Empty(valid.Issues);
    }
}
