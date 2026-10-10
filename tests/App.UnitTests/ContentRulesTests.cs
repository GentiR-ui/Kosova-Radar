using App.Domain.Content;

namespace App.UnitTests;

public class ContentRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Effective_end_uses_EndsAt_when_not_before_start()
    {
        var start = Now;
        Assert.Equal(start.AddHours(2), ContentRules.EffectiveEnd(start, start.AddHours(2)));
        Assert.Equal(start, ContentRules.EffectiveEnd(start, start));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    public void Effective_end_defaults_to_six_hours(int? endOffsetMinutes)
    {
        DateTimeOffset? end = endOffsetMinutes is { } m ? Now.AddMinutes(m) : null;
        Assert.Equal(Now.AddHours(6), ContentRules.EffectiveEnd(Now, end));
    }

    [Theory]
    [InlineData("2026-10-09T22:00:00Z", "2026-10-10T22:00:00Z")] // 00:00 local on 10 Oct (CEST) -> 00:00 local on 11 Oct
    [InlineData("2026-10-24T22:00:00Z", "2026-10-25T23:00:00Z")] // the day DST ends is 25 h long
    public void Date_only_event_ends_at_the_end_of_its_local_day(string start, string end)
    {
        var s = DateTimeOffset.Parse(start, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(DateTimeOffset.Parse(end, System.Globalization.CultureInfo.InvariantCulture), ContentRules.EffectiveEnd(s, null, false));
    }

    [Fact]
    public void Date_only_event_prefers_a_valid_EndsAt_and_stays_active_through_its_day()
    {
        var midnight = new DateTimeOffset(2026, 10, 9, 22, 0, 0, TimeSpan.Zero); // 00:00 local on 10 Oct
        Assert.Equal(midnight.AddHours(30), ContentRules.EffectiveEnd(midnight, midnight.AddHours(30), false));
        Assert.True(ContentRules.IsActiveEvent(ItemStatus.Published, midnight, null, midnight.AddHours(23), false));
        Assert.False(ContentRules.IsActiveEvent(ItemStatus.Published, midnight, null, midnight.AddHours(24), false));
        Assert.False(ContentRules.IsActiveEvent(ItemStatus.Published, midnight, null, midnight.AddHours(7), true)); // a timed one ends after 6 h
    }

    [Fact]
    public void Event_is_active_until_effective_end_exclusive()
    {
        var start = Now.AddHours(-6);
        Assert.False(ContentRules.IsActiveEvent(ItemStatus.Published, start, null, Now));
        Assert.True(ContentRules.IsActiveEvent(ItemStatus.Published, start, null, Now.AddSeconds(-1)));
        Assert.False(ContentRules.IsActiveEvent(ItemStatus.Withdrawn, Now, null, Now.AddHours(-1)));
        Assert.False(ContentRules.IsActiveEvent(ItemStatus.Expired, Now, null, Now.AddHours(-1)));
    }

    [Theory]
    [InlineData("2026-10-04T12:00:00Z", false)] // the fetch moment itself
    [InlineData("2026-10-07T21:59:59Z", false)] // 23:59:59 local on 7 Oct: the last second inside the window
    [InlineData("2026-10-07T22:00:00Z", true)] // 00:00 local on 8 Oct: the first moment beyond it
    [InlineData("2026-10-01T10:00:00Z", false)] // the past is never beyond the horizon
    public void The_horizon_is_counted_in_local_days_after_the_fetch_date(string startsAt, bool beyond)
    {
        // Fetched on 4 Oct (14:00 local) with a 3-day horizon: 4, 5, 6 and 7 Oct are inside.
        Assert.Equal(beyond, ContentRules.IsBeyondHorizon(DateTimeOffset.Parse(startsAt), Now, 3));
    }

    [Fact]
    public void Freshness_is_inclusive_at_exactly_48_hours()
    {
        Assert.True(ContentRules.IsFresh(Now.AddHours(-48), Now));
        Assert.False(ContentRules.IsFresh(Now.AddHours(-48).AddTicks(-1), Now));
        Assert.True(ContentRules.IsFresh(Now, Now));
    }

    [Fact]
    public void Deal_with_ValidUntil_is_active_until_it_passes()
    {
        Assert.True(ContentRules.IsActiveDeal(ItemStatus.Published, Now.AddTicks(1), Now.AddDays(-30), Now));
        Assert.False(ContentRules.IsActiveDeal(ItemStatus.Published, Now, Now, Now));
        Assert.False(ContentRules.IsActiveDeal(ItemStatus.Withdrawn, Now.AddDays(1), Now, Now));
    }

    [Fact]
    public void Undated_deal_is_active_for_seven_days_after_verification()
    {
        Assert.True(ContentRules.IsActiveDeal(ItemStatus.Published, null, Now.AddDays(-7).AddTicks(1), Now));
        Assert.False(ContentRules.IsActiveDeal(ItemStatus.Published, null, Now.AddDays(-7), Now));
    }

    [Theory]
    [InlineData(100, 50, 50)]
    [InlineData(3, 2, 33)] // 33.33
    [InlineData(200, 1, 100)] // 99.5 rounds away from zero
    [InlineData(8, 7, 13)] // 12.5 rounds away from zero
    public void Discount_rounds_half_away_from_zero(double oldPrice, double newPrice, int expected) =>
        Assert.Equal(expected, ContentRules.DiscountPercent((decimal)oldPrice, (decimal)newPrice));

    [Fact]
    public void Discount_is_null_without_a_real_reduction()
    {
        Assert.Null(ContentRules.DiscountPercent(10m, 10m));
        Assert.Null(ContentRules.DiscountPercent(10m, 12m));
        Assert.Null(ContentRules.DiscountPercent(null, 5m));
        Assert.Null(ContentRules.DiscountPercent(5m, null));
    }

    [Fact]
    public void Similarity_ignores_case_diacritics_punctuation_and_word_order()
    {
        Assert.Equal(1.0, ContentRules.TitleSimilarity("Koncert në Prishtinë!", "prishtine, KONCERT ne"));
    }

    [Fact]
    public void Similarity_threshold_is_inclusive_at_0_8()
    {
        // 4 shared of 5 distinct words = 0.8
        Assert.Equal(0.8, ContentRules.TitleSimilarity("alfa beta gama delta", "alfa beta gama delta epsilon"));
        Assert.True(ContentRules.AreSimilarTitles("alfa beta gama delta", "alfa beta gama delta epsilon"));
        // 3 of 5 = 0.6
        Assert.False(ContentRules.AreSimilarTitles("alfa beta gama", "alfa beta gama delta epsilon"));
    }

    [Theory]
    [InlineData(null, "abc")]
    [InlineData("", "abc")]
    [InlineData("a b", "x y")] // single characters are not words
    [InlineData("alfa", "beta")]
    public void Similarity_is_zero_when_nothing_comparable(string? a, string? b) =>
        Assert.Equal(0, ContentRules.TitleSimilarity(a, b));

    [Fact]
    public void Pristina_date_follows_local_midnight_in_winter_and_summer()
    {
        // CET (UTC+1): 23:30 UTC on 1 Jan is already 2 Jan locally; CEST (UTC+2): 22:00 UTC on 1 Jul is 2 Jul.
        Assert.Equal(new DateOnly(2027, 1, 2), ContentRules.PristinaDate(new(2027, 1, 1, 23, 30, 0, TimeSpan.Zero)));
        Assert.Equal(new DateOnly(2027, 1, 1), ContentRules.PristinaDate(new(2027, 1, 1, 22, 59, 0, TimeSpan.Zero)));
        Assert.Equal(new DateOnly(2027, 7, 2), ContentRules.PristinaDate(new(2027, 7, 1, 22, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Pristina_day_is_23_hours_long_on_the_spring_DST_change()
    {
        var (start, end) = ContentRules.PristinaDay(new DateOnly(2027, 3, 28));
        Assert.Equal(new DateTimeOffset(2027, 3, 27, 23, 0, 0, TimeSpan.Zero), start);
        Assert.Equal(TimeSpan.FromHours(23), end - start);
    }
}
