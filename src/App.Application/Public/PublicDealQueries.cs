using App.Application.Abstractions;
using App.Application.Common;
using App.Application.Reference;
using App.Domain.Content;
using App.Domain.Reference;
using App.Domain.Text;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Public;

public sealed record DealSummaryDto(
    Guid Id, string Title, string? MerchantName, CityDto? City, CategoryDto? Category, decimal? OldPrice, decimal? NewPrice,
    string? PriceText, string? Currency, int? DiscountPercent, DateTimeOffset? ValidFrom, DateTimeOffset? ValidUntil,
    string? ImageUrl, string SourceName, string SourceUrl, DateTimeOffset LastVerifiedAt, string Freshness, string Status, bool IsSynthetic);

/// <summary>Raw (unvalidated) query-string values of <c>GET /api/deals</c>.</summary>
public sealed record DealListRequest(string? City, string? Category, string? Search, string? Page, string? PageSize);

public sealed class PublicDealQueries(IAppDbContext db, IClock clock)
{
    /// <summary>Returns the page, or the field errors of an invalid request (REQ-082, 083).</summary>
    public async Task<(PagedResult<DealSummaryDto>? Result, Dictionary<string, string[]>? Errors)> ListAsync(
        DealListRequest request, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var pageNumber = PublicEventQueries.ParseInt(request.Page, "page", errors);
        var sizeNumber = PublicEventQueries.ParseInt(request.PageSize, "pageSize", errors);
        var (page, pageSize, pagingErrors) = Paging.Parse(pageNumber, sizeNumber);
        foreach (var (field, messages) in pagingErrors ?? []) errors.TryAdd(field, messages);

        Guid? cityId = null, categoryId = null;
        if (!string.IsNullOrWhiteSpace(request.City))
        {
            cityId = await db.Cities.AsNoTracking().Where(c => c.Slug == request.City).Select(c => (Guid?)c.Id).SingleOrDefaultAsync(ct);
            if (cityId is null) errors["city"] = ["Unknown city."];
        }
        if (!string.IsNullOrWhiteSpace(request.Category))
        {
            categoryId = await db.Categories.AsNoTracking().Where(c => c.Kind == ItemKind.Deal && c.Slug == request.Category)
                .Select(c => (Guid?)c.Id).SingleOrDefaultAsync(ct);
            if (categoryId is null) errors["category"] = ["Unknown category."];
        }
        if (errors.Count > 0) return (null, errors);

        var now = clock.UtcNow;
        var query = db.Deals.AsNoTracking().Where(ActiveItems.Deal(now));
        if (cityId is { } cid) query = query.Where(d => d.CityId == cid);
        if (categoryId is { } catId) query = query.Where(d => d.CategoryId == catId);
        var needle = TextNormalizer.Fold(request.Search);
        if (needle.Length > 0) query = query.Where(d => d.SearchText.Contains(needle));

        var total = await query.CountAsync(ct);
        var skip = (long)(page - 1) * pageSize;
        var deals = skip >= total
            ? []
            : await query.OrderBy(d => d.ValidUntil == null).ThenBy(d => d.ValidUntil).ThenBy(d => d.Id)
                .Skip((int)skip).Take(pageSize).ToListAsync(ct);
        return (new PagedResult<DealSummaryDto>(await MapAsync(deals, now, ct), page, pageSize, total), null);
    }

    private async Task<List<DealSummaryDto>> MapAsync(List<Deal> deals, DateTimeOffset now, CancellationToken ct)
    {
        var cityIds = deals.Where(d => d.CityId != null).Select(d => d.CityId!.Value).Distinct().ToList();
        var categoryIds = deals.Where(d => d.CategoryId != null).Select(d => d.CategoryId!.Value).Distinct().ToList();
        var sourceIds = deals.Select(d => d.PrimarySourceId).Distinct().ToList();
        var cities = await db.Cities.AsNoTracking().Where(c => cityIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => new CityDto(c.Slug, c.Name), ct);
        var categories = await db.Categories.AsNoTracking().Where(c => categoryIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => new CategoryDto(c.Slug, c.Name, c.Kind), ct);
        var sources = await db.Sources.AsNoTracking().Where(s => sourceIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        return deals.Select(d => new DealSummaryDto(
            d.Id, d.Title, d.MerchantName, d.CityId is { } c ? cities.GetValueOrDefault(c) : null,
            d.CategoryId is { } k ? categories.GetValueOrDefault(k) : null, d.OldPrice, d.NewPrice, d.PriceText, d.Currency,
            ContentRules.DiscountPercent(d.OldPrice, d.NewPrice), d.ValidFrom, d.ValidUntil, d.ImageUrl, sources[d.PrimarySourceId],
            d.SourceUrl, d.LastVerifiedAt, ContentRules.IsFresh(d.LastVerifiedAt, now) ? "fresh" : "stale",
            d.IsActive(now) ? "active" : "expired", d.IsSynthetic)).ToList();
    }
}
