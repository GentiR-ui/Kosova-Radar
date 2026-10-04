using App.Domain.Reference;

namespace App.Domain.Content;

/// <summary>One source of a published item; a merge adds a row (design.md §4).</summary>
public sealed class ItemSource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public ItemKind ItemKind { get; set; }
    public Guid ItemId { get; set; }
    public Guid SourceId { get; set; }
    public required string SourceUrl { get; set; }
    public required string Fingerprint { get; set; }
    public Guid CandidateId { get; set; }
    public DateTimeOffset AddedAt { get; set; }
}
