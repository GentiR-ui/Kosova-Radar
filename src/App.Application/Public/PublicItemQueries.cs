using App.Application.Abstractions;
using App.Application.Items;
using App.Application.Reference;
using App.Domain.Content;
using App.Domain.Reference;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Public;

public sealed record SourceRefDto(string Name, string Url);

public sealed record EventDetailDto(
    Guid Id, string Title, DateTimeOffset StartsAt, DateTimeOffset? EndsAt, CityDto City, string? VenueName, CategoryDto? Category,
    string? PriceText, decimal? PriceAmount, string? Currency, string? ImageUrl, string SourceName, string SourceUrl,
    DateTimeOffset LastVerifiedAt, string Freshness, string Status, bool IsSynthetic,
    string? Description, IReadOnlyList<SourceRefDto> Sources,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AdminStatus = null);

public sealed record DealDetailDto(
    Guid Id, string Title, string? MerchantName, CityDto? City, CategoryDto? Category, decimal? OldPrice, decimal? NewPrice,
    string? PriceText, string? Currency, int? DiscountPercent, DateTimeOffset? ValidFrom, DateTimeOffset? ValidUntil,
    string? ImageUrl, string SourceName, string SourceUrl, DateTimeOffset LastVerifiedAt, string Freshness, string Status,
    bool IsSynthetic, string? Description, IReadOnlyList<SourceRefDto> Sources,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AdminStatus = null);

/// <summary>Anonymous detail of a published event or deal (REQ-079, 085). Withdrawn items do not exist publicly; the admin view (REQ-069) includes them with <c>adminStatus</c>.</summary>
public sealed class PublicItemQueries(IAppDbContext db, IClock clock)
{
    public async Task<EventDetailDto?> GetEventAsync(Guid id, CancellationToken ct, bool admin = false)
    {
        var e = await db.Events.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && (admin || x.Status != ItemStatus.Withdrawn), ct);
        if (e is null) return null;
        var now = clock.UtcNow;
        var city = await db.Cities.AsNoTracking().Where(c => c.Id == e.CityId).Select(c => new CityDto(c.Slug, c.Name)).SingleAsync(ct);
        var venue = e.VenueId is { } v ? await db.Venues.AsNoTracking().Where(x => x.Id == v).Select(x => x.Name).SingleOrDefaultAsync(ct) : null;
        var (sourceName, sources) = await SourcesAsync(ItemKind.Event, e.Id, e.PrimarySourceId, e.SourceUrl, ct);
        return new(e.Id, e.Title, e.StartsAt, e.EndsAt, city, venue, await CategoryAsync(e.CategoryId, ct), e.PriceText, e.PriceAmount,
            e.Currency, e.ImageUrl, sourceName, e.SourceUrl, e.LastVerifiedAt, Freshness(e.LastVerifiedAt, now),
            e.IsActive(now) ? "active" : "expired", e.IsSynthetic, e.Description, sources,
            admin ? PublishedItems.AdminStatus(e.Status) : null);
    }

    public async Task<DealDetailDto?> GetDealAsync(Guid id, CancellationToken ct, bool admin = false)
    {
        var d = await db.Deals.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && (admin || x.Status != ItemStatus.Withdrawn), ct);
        if (d is null) return null;
        var now = clock.UtcNow;
        var city = d.CityId is { } c
            ? await db.Cities.AsNoTracking().Where(x => x.Id == c).Select(x => new CityDto(x.Slug, x.Name)).SingleOrDefaultAsync(ct)
            : null;
        var (sourceName, sources) = await SourcesAsync(ItemKind.Deal, d.Id, d.PrimarySourceId, d.SourceUrl, ct);
        return new(d.Id, d.Title, d.MerchantName, city, await CategoryAsync(d.CategoryId, ct), d.OldPrice, d.NewPrice, d.PriceText,
            d.Currency, ContentRules.DiscountPercent(d.OldPrice, d.NewPrice), d.ValidFrom, d.ValidUntil, d.ImageUrl, sourceName,
            d.SourceUrl, d.LastVerifiedAt, Freshness(d.LastVerifiedAt, now), d.IsActive(now) ? "active" : "expired", d.IsSynthetic,
            d.Description, sources, admin ? PublishedItems.AdminStatus(d.Status) : null);
    }

    private static string Freshness(DateTimeOffset lastVerifiedAt, DateTimeOffset now) =>
        ContentRules.IsFresh(lastVerifiedAt, now) ? "fresh" : "stale";

    private Task<CategoryDto?> CategoryAsync(Guid? id, CancellationToken ct) =>
        id is null
            ? Task.FromResult<CategoryDto?>(null)
            : db.Categories.AsNoTracking().Where(c => c.Id == id).Select(c => new CategoryDto(c.Slug, c.Name, c.Kind)).SingleOrDefaultAsync(ct);

    /// <summary>Primary source first, then the others in the order they were added.</summary>
    private async Task<(string PrimaryName, IReadOnlyList<SourceRefDto> Sources)> SourcesAsync(
        ItemKind kind, Guid itemId, Guid primarySourceId, string primaryUrl, CancellationToken ct)
    {
        var rows = await db.ItemSources.AsNoTracking()
            .Where(s => s.ItemKind == kind && s.ItemId == itemId)
            .OrderBy(s => s.AddedAt).ThenBy(s => s.Id)
            .Join(db.Sources, i => i.SourceId, s => s.Id, (i, s) => new { i.SourceId, i.SourceUrl, s.Name })
            .ToListAsync(ct);
        var primaryName = await db.Sources.AsNoTracking().Where(s => s.Id == primarySourceId).Select(s => s.Name).SingleAsync(ct);
        var list = rows.Where(r => r.SourceId == primarySourceId).Take(1).Select(r => new SourceRefDto(r.Name, primaryUrl))
            .Concat(rows.Where(r => r.SourceId != primarySourceId).Select(r => new SourceRefDto(r.Name, r.SourceUrl)))
            .ToList();
        return (primaryName, list);
    }
}
