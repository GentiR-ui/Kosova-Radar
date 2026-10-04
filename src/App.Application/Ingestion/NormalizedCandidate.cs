using App.Domain.Content;
using App.Domain.Reference;

namespace App.Application.Ingestion;

/// <summary>An item as a source adapter hands it to the ingestion run (design.md §2); never an entity.</summary>
/// <param name="SourceUrl">The item URL as fetched; empty when the item has only an <c>externalId</c>.</param>
/// <param name="Text">Unstructured text for AI extraction; not part of <see cref="CandidateFields"/>.</param>
/// <param name="Evidence">The item's raw source text exactly as received.</param>
public sealed record NormalizedCandidate(
    ItemKind Kind,
    string? ExternalId,
    string SourceUrl,
    string Fingerprint,
    string ContentHash,
    CandidateFields Fields,
    string? Text,
    string Evidence);

/// <summary>An item that could not be parsed; the rest of the feed is still processed (REQ-018).</summary>
public sealed record ItemFailure(int Index, string Reason);

/// <summary>Outcome of parsing a feed. <see cref="ErrorCode"/> is set only when the whole payload is unusable.</summary>
public sealed record FeedParseResult(
    int Received,
    IReadOnlyList<NormalizedCandidate> Items,
    IReadOnlyList<ItemFailure> Failures,
    string? ErrorCode = null)
{
    public static FeedParseResult Malformed { get; } = new(0, [], [], "MalformedPayload");
}
