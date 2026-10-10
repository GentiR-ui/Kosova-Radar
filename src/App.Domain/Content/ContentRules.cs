using App.Domain.Text;

namespace App.Domain.Content;

/// <summary>Pure domain rules of design.md §4.</summary>
public static class ContentRules
{
    public static readonly TimeSpan DefaultEventDuration = TimeSpan.FromHours(6);
    public static readonly TimeSpan FreshnessWindow = TimeSpan.FromHours(48);
    public static readonly TimeSpan UndatedDealWindow = TimeSpan.FromDays(7);
    public const double DuplicateSimilarityThreshold = 0.8;

    /// <summary>"Europe/Pristina" = IANA <c>Europe/Belgrade</c> (design.md §4).</summary>
    private static readonly TimeZoneInfo Pristina = TimeZoneInfo.FindSystemTimeZoneById("Europe/Belgrade");

    /// <summary>The calendar date of an instant in Europe/Pristina.</summary>
    public static DateOnly PristinaDate(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Pristina).DateTime);

    /// <summary>[midnight, next midnight) of a Europe/Pristina date as UTC instants (DST-safe).</summary>
    public static (DateTimeOffset Start, DateTimeOffset End) PristinaDay(DateOnly date) =>
        (Midnight(date), Midnight(date.AddDays(1)));

    private static DateTimeOffset Midnight(DateOnly date) =>
        new(TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue), Pristina), TimeSpan.Zero);

    /// <summary>
    /// REQ-026: the start date (Europe/Pristina) is more than <paramref name="days"/> days after the fetch date. Dates, not
    /// instants: everything today and on the next <paramref name="days"/> days is inside the window.
    /// </summary>
    public static bool IsBeyondHorizon(DateTimeOffset startsAt, DateTimeOffset fetchedAt, int days) =>
        PristinaDate(startsAt) > PristinaDate(fetchedAt).AddDays(days);

    /// <summary>
    /// <c>EndsAt</c> when present and not before <c>StartsAt</c>, else start + 6 h (ADR-010); a date-only event
    /// (<paramref name="startTimeKnown"/> false) lasts until the end of its local day (ADR-040).
    /// </summary>
    public static DateTimeOffset EffectiveEnd(DateTimeOffset startsAt, DateTimeOffset? endsAt, bool startTimeKnown = true) =>
        endsAt is { } end && end >= startsAt ? end
        : startTimeKnown ? startsAt + DefaultEventDuration
        : PristinaDay(PristinaDate(startsAt)).End;

    public static bool IsActiveEvent(ItemStatus status, DateTimeOffset startsAt, DateTimeOffset? endsAt, DateTimeOffset now,
        bool startTimeKnown = true) =>
        status == ItemStatus.Published && EffectiveEnd(startsAt, endsAt, startTimeKnown) > now;

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
