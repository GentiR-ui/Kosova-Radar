using App.Domain.Content;
using App.Domain.Reference;

namespace App.UnitTests;

public class CandidateValidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    private static CandidateFields GoodEvent() => new()
    {
        Title = "Koncert", Url = "https://example.com/e", CitySlug = "prishtine", CityText = "Prishtinë", StartsAt = Now.AddDays(1),
    };

    private static CandidateFields GoodDeal() => new()
    {
        Title = "Ofertë", Url = "https://example.com/d", OldPrice = 100, NewPrice = 80,
    };

    private static string[] Codes(ValidationResult r) => r.Issues.Select(i => i.Code).ToArray();

    [Fact]
    public void Good_event_and_deal_are_valid_and_pending()
    {
        foreach (var (kind, f) in new[] { (ItemKind.Event, GoodEvent()), (ItemKind.Deal, GoodDeal()) })
        {
            var r = CandidateValidator.Validate(kind, f, Now, false);
            Assert.Empty(r.Issues);
            Assert.Equal(ValidationOutcome.Valid, r.Outcome);
            Assert.Equal(CandidateStatus.Pending, r.Status);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Blank_title_is_rejected(string? title)
    {
        var r = CandidateValidator.Validate(ItemKind.Event, GoodEvent() with { Title = title }, Now, false);
        Assert.Contains(IssueCodes.MissingTitle, Codes(r));
        Assert.Equal(CandidateStatus.Rejected, r.Status);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("ftp://x.com/a", false)]
    [InlineData("/events/1", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("http://x.com", true)]
    [InlineData("https://x.com/a?b=1", true)]
    public void Url_must_be_absolute_http(string? url, bool ok)
    {
        var r = CandidateValidator.Validate(ItemKind.Event, GoodEvent() with { Url = url }, Now, false);
        Assert.Equal(ok, !Codes(r).Contains(IssueCodes.InvalidUrl));
    }

    [Fact]
    public void Invalid_image_url_is_dropped_and_only_informational()
    {
        var r = CandidateValidator.Validate(ItemKind.Event, GoodEvent() with { ImageUrl = "nope" }, Now, false);
        Assert.Null(r.Fields.ImageUrl);
        Assert.Equal(IssueSeverity.Info, r.Issues.Single().Severity);
        Assert.Equal(ValidationOutcome.Valid, r.Outcome);
    }

    [Fact]
    public void Event_expiry_uses_effective_end_with_exclusive_boundary()
    {
        // Started 6 h ago without end: effective end == now, already over.
        var over = CandidateValidator.Validate(ItemKind.Event, GoodEvent() with { StartsAt = Now.AddHours(-6) }, Now, false);
        Assert.Contains(IssueCodes.Expired, Codes(over));
        var running = CandidateValidator.Validate(ItemKind.Event, GoodEvent() with { StartsAt = Now.AddHours(-6).AddSeconds(1) }, Now, false);
        Assert.DoesNotContain(IssueCodes.Expired, Codes(running));
        var longRunning = CandidateValidator.Validate(ItemKind.Event, GoodEvent() with { StartsAt = Now.AddHours(-10), EndsAt = Now.AddHours(2) }, Now, false);
        Assert.DoesNotContain(IssueCodes.Expired, Codes(longRunning));
    }

    [Fact]
    public void Event_without_start_needs_review_and_is_not_expired()
    {
        var r = CandidateValidator.Validate(ItemKind.Event, GoodEvent() with { StartsAt = null }, Now, false);
        Assert.Equal([IssueCodes.MissingStartsAt], Codes(r));
        Assert.Equal(ValidationOutcome.NeedsReview, r.Outcome);
    }

    [Fact]
    public void Event_date_range_end_before_start_is_invalid_but_equal_is_fine()
    {
        var start = Now.AddDays(1);
        var bad = CandidateValidator.Validate(ItemKind.Event, GoodEvent() with { StartsAt = start, EndsAt = start.AddHours(-1) }, Now, false);
        Assert.Equal([IssueCodes.InvalidDateRange], Codes(bad));
        var equal = CandidateValidator.Validate(ItemKind.Event, GoodEvent() with { StartsAt = start, EndsAt = start }, Now, false);
        Assert.Empty(equal.Issues);
    }

    [Fact]
    public void Deal_date_range_and_expiry()
    {
        var bad = CandidateValidator.Validate(ItemKind.Deal, GoodDeal() with { ValidFrom = Now.AddHours(12), ValidUntil = Now.AddHours(8) }, Now, false);
        Assert.Equal([IssueCodes.InvalidDateRange], Codes(bad));
        var expired = CandidateValidator.Validate(ItemKind.Deal, GoodDeal() with { ValidUntil = Now }, Now, false);
        Assert.Contains(IssueCodes.Expired, Codes(expired));
        Assert.Equal(ValidationOutcome.Rejected, expired.Outcome);
        var open = CandidateValidator.Validate(ItemKind.Deal, GoodDeal() with { ValidUntil = Now.AddSeconds(1) }, Now, false);
        Assert.Empty(open.Issues);
    }

    [Fact]
    public void City_rules_differ_by_kind()
    {
        Assert.Contains(IssueCodes.UnknownCity,
            Codes(CandidateValidator.Validate(ItemKind.Event, GoodEvent() with { CityText = null, CitySlug = null }, Now, false)));
        Assert.Contains(IssueCodes.UnknownCity,
            Codes(CandidateValidator.Validate(ItemKind.Deal, GoodDeal() with { CityText = "Tirana" }, Now, false)));
        Assert.DoesNotContain(IssueCodes.UnknownCity,
            Codes(CandidateValidator.Validate(ItemKind.Deal, GoodDeal(), Now, false)));
        Assert.DoesNotContain(IssueCodes.UnknownCity,
            Codes(CandidateValidator.Validate(ItemKind.Deal, GoodDeal() with { CityText = "Prishtina", CitySlug = "prishtine" }, Now, false)));
    }

    [Theory]
    [InlineData(null, null, null, true)]
    [InlineData(null, null, "Falas", false)]
    [InlineData(null, 10, null, false)]
    public void Missing_price(int? old, int? @new, string? text, bool missing)
    {
        var f = GoodDeal() with { OldPrice = old, NewPrice = @new, PriceText = text };
        Assert.Equal(missing, Codes(CandidateValidator.Validate(ItemKind.Deal, f, Now, false)).Contains(IssueCodes.MissingPrice));
    }

    [Theory]
    [InlineData(null, -5, true)]
    [InlineData(50, 50, true)]
    [InlineData(40, 50, true)]
    [InlineData(-10, 5, true)]
    [InlineData(-1, null, true)]
    [InlineData(51, 50, false)]
    [InlineData(10, 0, false)]
    [InlineData(null, 0, false)]
    public void Price_consistency(int? old, int? @new, bool inconsistent)
    {
        var f = GoodDeal() with { OldPrice = old, NewPrice = @new, PriceText = "x" };
        Assert.Equal(inconsistent, Codes(CandidateValidator.Validate(ItemKind.Deal, f, Now, false)).Contains(IssueCodes.PriceInconsistent));
    }

    [Theory]
    [InlineData(25, true)]
    [InlineData(22, true)]
    [InlineData(18, true)]
    [InlineData(21, false)]
    [InlineData(19, false)]
    [InlineData(20, false)]
    public void Stated_discount_tolerance_is_one_point(int stated, bool conflict)
    {
        var f = GoodDeal() with { StatedDiscountPercent = stated };
        Assert.Equal(conflict, Codes(CandidateValidator.Validate(ItemKind.Deal, f, Now, false)).Contains(IssueCodes.ConflictingData));
    }

    [Fact]
    public void Stated_discount_without_computable_discount_is_not_a_conflict()
    {
        var f = GoodDeal() with { OldPrice = null, StatedDiscountPercent = 50 };
        Assert.DoesNotContain(IssueCodes.ConflictingData, Codes(CandidateValidator.Validate(ItemKind.Deal, f, Now, false)));
    }

    [Fact]
    public void Unreliable_source_needs_review_and_rejected_wins_over_review()
    {
        var review = CandidateValidator.Validate(ItemKind.Event, GoodEvent(), Now, true);
        Assert.Equal([IssueCodes.SourceUnreliable], Codes(review));
        Assert.Equal(ValidationOutcome.NeedsReview, review.Outcome);

        var both = CandidateValidator.Validate(ItemKind.Event, GoodEvent() with { Title = null, StartsAt = null }, Now, true);
        Assert.Equal(ValidationOutcome.Rejected, both.Outcome);
        Assert.Contains(IssueCodes.MissingStartsAt, Codes(both));
    }
}
