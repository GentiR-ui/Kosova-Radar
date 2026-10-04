using App.Application.Abstractions;
using App.Application.Common;
using App.Application.Reference;
using App.Domain.Content;
using App.Domain.Reference;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Candidates;

/// <summary>Read side of the review queue (REQ-060, 061).</summary>
public static class CandidateQueries
{
    /// <summary>Parses the optional list filters; every bad value is reported on its own query parameter.</summary>
    public static (CandidateFilter? Filter, Dictionary<string, string[]>? Errors) ParseFilter(
        string? status, string? validation, string? kind, string? sourceId)
    {
        var errors = new Dictionary<string, string[]>();
        var s = ParseEnum<CandidateStatus>(status, "status", errors);
        var v = ParseEnum<ValidationOutcome>(validation, "validation", errors);
        var k = ParseEnum<ItemKind>(kind, "kind", errors);
        Guid? src = null;
        if (!string.IsNullOrEmpty(sourceId))
        {
            if (Guid.TryParse(sourceId, out var g)) src = g;
            else errors["sourceId"] = ["Must be a valid id."];
        }
        return errors.Count > 0 ? (null, errors) : (new CandidateFilter(s, v, k, src), null);
    }

    // Names only: Enum.TryParse alone would also accept "1" or "pending, rejected".
    private static T? ParseEnum<T>(string? value, string field, Dictionary<string, string[]> errors) where T : struct, Enum
    {
        if (string.IsNullOrEmpty(value)) return null;
        foreach (var name in Enum.GetNames<T>())
            if (string.Equals(name, value, StringComparison.OrdinalIgnoreCase)) return Enum.Parse<T>(name);
        errors[field] = [$"Must be one of: {string.Join(", ", Enum.GetNames<T>().Select(n => char.ToLowerInvariant(n[0]) + n[1..]))}."];
        return null;
    }

    public static async Task<PagedResult<CandidateSummaryDto>> ListAsync(
        IAppDbContext db, CandidateFilter f, int page, int pageSize, CancellationToken ct)
    {
        var query = db.Candidates.AsNoTracking()
            .Where(c => (f.Status == null || c.Status == f.Status)
                        && (f.Validation == null || c.Validation == f.Validation)
                        && (f.Kind == null || c.Kind == f.Kind)
                        && (f.SourceId == null || c.SourceId == f.SourceId));
        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(c => c.FetchedAt).ThenBy(c => c.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            // Evidence (up to 64 KB) is not needed in the list.
            .Select(c => new
            {
                c.Id, c.Kind, c.Fields, c.Status, c.Validation, c.Issues, c.SourceId, c.FetchedAt, c.IsSynthetic,
                SourceName = db.Sources.Where(s => s.Id == c.SourceId).Select(s => s.Name).Single(),
            })
            .ToListAsync(ct);
        var items = rows.Select(r => new CandidateSummaryDto(
            r.Id, r.Kind, r.Fields.Title, r.Status, r.Validation, r.Issues.Select(IssueDto.From).ToList(),
            r.SourceId, r.SourceName, r.FetchedAt, r.IsSynthetic)).ToList();
        return new PagedResult<CandidateSummaryDto>(items, page, pageSize, total);
    }

    public static async Task<CandidateDto?> GetAsync(IAppDbContext db, Guid id, CancellationToken ct)
    {
        var c = await db.Candidates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return null;

        var sourceName = await db.Sources.Where(s => s.Id == c.SourceId).Select(s => s.Name).SingleAsync(ct);
        var suggested = c.SuggestedCategoryId is { } catId
            ? await db.Categories.Where(x => x.Id == catId).Select(x => new CategoryDto(x.Slug, x.Name, x.Kind)).SingleOrDefaultAsync(ct)
            : null;
        return new CandidateDto(
            c.Id, c.Kind, c.Fields.Title, c.Status, c.Validation, c.Issues.Select(IssueDto.From).ToList(),
            c.SourceId, sourceName, c.FetchedAt, c.IsSynthetic, c.ExternalId, c.SourceUrl,
            CandidateFieldsDto.From(c.Fields), c.Evidence, suggested,
            c.AiModel is null ? null : new CandidateAiDto(c.AiModel, c.AiRequestId, c.AiConfidence, c.AiProcessedAt, c.AiSuggestedFields),
            await DuplicateOfAsync(db, c, ct), c.MatchedItemId, c.PublishedItemId, c.RejectionReason);
    }

    private static async Task<DuplicateOfDto?> DuplicateOfAsync(IAppDbContext db, Candidate c, CancellationToken ct)
    {
        if (c.DuplicateOfKind is not { } kind || c.DuplicateOfId is not { } id) return null;
        var isCandidate = c.DuplicateOfIsCandidate == true;
        // Polymorphic reference without a FK (design.md §4): the target may be gone, then the title is unknown.
        string? title;
        if (isCandidate)
            title = (await db.Candidates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct))?.Fields.Title;
        else if (kind == ItemKind.Event)
            title = await db.Events.Where(x => x.Id == id).Select(x => x.Title).SingleOrDefaultAsync(ct);
        else
            title = await db.Deals.Where(x => x.Id == id).Select(x => x.Title).SingleOrDefaultAsync(ct);
        return new DuplicateOfDto(kind, id, title, isCandidate);
    }
}
