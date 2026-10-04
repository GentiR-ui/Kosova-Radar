using App.Domain.Reference;

namespace App.Domain.Content;

/// <summary>A normalized item from a source waiting for (or past) review (design.md §4).</summary>
public sealed class Candidate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public ItemKind Kind { get; set; }
    public Guid SourceId { get; set; }
    public string? ExternalId { get; set; }

    /// <summary>As fetched, immutable; the editable URL is <c>Fields.Url</c>.</summary>
    public required string SourceUrl { get; set; }

    public required string Fingerprint { get; set; }
    public required string ContentHash { get; set; }
    public CandidateFields Fields { get; set; } = new();

    /// <summary>Raw source text exactly as received (at most 64 KB).</summary>
    public required string Evidence { get; set; }

    public CandidateStatus Status { get; set; }
    public ValidationOutcome Validation { get; set; }
    public List<Issue> Issues { get; set; } = [];
    public Guid? SuggestedCategoryId { get; set; }

    public ItemKind? DuplicateOfKind { get; set; }
    public Guid? DuplicateOfId { get; set; }
    public bool? DuplicateOfIsCandidate { get; set; }
    public Guid? MatchedItemId { get; set; }
    public Guid? PublishedItemId { get; set; }

    public string? RejectionReason { get; set; }

    /// <summary>Null on an auto-rejected candidate (still editable); a person's rejection is final.</summary>
    public Guid? RejectedByUserId { get; set; }

    public bool IsSynthetic { get; set; }

    public string? AiModel { get; set; }
    public string? AiRequestId { get; set; }
    public double? AiConfidence { get; set; }
    public DateTimeOffset? AiProcessedAt { get; set; }
    public List<string> AiSuggestedFields { get; set; } = [];

    public DateTimeOffset FetchedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>PostgreSQL <c>xmin</c>: concurrent edits fail instead of overwriting.</summary>
    public uint Version { get; set; }
}
