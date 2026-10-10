namespace App.Domain.Content;

public sealed class Event
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Title { get; set; }
    public string? Description { get; set; }
    public Guid? VenueId { get; set; }
    public Guid CityId { get; set; }
    public Guid? CategoryId { get; set; }
    public DateTimeOffset StartsAt { get; set; }

    /// <summary>False = only the date is known; <see cref="StartsAt"/> is local 00:00 of that date (ADR-040).</summary>
    public bool StartTimeKnown { get; set; } = true;

    public DateTimeOffset? EndsAt { get; set; }
    public string? PriceText { get; set; }
    public decimal? PriceAmount { get; set; }
    public string? Currency { get; set; }
    public string? ImageUrl { get; set; }
    public ItemStatus Status { get; set; }
    public Guid PrimarySourceId { get; set; }
    public required string SourceUrl { get; set; }
    public required string Fingerprint { get; set; }
    public bool IsSynthetic { get; set; }

    /// <summary>Normalized title + description + venue name (<see cref="Text.TextNormalizer"/>).</summary>
    public string SearchText { get; set; } = "";

    public DateTimeOffset LastVerifiedAt { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public uint Version { get; set; }

    public DateTimeOffset EffectiveEnd => ContentRules.EffectiveEnd(StartsAt, EndsAt, StartTimeKnown);
    public bool IsActive(DateTimeOffset now) => ContentRules.IsActiveEvent(Status, StartsAt, EndsAt, now, StartTimeKnown);
}
