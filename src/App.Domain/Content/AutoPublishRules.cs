using App.Domain.Reference;

namespace App.Domain.Content;

/// <summary>
/// When a candidate of an auto-publish source goes public without a person (REQ-023, 024, 026, 044; ADR-039, ADR-042).
/// The ingestion validation has already run: this only decides whether its outcome is good enough.
/// </summary>
public static class AutoPublishRules
{
    /// <summary>A deal is "daily" when it ends at most this long after it starts (REQ-044).</summary>
    public static readonly TimeSpan DailyDealWindow = TimeSpan.FromHours(24);

    /// <summary>
    /// An event that runs longer than this (an <c>endsAt</c> far in the future would keep it listed for years) is left for a
    /// person; the 3-day window of ADR-042 bounds only its start.
    /// </summary>
    public static readonly TimeSpan MaxEventSpan = TimeSpan.FromDays(14);

    /// <summary>
    /// Fields the gates rest on. A value the AI read out of unreviewed page text must not decide that an item goes
    /// public without a person (prompt injection, hallucination); such a candidate waits for review.
    /// </summary>
    private static readonly string[] AiGatedFields =
        ["startsAt", "endsAt", "citySlug", "cityText", "oldPrice", "newPrice", "validFrom", "validUntil"];

    /// <summary>
    /// New candidate: validation <c>Valid</c>. Candidate linked to a published item: its only review issue is
    /// <c>ChangedSinceApproval</c>. Deals must also be daily with at least <paramref name="minDealDiscountPercent"/> off,
    /// events must start inside the horizon (a start the AI filled in after the horizon check stays for a person).
    /// </summary>
    public static bool CanPublish(Candidate c, int horizonDays, int minDealDiscountPercent)
    {
        if (c.Status != CandidateStatus.Pending || c.Validation == ValidationOutcome.Rejected) return false;
        if (!HasNoOtherBlockingIssue(c)) return false;
        if (c.AiSuggestedFields.Any(f => AiGatedFields.Contains(f))) return false;

        return c.Kind == ItemKind.Deal
            ? IsDailyDeal(c.Fields, c.FetchedAt) && ContentRules.DiscountPercent(c.Fields.OldPrice, c.Fields.NewPrice) >= minDealDiscountPercent
            : IsCurrentEvent(c.Fields, c.FetchedAt, horizonDays);
    }

    /// <summary>
    /// No <c>validUntil</c>: an online-shop offer that is valid while it is listed counts as today's deal (REQ-045). Otherwise
    /// <c>validUntil</c> at most 24 h after <c>validFrom</c> (or after the fetch when there is no <c>validFrom</c>), and the day
    /// starts no later than 24 h after the fetch: a far-future "daily" deal would otherwise be active at once (REQ-044).
    /// </summary>
    public static bool IsDailyDeal(CandidateFields f, DateTimeOffset fetchedAt) =>
        f.ValidUntil is not { } until
        || (until > fetchedAt
            && (f.ValidFrom ?? fetchedAt) <= fetchedAt + DailyDealWindow
            && until <= (f.ValidFrom ?? fetchedAt) + DailyDealWindow);

    /// <summary>Starts inside the horizon (REQ-026) and does not run longer than <see cref="MaxEventSpan"/>.</summary>
    private static bool IsCurrentEvent(CandidateFields f, DateTimeOffset fetchedAt, int horizonDays) =>
        f.EventStart() is not { } start
        || (!ContentRules.IsBeyondHorizon(start, fetchedAt, horizonDays)
            && ContentRules.EffectiveEnd(start, f.EndsAt, f.StartsAt is not null) <= start + MaxEventSpan);

    private static bool HasNoOtherBlockingIssue(Candidate c)
    {
        var blocking = c.Issues.Where(i => i.Severity != IssueSeverity.Info).ToList();
        if (c.MatchedItemId is null) return blocking.Count == 0 && c.Validation == ValidationOutcome.Valid;
        // A change of a published item always carries ChangedSinceApproval; anything else on top needs a person.
        return blocking.Count > 0 && blocking.All(i => i.Code == IssueCodes.ChangedSinceApproval);
    }
}
