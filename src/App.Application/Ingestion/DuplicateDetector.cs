using App.Application.Abstractions;
using App.Domain.Content;
using App.Domain.Reference;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Ingestion;

/// <summary>A pending candidate that a new candidate may duplicate.</summary>
public sealed record PendingRef(Guid Id, Guid SourceId, ItemKind Kind, CandidateFields Fields);

/// <summary>The best match of the duplicate rules (design.md §4).</summary>
public sealed record DuplicateMatch(ItemKind Kind, Guid Id, bool IsCandidate);

/// <summary>
/// Duplicate rules of REQ-050: compared against published items of any source (except the item the candidate
/// itself updates) and pending candidates of other sources; same kind, same city, for events the same Europe/Pristina
/// start date, title similarity at least 0.8. The best similarity wins; published items win ties.
/// </summary>
public sealed class DuplicateDetector(IAppDbContext db)
{
    /// <summary>Pending candidates of all sources; load once per run and pass to <see cref="FindAsync"/>.</summary>
    public async Task<IReadOnlyList<PendingRef>> LoadPendingAsync(CancellationToken ct) =>
        await db.Candidates.AsNoTracking().Where(c => c.Status == CandidateStatus.Pending)
            .Select(c => new PendingRef(c.Id, c.SourceId, c.Kind, c.Fields)).ToListAsync(ct);

    public async Task<DuplicateMatch?> FindAsync(
        ItemKind kind, CandidateFields f, Guid candidateId, Guid sourceId, Guid? matchedItemId,
        IReadOnlyList<PendingRef> pending, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(f.Title)) return null;
        if (kind == ItemKind.Event && (f.CitySlug is null || f.StartsAt is null)) return null;

        DuplicateMatch? best = null;
        var bestScore = 0.0;
        void Consider(ItemKind k, Guid id, bool isCandidate, string? title)
        {
            var score = ContentRules.TitleSimilarity(f.Title, title);
            if (score >= ContentRules.DuplicateSimilarityThreshold && score > bestScore)
                (best, bestScore) = (new DuplicateMatch(k, id, isCandidate), score);
        }

        if (kind == ItemKind.Event)
        {
            var (from, to) = ContentRules.PristinaDay(ContentRules.PristinaDate(f.StartsAt!.Value));
            var events = await db.Events.AsNoTracking()
                .Where(e => e.Status == ItemStatus.Published && e.Id != matchedItemId && e.StartsAt >= from && e.StartsAt < to
                            && db.Cities.Any(c => c.Id == e.CityId && c.Slug == f.CitySlug))
                .OrderBy(e => e.Id).Select(e => new { e.Id, e.Title }).ToListAsync(ct);
            foreach (var e in events) Consider(ItemKind.Event, e.Id, false, e.Title);
        }
        else
        {
            var deals = await db.Deals.AsNoTracking()
                .Where(d => d.Status == ItemStatus.Published && d.Id != matchedItemId
                            && (f.CitySlug == null
                                ? d.CityId == null
                                : db.Cities.Any(c => c.Id == d.CityId && c.Slug == f.CitySlug)))
                .OrderBy(d => d.Id).Select(d => new { d.Id, d.Title }).ToListAsync(ct);
            foreach (var d in deals) Consider(ItemKind.Deal, d.Id, false, d.Title);
        }

        foreach (var p in pending.Where(p => p.Kind == kind && p.SourceId != sourceId && p.Id != candidateId).OrderBy(p => p.Id))
        {
            if (p.Fields.CitySlug != f.CitySlug) continue;
            if (kind == ItemKind.Event
                && (p.Fields.StartsAt is not { } start || ContentRules.PristinaDate(start) != ContentRules.PristinaDate(f.StartsAt!.Value)))
                continue;
            Consider(kind, p.Id, true, p.Fields.Title);
        }
        return best;
    }

    /// <summary>
    /// After the validation rules: <c>ChangedSinceApproval</c> for a candidate linked to a published item, then the
    /// duplicate check (skipped once the candidate is rejected). The outcome follows the added issues.
    /// </summary>
    public async Task<(ValidationResult Validation, DuplicateMatch? Duplicate)> ApplyAsync(
        ValidationResult validation, ItemKind kind, Guid candidateId, Guid sourceId, Guid? matchedItemId,
        IReadOnlyList<PendingRef> pending, CancellationToken ct)
    {
        if (matchedItemId is not null)
            validation = validation.WithIssues([new(IssueCodes.ChangedSinceApproval, IssueSeverity.Review, null,
                "The source changed this item after it was published; approving applies the changes to the published item.")]);
        if (validation.Outcome == ValidationOutcome.Rejected) return (validation, null);

        var match = await FindAsync(kind, validation.Fields, candidateId, sourceId, matchedItemId, pending, ct);
        if (match is not null)
            validation = validation.WithIssues([new(IssueCodes.PossibleDuplicate, IssueSeverity.Review, null,
                "Another item or pending candidate looks like the same item.")]);
        return (validation, match);
    }
}
