using App.Domain.Content;
using App.Domain.Reference;

namespace App.UnitTests;

/// <summary>REQ-023, 024, 026, 044 (ADR-039, ADR-042): what an auto-publish source may publish without a person.</summary>
public class AutoPublishRulesTests
{
    private static readonly DateTimeOffset Fetched = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);
    private const int Horizon = 3;
    private const int MinDiscount = 35;

    private static Candidate Event(Action<Candidate>? change = null, CandidateFields? fields = null)
    {
        var c = new Candidate
        {
            Kind = ItemKind.Event,
            SourceUrl = "https://example.com/e",
            Fingerprint = "f",
            ContentHash = "h",
            Evidence = "e",
            Status = CandidateStatus.Pending,
            Validation = ValidationOutcome.Valid,
            FetchedAt = Fetched,
            Fields = fields ?? new CandidateFields { Title = "Koncert", StartsAt = Fetched.AddDays(1) },
        };
        change?.Invoke(c);
        return c;
    }

    private static Candidate Deal(decimal? oldPrice, decimal? newPrice, DateTimeOffset? validFrom, DateTimeOffset? validUntil) =>
        Event(c => c.Kind = ItemKind.Deal, new CandidateFields
        {
            Title = "Ofertë",
            OldPrice = oldPrice,
            NewPrice = newPrice,
            ValidFrom = validFrom,
            ValidUntil = validUntil,
        });

    private static bool Can(Candidate c) => AutoPublishRules.CanPublish(c, Horizon, MinDiscount);

    [Fact]
    public void A_new_valid_event_is_published()
    {
        Assert.True(Can(Event()));
    }

    [Theory]
    [InlineData(ValidationOutcome.NeedsReview)]
    [InlineData(ValidationOutcome.Rejected)]
    public void A_candidate_that_needs_review_or_is_rejected_is_not_published(ValidationOutcome outcome)
    {
        Assert.False(Can(Event(c => c.Validation = outcome)));
    }

    [Theory]
    [InlineData(CandidateStatus.Rejected)]
    [InlineData(CandidateStatus.Published)]
    [InlineData(CandidateStatus.Merged)]
    public void Only_a_pending_candidate_is_published(CandidateStatus status)
    {
        Assert.False(Can(Event(c => c.Status = status)));
    }

    [Fact]
    public void An_info_issue_does_not_block_publishing()
    {
        var c = Event(c => c.Issues = [new(IssueCodes.InvalidImageUrl, IssueSeverity.Info, "imageUrl", "Image dropped.")]);
        Assert.True(Can(c));
    }

    [Fact]
    public void A_change_of_a_published_item_with_only_ChangedSinceApproval_is_published()
    {
        var c = Event(c =>
        {
            c.Validation = ValidationOutcome.NeedsReview;
            c.MatchedItemId = Guid.NewGuid();
            c.Issues = [new(IssueCodes.ChangedSinceApproval, IssueSeverity.Review, null, "Changed.")];
        });
        Assert.True(Can(c));
    }

    [Theory]
    [InlineData(IssueCodes.PossibleDuplicate)]
    [InlineData(IssueCodes.UnknownCity)]
    [InlineData(IssueCodes.AiUnavailable)]
    public void A_change_with_another_review_issue_stays_for_a_person(string other)
    {
        var c = Event(c =>
        {
            c.Validation = ValidationOutcome.NeedsReview;
            c.MatchedItemId = Guid.NewGuid();
            c.Issues =
            [
                new(IssueCodes.ChangedSinceApproval, IssueSeverity.Review, null, "Changed."),
                new(other, IssueSeverity.Review, null, "Other."),
            ];
        });
        Assert.False(Can(c));
    }

    [Fact]
    public void A_linked_candidate_without_the_ChangedSinceApproval_issue_is_not_published()
    {
        Assert.False(Can(Event(c => c.MatchedItemId = Guid.NewGuid())));
    }

    [Fact]
    public void An_event_starting_after_the_horizon_is_left_for_a_person()
    {
        // Fetched 10 Oct 11:00 local: 13 Oct is the last day inside.
        Assert.True(Can(Event(fields: new CandidateFields { Title = "A", StartsAt = new DateTimeOffset(2026, 10, 13, 21, 0, 0, TimeSpan.FromHours(2)) })));
        Assert.False(Can(Event(fields: new CandidateFields { Title = "A", StartsAt = new DateTimeOffset(2026, 10, 14, 0, 30, 0, TimeSpan.FromHours(2)) })));
    }

    [Fact]
    public void A_daily_deal_with_35_percent_off_is_published()
    {
        Assert.True(Can(Deal(100m, 65m, null, Fetched.AddHours(12))));
    }

    [Fact]
    public void A_daily_deal_with_a_smaller_discount_is_not_published()
    {
        Assert.False(Can(Deal(100m, 66m, null, Fetched.AddHours(12))));
    }

    [Fact]
    public void A_big_discount_on_a_longer_deal_is_not_published()
    {
        Assert.False(Can(Deal(100m, 50m, null, Fetched.AddHours(24).AddMinutes(1))));
        Assert.False(Can(Deal(100m, 50m, Fetched, Fetched.AddDays(10))));
    }

    [Fact]
    public void The_day_is_counted_from_validFrom_when_there_is_one()
    {
        var from = Fetched.AddHours(-10);
        Assert.True(Can(Deal(100m, 50m, from, from.AddHours(24))));
        Assert.False(Can(Deal(100m, 50m, from, from.AddHours(24).AddSeconds(1))));
    }

    [Fact]
    public void A_deal_without_prices_or_with_a_daily_deal_missing_its_discount_is_not_published()
    {
        Assert.False(Can(Deal(null, 50m, null, Fetched.AddHours(5))));
        Assert.False(Can(Deal(null, null, null, null)));
    }

    [Theory]
    [InlineData(100, 65, true)] // 35 %
    [InlineData(100, 66, false)] // 34 %
    public void An_undated_deal_counts_as_todays_deal_and_needs_the_minimum_discount(int oldPrice, int newPrice, bool expected) =>
        Assert.Equal(expected, Can(Deal(oldPrice, newPrice, null, null)));

    [Theory]
    [InlineData("startsAt")]
    [InlineData("citySlug")]
    [InlineData("oldPrice")]
    [InlineData("newPrice")]
    [InlineData("validUntil")]
    public void A_gate_field_filled_by_the_ai_leaves_the_candidate_for_a_person(string field)
    {
        Assert.False(Can(Event(c => c.AiSuggestedFields = [field])));
    }

    [Fact]
    public void An_ai_filled_field_the_gates_do_not_rest_on_does_not_block_publishing()
    {
        Assert.True(Can(Event(c => c.AiSuggestedFields = ["venueName", "priceText"])));
    }

    [Fact]
    public void A_far_future_daily_deal_is_not_published()
    {
        var from = Fetched.AddYears(100);
        Assert.False(Can(Deal(100m, 10m, from, from.AddHours(12))));
    }

    [Fact]
    public void A_deal_that_has_already_ended_is_not_published()
    {
        Assert.False(Can(Deal(100m, 10m, null, Fetched.AddMinutes(-1))));
    }

    [Fact]
    public void An_event_that_starts_soon_but_runs_for_years_is_left_for_a_person()
    {
        var fields = new CandidateFields { Title = "A", StartsAt = Fetched.AddDays(1), EndsAt = Fetched.AddYears(70) };
        Assert.False(Can(Event(fields: fields)));
        Assert.True(Can(Event(fields: fields with { EndsAt = Fetched.AddDays(1).AddDays(13) })));
        Assert.False(Can(Event(fields: fields with { EndsAt = Fetched.AddDays(1).AddDays(15) })));
    }

    [Theory]
    [InlineData("https://prishtina.com/events", "https://prishtina.com/events/jazz-1", true)]
    [InlineData("https://prishtina.com/events", "https://www.prishtina.com/events/jazz-1", true)]
    [InlineData("https://www.prishtina.com/events", "https://prishtina.com/x", true)]
    [InlineData("https://prishtina.com/events", "https://tickets.prishtina.com/x", true)]
    [InlineData("https://api.partner.com/feed.json", "https://partner.com/e/1", true)]
    [InlineData("https://prishtina.com/events", "https://evil.example/events/1", false)]
    [InlineData("https://prishtina.com/events", "https://prishtina.com.evil.example/x", false)]
    [InlineData("https://prishtina.com/events", "https://notprishtina.com/x", false)]
    [InlineData("https://prishtina.com/events", "https://prishtina.com@evil.example/x", false)]
    [InlineData("https://prishtina.com/events", "not a url", false)]
    public void The_item_link_must_be_on_the_sources_own_site(string baseUrl, string url, bool expected)
    {
        var source = new App.Domain.Sources.Source { Name = "S", BaseUrl = baseUrl };
        Assert.Equal(expected, App.Application.Ingestion.AutoPublisher.IsOnSourceHost(source, url));
    }

    [Fact]
    public void A_source_without_a_location_has_nothing_to_compare_with()
    {
        var source = new App.Domain.Sources.Source { Name = "Fixture", BaseUrl = null };
        Assert.True(App.Application.Ingestion.AutoPublisher.IsOnSourceHost(source, "https://anything.example/x"));
    }
}
