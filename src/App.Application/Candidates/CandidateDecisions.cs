using App.Application.Abstractions;
using App.Application.Audit;
using App.Application.Ingestion;
using App.Domain.Content;
using App.Domain.Reference;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Candidates;

public enum DecisionStatus { Done, Invalid, NotFound, Conflict }

public sealed record DecisionResult(DecisionStatus Status, CandidateDto? Candidate = null, IReadOnlyDictionary<string, string[]>? Errors = null);

/// <summary>Reviewer decisions on a candidate (REQ-064, 065, 066, 067, 074, 075). Each change and its audit entry commit together.</summary>
public sealed class CandidateDecisions(IAppDbContext db, IAuditWriter audit, ICurrentUser user, IClock clock, DuplicateDetector duplicates)
{
    public async Task<DecisionResult> EditAsync(Guid id, CandidateFieldsRequest? body, CancellationToken ct)
    {
        var c = await db.Candidates.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return new(DecisionStatus.NotFound);
        if (!IsOpen(c)) return new(DecisionStatus.Conflict);

        var (req, errors) = CandidateRequestValidator.Validate(body);
        if (req is null) return new(DecisionStatus.Invalid, Errors: errors);
        await CheckReferencesAsync(c.Kind, req, errors, ct);
        if (errors.Count > 0) return new(DecisionStatus.Invalid, Errors: errors);

        var unreliable = await db.Sources.Where(s => s.Id == c.SourceId).Select(s => s.IsUnreliable).SingleAsync(ct);
        var before = new { fields = CandidateFieldsDto.From(c.Fields), status = c.Status, validation = c.Validation };
        var validation = CandidateValidator.Validate(c.Kind, CandidateRequestValidator.Apply(c.Kind, c.Fields, req), clock.UtcNow, unreliable);

        // The link to a published item and the duplicate check are part of the issues, so an edit must not drop them.
        var (linked, duplicate) = await duplicates.ApplyAsync(validation, c.Kind, c.Id, c.SourceId, c.MatchedItemId,
            await duplicates.LoadPendingAsync(ct), ct);
        validation = linked;
        c.DuplicateOfKind = duplicate?.Kind;
        c.DuplicateOfId = duplicate?.Id;
        c.DuplicateOfIsCandidate = duplicate?.IsCandidate;

        // Only the rules decide the status: an auto-rejected candidate may become pending again.
        c.Fields = validation.Fields;
        c.Validation = validation.Outcome;
        c.Issues = validation.Issues;
        c.Status = validation.Status;
        c.UpdatedAt = clock.UtcNow;
        audit.Record(AuditActions.CandidateEdit, AuditEntityTypes.Candidate, c.Id, before,
            new { fields = CandidateFieldsDto.From(c.Fields), status = c.Status, validation = c.Validation });
        return await SaveAsync(c, ct);
    }

    public async Task<DecisionResult> RejectAsync(Guid id, RejectRequest? body, CancellationToken ct)
    {
        var c = await db.Candidates.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return new(DecisionStatus.NotFound);
        if (!IsOpen(c)) return new(DecisionStatus.Conflict);

        var (reason, errors) = CandidateRequestValidator.ValidateReason(body);
        if (reason is null) return new(DecisionStatus.Invalid, Errors: errors);

        var before = new { status = c.Status, rejectionReason = c.RejectionReason };
        c.Status = CandidateStatus.Rejected;
        c.RejectionReason = reason;
        c.RejectedByUserId = user.Require().Id;
        c.UpdatedAt = clock.UtcNow;
        audit.Record(AuditActions.CandidateReject, AuditEntityTypes.Candidate, c.Id, before,
            new { status = c.Status, rejectionReason = c.RejectionReason });
        return await SaveAsync(c, ct);
    }

    /// <summary>Merge into a published item of the same kind (REQ-066, 075): adds the source, refreshes <c>lastVerifiedAt</c>, creates no item.</summary>
    public async Task<DecisionResult> MergeAsync(Guid id, MergeRequest? body, CancellationToken ct)
    {
        var c = await db.Candidates.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return new(DecisionStatus.NotFound);
        if (!IsOpen(c)) return new(DecisionStatus.Conflict);

        var invalid = new DecisionResult(DecisionStatus.Invalid,
            Errors: new Dictionary<string, string[]> { ["targetId"] = ["The target must be a published item of the same kind."] });
        if (!Guid.TryParse(body?.TargetId, out var targetId)) return invalid;
        // A changed-since-approval candidate belongs to its matched item: another target would move its fingerprint away from it.
        if (c.MatchedItemId is { } matched && matched != targetId) return invalid;

        // Event and Deal are separate tables, so a target of the other kind is simply not found.
        Event? ev = null;
        Deal? deal = null;
        DateTimeOffset verified;
        if (c.Kind == ItemKind.Event)
        {
            ev = await db.Events.SingleOrDefaultAsync(x => x.Id == targetId && x.Status == ItemStatus.Published, ct);
            if (ev is null) return invalid;
            verified = ev.LastVerifiedAt;
        }
        else
        {
            deal = await db.Deals.SingleOrDefaultAsync(x => x.Id == targetId && x.Status == ItemStatus.Published, ct);
            if (deal is null) return invalid;
            verified = deal.LastVerifiedAt;
        }

        var now = clock.UtcNow;
        var newVerified = verified > c.FetchedAt ? verified : c.FetchedAt;
        if (ev is not null) { ev.LastVerifiedAt = newVerified; ev.UpdatedAt = now; }
        if (deal is not null) { deal.LastVerifiedAt = newVerified; deal.UpdatedAt = now; }

        if (!await db.ItemSources.AnyAsync(x => x.ItemId == targetId && x.Fingerprint == c.Fingerprint, ct))
            db.ItemSources.Add(new ItemSource
            {
                ItemKind = c.Kind,
                ItemId = targetId,
                SourceId = c.SourceId,
                SourceUrl = c.Fields.Url ?? c.SourceUrl,
                Fingerprint = c.Fingerprint,
                CandidateId = c.Id,
                AddedAt = now,
            });

        var before = new { status = c.Status };
        c.Status = CandidateStatus.Merged;
        c.UpdatedAt = now;
        audit.Record(AuditActions.CandidateMerge, AuditEntityTypes.Candidate, c.Id, before,
            new { status = c.Status, targetId, kind = c.Kind });
        return await SaveAsync(c, ct);
    }

    /// <summary>Pending, or auto-rejected (no reviewer yet); published, merged and person-rejected candidates are final (REQ-067).</summary>
    internal static bool IsOpen(Candidate c) =>
        c.Status == CandidateStatus.Pending || (c.Status == CandidateStatus.Rejected && c.RejectedByUserId is null);

    private async Task CheckReferencesAsync(ItemKind kind, CandidateFieldsRequest req, Dictionary<string, string[]> errors, CancellationToken ct)
    {
        if (req.CitySlug is { } city && !await db.Cities.AnyAsync(x => x.Slug == city, ct))
            errors["citySlug"] = ["Unknown city."];
        if (req.CategorySlug is { } cat && !await db.Categories.AnyAsync(x => x.Slug == cat && x.Kind == kind, ct))
            errors["categorySlug"] = ["Unknown category for this kind."];
    }

    // The xmin token makes a parallel decision fail instead of overwriting; the loser gets a 409 (changed concurrently).
    private async Task<DecisionResult> SaveAsync(Candidate c, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(DecisionStatus.Conflict);
        }
        return new(DecisionStatus.Done, await CandidateQueries.GetAsync(db, c.Id, ct));
    }
}
