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

    [Fact]
    public void Event_is_active_until_effective_end_exclusive()
    {
        var start = Now.AddHours(-6);
        Assert.False(ContentRules.IsActiveEvent(ItemStatus.Published, start, null, Now));
        Assert.True(ContentRules.IsActiveEvent(ItemStatus.Published, start, null, Now.AddSeconds(-1)));
        Assert.False(ContentRules.IsActiveEvent(ItemStatus.Withdrawn, Now, null, Now.AddHours(-1)));
        Assert.False(ContentRules.IsActiveEvent(ItemStatus.Expired, Now, null, Now.AddHours(-1)));
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
}
