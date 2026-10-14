using App.Application.Abstractions;
using App.Application.Common;
using App.Domain.Text;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Public;

public sealed record SearchResultDto(string Kind, Guid Id, string Title, DateTimeOffset? Date, string? CityName, string SourceName);

/// <summary>Raw query-string values of <c>GET /api/search</c>.</summary>
public sealed record SearchRequest(string? Q, string? Page, string? PageSize);

/// <summary>Unified search over active events (by start) followed by active deals (by validUntil, open-ended last), REQ-089.</summary>
public sealed class PublicSearchQueries(IAppDbContext db, IClock clock, DealOptions? dealOptions = null)
{
    public const int MinQueryLength = 2;

    public async Task<(PagedResult<SearchResultDto>? Result, Dictionary<string, string[]>? Errors)> SearchAsync(
        SearchRequest request, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var pageNumber = PublicEventQueries.ParseInt(request.Page, "page", errors);
        var sizeNumber = PublicEventQueries.ParseInt(request.PageSize, "pageSize", errors);
        var (page, pageSize, pagingErrors) = Paging.Parse(pageNumber, sizeNumber);
        foreach (var (field, messages) in pagingErrors ?? []) errors.TryAdd(field, messages);

        var q = request.Q?.Trim() ?? "";
        var needle = TextNormalizer.Fold(q);
        if (q.Length < MinQueryLength) errors["q"] = [$"Must be at least {MinQueryLength} characters."];
        else if (needle.Length == 0) errors["q"] = ["Must contain letters or digits."]; // "--" folds to "" and would match everything
        if (errors.Count > 0) return (null, errors);

        var now = clock.UtcNow;
        var events = db.Events.AsNoTracking().Where(ActiveItems.Event(now)).Where(e => e.SearchText.Contains(needle));
        var deals = db.Deals.AsNoTracking().Where(ActiveItems.Deal(now, dealOptions)).Where(d => d.SearchText.Contains(needle));
        var eventCount = await events.CountAsync(ct);
        var dealCount = await deals.CountAsync(ct);
        var total = eventCount + dealCount;

        var skip = (long)(page - 1) * pageSize;
        var items = new List<SearchResultDto>();
        if (skip < eventCount)
        {
            var rows = await events.OrderBy(e => e.StartsAt).ThenBy(e => e.Id).Skip((int)skip).Take(pageSize)
                .Select(e => new
                {
                    e.Id,
                    e.Title,
                    e.StartsAt,
                    City = db.Cities.Where(c => c.Id == e.CityId).Select(c => c.Name).FirstOrDefault(),
                    Source = db.Sources.Where(s => s.Id == e.PrimarySourceId).Select(s => s.Name).First(),
                }).ToListAsync(ct);
            items.AddRange(rows.Select(r => new SearchResultDto("event", r.Id, r.Title, r.StartsAt, r.City, r.Source)));
        }
        var remaining = pageSize - items.Count;
        if (remaining > 0 && skip < total)
        {
            var dealSkip = (int)Math.Max(0, skip - eventCount);
            var rows = await deals.OrderBy(d => d.ValidUntil == null).ThenBy(d => d.ValidUntil).ThenBy(d => d.Id)
                .Skip(dealSkip).Take(remaining)
                .Select(d => new
                {
                    d.Id,
                    d.Title,
                    d.ValidUntil,
                    City = db.Cities.Where(c => c.Id == d.CityId).Select(c => c.Name).FirstOrDefault(),
                    Source = db.Sources.Where(s => s.Id == d.PrimarySourceId).Select(s => s.Name).First(),
                }).ToListAsync(ct);
            items.AddRange(rows.Select(r => new SearchResultDto("deal", r.Id, r.Title, r.ValidUntil, r.City, r.Source)));
        }
        return (new PagedResult<SearchResultDto>(items, page, pageSize, total), null);
    }
}
