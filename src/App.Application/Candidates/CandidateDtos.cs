using App.Application.Reference;
using App.Domain.Content;
using App.Domain.Reference;

namespace App.Application.Candidates;

/// <summary>design.md §5. Severity is a camelCase enum string; the code stays PascalCase.</summary>
public sealed record IssueDto(string Code, IssueSeverity Severity, string? Field, string Message)
{
    public static IssueDto From(Issue i) => new(i.Code, i.Severity, i.Field, i.Message);
}

public sealed record CandidateFieldsDto(
    string? Title, string? Description, string? Url, string? ImageUrl, string? CityText, string? CitySlug,
    string? VenueName, string? CategoryText, string? CategorySlug, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt,
    string? PriceText, decimal? PriceAmount, string? Currency, string? MerchantName, decimal? OldPrice, decimal? NewPrice,
    int? StatedDiscountPercent, int? DiscountPercent, DateTimeOffset? ValidFrom, DateTimeOffset? ValidUntil,
    DateOnly? StartDate = null)
{
    public static CandidateFieldsDto From(CandidateFields f) =>
        new(f.Title, f.Description, f.Url, f.ImageUrl, f.CityText, f.CitySlug, f.VenueName, f.CategoryText, f.CategorySlug,
            f.StartsAt, f.EndsAt, f.PriceText, f.PriceAmount, f.Currency, f.MerchantName, f.OldPrice, f.NewPrice,
            f.StatedDiscountPercent, ContentRules.DiscountPercent(f.OldPrice, f.NewPrice), f.ValidFrom, f.ValidUntil, f.StartDate);
}

public sealed record CandidateSummaryDto(
    Guid Id, ItemKind Kind, string? Title, CandidateStatus Status, ValidationOutcome Validation,
    IReadOnlyList<IssueDto> Issues, Guid SourceId, string SourceName, DateTimeOffset FetchedAt, bool IsSynthetic);

public sealed record SourceOptionDto(Guid Id, string Name);

public sealed record CandidateAiDto(
    string Model, string? RequestId, double? Confidence, DateTimeOffset? ProcessedAt, IReadOnlyList<string> SuggestedFields);

public sealed record DuplicateOfDto(ItemKind Kind, Guid Id, string? Title, bool IsCandidate);

public sealed record CandidateDto(
    Guid Id, ItemKind Kind, string? Title, CandidateStatus Status, ValidationOutcome Validation,
    IReadOnlyList<IssueDto> Issues, Guid SourceId, string SourceName, DateTimeOffset FetchedAt, bool IsSynthetic,
    string? ExternalId, string SourceUrl, CandidateFieldsDto Fields, string Evidence, CategoryDto? SuggestedCategory,
    CandidateAiDto? Ai, DuplicateOfDto? DuplicateOf, Guid? MatchedItemId, Guid? PublishedItemId, string? RejectionReason);

/// <summary>Validated list filters (see <see cref="CandidateQueries.ParseFilter"/>).</summary>
public sealed record CandidateFilter(
    CandidateStatus? Status, ValidationOutcome? Validation, ItemKind? Kind, Guid? SourceId);
