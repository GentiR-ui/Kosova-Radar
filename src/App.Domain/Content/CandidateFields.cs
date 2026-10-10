using System.Text.Json.Serialization;

namespace App.Domain.Content;

/// <summary>Normalized fields of a candidate (design.md §5); null means unknown. Stored as jsonb.</summary>
public sealed record CandidateFields
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? Url { get; init; }
    public string? ImageUrl { get; init; }
    public string? CityText { get; init; }
    public string? CitySlug { get; init; }
    public string? VenueName { get; init; }
    public string? CategoryText { get; init; }
    public string? CategorySlug { get; init; }
    public DateTimeOffset? StartsAt { get; init; }

    /// <summary>Local (Europe/Pristina) start date of an event whose time is unknown; only set while <see cref="StartsAt"/> is null (ADR-040).
    /// Omitted from JSON when null so that the content hash of existing items does not change.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateOnly? StartDate { get; init; }

    public DateTimeOffset? EndsAt { get; init; }
    public string? PriceText { get; init; }
    public decimal? PriceAmount { get; init; }
    public string? Currency { get; init; }
    public string? MerchantName { get; init; }
    public decimal? OldPrice { get; init; }
    public decimal? NewPrice { get; init; }
    public int? StatedDiscountPercent { get; init; }
    public DateTimeOffset? ValidFrom { get; init; }
    public DateTimeOffset? ValidUntil { get; init; }
}

public static class CandidateFieldsExtensions
{
    /// <summary>The start instant: <c>StartsAt</c>, else local 00:00 of <c>StartDate</c> (ADR-040).</summary>
    public static DateTimeOffset? EventStart(this CandidateFields f) =>
        f.StartsAt ?? (f.StartDate is { } d ? ContentRules.PristinaDay(d).Start : null);
}

public enum IssueSeverity { Info, Review, Rejected }

/// <summary>One validation finding; <see cref="Code"/> is a PascalCase string on the wire (design.md §4).</summary>
public sealed record Issue(string Code, IssueSeverity Severity, string? Field, string Message);
