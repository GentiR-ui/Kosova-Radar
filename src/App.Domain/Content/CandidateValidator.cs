using App.Domain.Reference;

namespace App.Domain.Content;

/// <summary>Issue codes of design.md §4; PascalCase strings on the wire.</summary>
public static class IssueCodes
{
    public const string MissingTitle = "MissingTitle";
    public const string InvalidUrl = "InvalidUrl";
    public const string Expired = "Expired";
    public const string MissingStartsAt = "MissingStartsAt";
    public const string InvalidDateRange = "InvalidDateRange";
    public const string UnknownCity = "UnknownCity";
    public const string MissingPrice = "MissingPrice";
    public const string PriceInconsistent = "PriceInconsistent";
    public const string ConflictingData = "ConflictingData";
    public const string AiUnavailable = "AiUnavailable";
    public const string PossibleDuplicate = "PossibleDuplicate";
    public const string ChangedSinceApproval = "ChangedSinceApproval";
    public const string SourceUnreliable = "SourceUnreliable";
    public const string InvalidImageUrl = "InvalidImageUrl";
}

/// <summary>Result of validating a candidate: the fields (an invalid image URL is dropped), the issues and the outcome.</summary>
public sealed record ValidationResult(CandidateFields Fields, List<Issue> Issues, ValidationOutcome Outcome)
{
    /// <summary>A rejected outcome makes the candidate <c>Rejected</c> (without a reviewer); anything else waits for a person.</summary>
    public CandidateStatus Status => Outcome == ValidationOutcome.Rejected ? CandidateStatus.Rejected : CandidateStatus.Pending;

    /// <summary>The same result with issues from outside the rules (AI step, duplicates) added; the outcome follows.</summary>
    public ValidationResult WithIssues(IEnumerable<Issue> extra)
    {
        var issues = Issues.Concat(extra).ToList();
        return this with { Issues = issues, Outcome = CandidateValidator.Outcome(issues) };
    }
}

/// <summary>Validation rules REQ-027..039. Pure: <c>now</c> is the fetch (or edit) time, never the wall clock.</summary>
public static class CandidateValidator
{
    private const int StatedDiscountTolerance = 1;

    public static ValidationResult Validate(ItemKind kind, CandidateFields fields, DateTimeOffset now, bool sourceIsUnreliable)
    {
        var issues = new List<Issue>();

        if (string.IsNullOrWhiteSpace(fields.Title))
            issues.Add(new(IssueCodes.MissingTitle, IssueSeverity.Rejected, "title", "The item has no title."));
        if (!IsHttpUrl(fields.Url))
            issues.Add(new(IssueCodes.InvalidUrl, IssueSeverity.Rejected, "url", "The URL is missing or is not an absolute http(s) URL."));

        if (fields.ImageUrl is not null && !IsHttpUrl(fields.ImageUrl))
        {
            fields = fields with { ImageUrl = null };
            issues.Add(new(IssueCodes.InvalidImageUrl, IssueSeverity.Info, "imageUrl", "The image URL is not an absolute http(s) URL and was dropped."));
        }

        if (kind == ItemKind.Event) ValidateEvent(fields, now, issues);
        else ValidateDeal(fields, now, issues);

        if ((kind == ItemKind.Event || !string.IsNullOrWhiteSpace(fields.CityText)) && fields.CitySlug is null)
            issues.Add(new(IssueCodes.UnknownCity, IssueSeverity.Review, "city",
                "The city is missing or is not a Kosovo municipality."));

        if (sourceIsUnreliable)
            issues.Add(new(IssueCodes.SourceUnreliable, IssueSeverity.Review, null, "The source is marked unreliable."));

        return new(fields, issues, Outcome(issues));
    }

    public static ValidationOutcome Outcome(IReadOnlyCollection<Issue> issues) =>
        issues.Any(i => i.Severity == IssueSeverity.Rejected) ? ValidationOutcome.Rejected
        : issues.Any(i => i.Severity == IssueSeverity.Review) ? ValidationOutcome.NeedsReview
        : ValidationOutcome.Valid;

    public static bool IsHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    private static void ValidateEvent(CandidateFields f, DateTimeOffset now, List<Issue> issues)
    {
        if (f.EventStart() is not { } start)
        {
            issues.Add(new(IssueCodes.MissingStartsAt, IssueSeverity.Review, "startsAt", "The event has no start time."));
            return;
        }

        if (f.EndsAt is { } end && end < start)
            issues.Add(new(IssueCodes.InvalidDateRange, IssueSeverity.Review, "endsAt", "The event ends before it starts."));
        if (ContentRules.EffectiveEnd(start, f.EndsAt, f.StartsAt is not null) <= now)
            issues.Add(new(IssueCodes.Expired, IssueSeverity.Rejected, "startsAt", "The event has already ended."));
    }

    private static void ValidateDeal(CandidateFields f, DateTimeOffset now, List<Issue> issues)
    {
        if (f.ValidFrom is { } from && f.ValidUntil is { } until && until < from)
            issues.Add(new(IssueCodes.InvalidDateRange, IssueSeverity.Review, "validUntil", "The deal ends before it starts."));
        if (f.ValidUntil is { } expiry && expiry <= now)
            issues.Add(new(IssueCodes.Expired, IssueSeverity.Rejected, "validUntil", "The deal has already ended."));

        if (f.NewPrice is null && string.IsNullOrWhiteSpace(f.PriceText))
            issues.Add(new(IssueCodes.MissingPrice, IssueSeverity.Review, "newPrice", "The deal has neither a price nor a price text."));

        var negative = f.NewPrice < 0 || f.OldPrice < 0;
        var notReduced = f.OldPrice is { } o && f.NewPrice is { } n && o <= n;
        if (negative || notReduced)
            issues.Add(new(IssueCodes.PriceInconsistent, IssueSeverity.Review, "oldPrice",
                "The prices are inconsistent: a price is negative or the old price is not greater than the new price."));

        if (f.StatedDiscountPercent is { } stated && ContentRules.DiscountPercent(f.OldPrice, f.NewPrice) is { } computed
            && Math.Abs(stated - computed) > StatedDiscountTolerance)
            issues.Add(new(IssueCodes.ConflictingData, IssueSeverity.Review, "statedDiscountPercent",
                "The stated discount differs from the discount computed from the prices."));
    }
}
