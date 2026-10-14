using App.Domain.Sources;

namespace App.Domain.Content;

public sealed class Deal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Title { get; set; }
    public string? Description { get; set; }
    public string? MerchantName { get; set; }
    public Guid? CityId { get; set; }
    public Guid? CategoryId { get; set; }
    public decimal? OldPrice { get; set; }
    public decimal? NewPrice { get; set; }
    public string? PriceText { get; set; }
    public string? Currency { get; set; }
    public int? DiscountPercent { get; set; }
    public DateTimeOffset? ValidFrom { get; set; }
    public DateTimeOffset? ValidUntil { get; set; }
    public string? ImageUrl { get; set; }
    public ItemStatus Status { get; set; }
    public Guid PrimarySourceId { get; set; }
    public required string SourceUrl { get; set; }
    public required string Fingerprint { get; set; }
    public bool IsSynthetic { get; set; }
    public string SearchText { get; set; } = "";
    public DateTimeOffset LastVerifiedAt { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public uint Version { get; set; }

    /// <summary>Not loaded by default; set by queries that need the source's settings (<c>AutoPublish</c>, REQ-045).</summary>
    public Source? PrimarySource { get; set; }

    public bool IsActive(DateTimeOffset now, bool isListingDeal = false, TimeSpan? listingWindow = null) =>
        ContentRules.IsActiveDeal(Status, ValidUntil, LastVerifiedAt, now, isListingDeal, listingWindow);
}
