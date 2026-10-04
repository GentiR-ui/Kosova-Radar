using App.Domain.Text;

namespace App.Domain.Content;

/// <summary>Pure domain rules of design.md §4.</summary>
public static class ContentRules
{
    public static readonly TimeSpan DefaultEventDuration = TimeSpan.FromHours(6);
    public static readonly TimeSpan FreshnessWindow = TimeSpan.FromHours(48);
    public static readonly TimeSpan UndatedDealWindow = TimeSpan.FromDays(7);
    public const double DuplicateSimilarityThreshold = 0.8;

    /// <summary><c>EndsAt</c> when present and not before <c>StartsAt</c>, else start + 6 h (ADR-010).</summary>
    public static DateTimeOffset EffectiveEnd(DateTimeOffset startsAt, DateTimeOffset? endsAt) =>
        endsAt is { } end && end >= startsAt ? end : startsAt + DefaultEventDuration;

    public static bool IsActiveEvent(ItemStatus status, DateTimeOffset startsAt, DateTimeOffset? endsAt, DateTimeOffset now) =>
        status == ItemStatus.Published && EffectiveEnd(startsAt, endsAt) > now;

    public static bool IsActiveDeal(ItemStatus status, DateTimeOffset? validUntil, DateTimeOffset lastVerifiedAt, DateTimeOffset now) =>
        status == ItemStatus.Published
        && (validUntil is { } until ? until > now : lastVerifiedAt > now - UndatedDealWindow);

    /// <summary>Fresh when verified within the last 48 hours (inclusive).</summary>
    public static bool IsFresh(DateTimeOffset lastVerifiedAt, DateTimeOffset now) => lastVerifiedAt >= now - FreshnessWindow;

    /// <summary>round((old − new) / old × 100), midpoint away from zero; only when both prices exist and old &gt; new.</summary>
    public static int? DiscountPercent(decimal? oldPrice, decimal? newPrice)
    {
        if (oldPrice is not { } o || newPrice is not { } n || o <= n) return null;
        return (int)Math.Round((o - n) / o * 100m, MidpointRounding.AwayFromZero);
    }

    /// <summary>Jaccard similarity of the normalized word sets (words of 2+ characters); 0 when either set is empty.</summary>
    public static double TitleSimilarity(string? a, string? b)
    {
        var x = Words(a);
        var y = Words(b);
        if (x.Count == 0 || y.Count == 0) return 0;
        var intersection = x.Count(y.Contains);
        return intersection / (double)(x.Count + y.Count - intersection);
    }

    public static bool AreSimilarTitles(string? a, string? b) => TitleSimilarity(a, b) >= DuplicateSimilarityThreshold;

    private static HashSet<string> Words(string? text) =>
        TextNormalizer.Fold(text).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 2).ToHashSet(StringComparer.Ordinal);
}
