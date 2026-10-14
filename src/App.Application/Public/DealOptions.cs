using App.Domain.Content;

namespace App.Application.Public;

/// <summary>Settings from the <c>Deals__*</c> environment variables (design.md §2).</summary>
public sealed class DealOptions
{
    public const string Section = "Deals";

    /// <summary>
    /// A listing deal (no <c>validUntil</c>, auto-publish source, discount at least <see cref="MinDiscountPercent"/>) stays
    /// active this many hours after the last successful fetch that listed it (REQ-045, ADR-045). Keep it above twice the
    /// source's poll interval (shops: 3 h), or the deals vanish between two fetches.
    /// </summary>
    public int ListingStaleHours { get; set; } = (int)ContentRules.DefaultListingStaleWindow.TotalHours;

    /// <summary>Copied from <c>AutoPublish__MinDealDiscountPercent</c> at start-up: "today's deal" needs this discount (REQ-045).</summary>
    public int MinDiscountPercent { get; set; } = 35;

    public TimeSpan ListingWindow => TimeSpan.FromHours(ListingStaleHours);

    /// <summary>
    /// REQ-045: an undated deal of an auto-publish source with enough off is today's deal. A person-approved deal with a
    /// small discount keeps the 7-day window of undated deals.
    /// </summary>
    public bool IsListingDeal(DateTimeOffset? validUntil, int? discountPercent, bool sourceAutoPublish) =>
        validUntil is null && sourceAutoPublish && discountPercent >= MinDiscountPercent;
}
