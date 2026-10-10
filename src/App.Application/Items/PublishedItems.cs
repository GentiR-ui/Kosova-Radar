using App.Application.Abstractions;
using App.Application.Audit;
using App.Application.Candidates;
using App.Application.Common;
using App.Domain.Content;
using App.Domain.Reference;
using App.Domain.Text;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Items;

public sealed record AdminItemDto(
    ItemKind Kind, Guid Id, string Title, string AdminStatus, string? CityName, DateTimeOffset? Date, string SourceName, DateTimeOffset LastVerifiedAt);

public enum ItemActionStatus { Done, NotFound, Conflict, Invalid }

public sealed record ItemActionResult(ItemActionStatus Status, IReadOnlyDictionary<string, string[]>? Errors = null);

/// <summary>Management of published events and deals (REQ-069, 070, 071). Each change and its audit entry commit together.</summary>
public sealed class PublishedItems(IAppDbContext db, IAuditWriter audit, IClock clock)
{
    public static string AdminStatus(ItemStatus s) => s switch
    {
        ItemStatus.Published => "published",
        ItemStatus.Expired => "expired",
        _ => "withdrawn",
    };

    /// <summary>Parses the optional list filters; every bad value is reported on its own query parameter.</summary>
    public static (ItemStatus? Status, ItemKind? Kind, Dictionary<string, string[]>? Errors) ParseFilter(string? status, string? kind)
    {
        var errors = new Dictionary<string, string[]>();
        ItemStatus? parsedStatus = null;
        ItemKind? parsedKind = null;
        if (!string.IsNullOrEmpty(status))
        {
            foreach (var s in Enum.GetValues<ItemStatus>())
                if (AdminStatus(s) == status.ToLowerInvariant()) parsedStatus = s;
            if (parsedStatus is null) errors["status"] = ["Must be one of: published, expired, withdrawn."];
        }
        if (!string.IsNullOrEmpty(kind))
        {
            if (kind.Equals("event", StringComparison.OrdinalIgnoreCase)) parsedKind = ItemKind.Event;
            else if (kind.Equals("deal", StringComparison.OrdinalIgnoreCase)) parsedKind = ItemKind.Deal;
            else errors["kind"] = ["Must be one of: event, deal."];
        }
        return (parsedStatus, parsedKind, errors.Count > 0 ? errors : null);
    }

    public async Task<PagedResult<AdminItemDto>> ListAsync(ItemKind? kind, ItemStatus? status, string? search, int page, int pageSize, CancellationToken ct)
    {
        var term = TextNormalizer.Fold(search);
        // Two tables: each side supplies its first page*pageSize rows (ordered before projecting), the merge happens in memory.
        var skip = (long)(page - 1) * pageSize;
        var take = (int)Math.Min(skip + pageSize, int.MaxValue);
        var total = 0;
        var rows = new List<Row>();
        if (kind != ItemKind.Deal)
        {
            var events = db.Events.AsNoTracking()
                .Where(e => (status == null || e.Status == status) && (term == "" || e.SearchText.Contains(term)));
            total += await events.CountAsync(ct);
            rows.AddRange(await events.OrderByDescending(e => e.LastVerifiedAt).ThenBy(e => e.Id).Take(take)
                .Select(e => new Row(ItemKind.Event, e.Id, e.Title, e.Status,
                    db.Cities.Where(c => c.Id == e.CityId).Select(c => c.Name).FirstOrDefault(), (DateTimeOffset?)e.StartsAt,
                    db.Sources.Where(s => s.Id == e.PrimarySourceId).Select(s => s.Name).FirstOrDefault()!, e.LastVerifiedAt))
                .ToListAsync(ct));
        }
        if (kind != ItemKind.Event)
        {
            var deals = db.Deals.AsNoTracking()
                .Where(d => (status == null || d.Status == status) && (term == "" || d.SearchText.Contains(term)));
            total += await deals.CountAsync(ct);
            rows.AddRange(await deals.OrderByDescending(d => d.LastVerifiedAt).ThenBy(d => d.Id).Take(take)
                .Select(d => new Row(ItemKind.Deal, d.Id, d.Title, d.Status,
                    db.Cities.Where(c => c.Id == d.CityId).Select(c => c.Name).FirstOrDefault(), d.ValidUntil,
                    db.Sources.Where(s => s.Id == d.PrimarySourceId).Select(s => s.Name).FirstOrDefault()!, d.LastVerifiedAt))
                .ToListAsync(ct));
        }
        if (skip >= total) return new([], page, pageSize, total);
        var items = rows.OrderByDescending(r => r.LastVerifiedAt).ThenBy(r => r.Id).Skip((int)skip).Take(pageSize)
            .Select(r => new AdminItemDto(r.Kind, r.Id, r.Title, AdminStatus(r.Status), r.CityName, r.Date, r.SourceName, r.LastVerifiedAt))
            .ToList();
        return new(items, page, pageSize, total);
    }

    public async Task<ItemActionResult> EditEventAsync(Guid id, CandidateFieldsRequest? body, CancellationToken ct)
    {
        var e = await db.Events.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return new(ItemActionStatus.NotFound);
        if (e.Status == ItemStatus.Withdrawn) return new(ItemActionStatus.Conflict);
        var v = await ValidateAsync(ItemKind.Event, body, ct);
        if (v.Fields is null) return new(ItemActionStatus.Invalid, v.Errors);

        var before = Snapshot(e);
        var venueId = await ItemFields.FindOrCreateVenueAsync(db, v.Fields.VenueName, v.CityId!.Value, ct);
        ItemFields.ApplyEvent(e, v.Fields, v.CityId.Value, venueId, v.CategoryId, clock.UtcNow);
        audit.Record(AuditActions.ItemEdit, AuditEntityTypes.Event, e.Id, before, Snapshot(e));
        return await SaveAsync(ct);
    }

    public async Task<ItemActionResult> EditDealAsync(Guid id, CandidateFieldsRequest? body, CancellationToken ct)
    {
        var d = await db.Deals.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) return new(ItemActionStatus.NotFound);
        if (d.Status == ItemStatus.Withdrawn) return new(ItemActionStatus.Conflict);
        var v = await ValidateAsync(ItemKind.Deal, body, ct);
        if (v.Fields is null) return new(ItemActionStatus.Invalid, v.Errors);

        var before = Snapshot(d);
        ItemFields.ApplyDeal(d, v.Fields, v.CityId, v.CategoryId, clock.UtcNow);
        audit.Record(AuditActions.ItemEdit, AuditEntityTypes.Deal, d.Id, before, Snapshot(d));
        return await SaveAsync(ct);
    }

    /// <summary>Idempotent; a withdrawn item cannot be expired.</summary>
    public async Task<ItemActionResult> ExpireEventAsync(Guid id, CancellationToken ct)
    {
        var e = await db.Events.SingleOrDefaultAsync(x => x.Id == id, ct);
        return e is null ? new(ItemActionStatus.NotFound) : await ChangeStatusAsync(AuditEntityTypes.Event, id, e.Status, ItemStatus.Expired, s => e.Status = s, t => e.UpdatedAt = t, ct);
    }

    public async Task<ItemActionResult> ExpireDealAsync(Guid id, CancellationToken ct)
    {
        var d = await db.Deals.SingleOrDefaultAsync(x => x.Id == id, ct);
        return d is null ? new(ItemActionStatus.NotFound) : await ChangeStatusAsync(AuditEntityTypes.Deal, id, d.Status, ItemStatus.Expired, s => d.Status = s, t => d.UpdatedAt = t, ct);
    }

    public async Task<ItemActionResult> WithdrawEventAsync(Guid id, CancellationToken ct)
    {
        var e = await db.Events.SingleOrDefaultAsync(x => x.Id == id, ct);
        return e is null ? new(ItemActionStatus.NotFound) : await ChangeStatusAsync(AuditEntityTypes.Event, id, e.Status, ItemStatus.Withdrawn, s => e.Status = s, t => e.UpdatedAt = t, ct);
    }

    public async Task<ItemActionResult> WithdrawDealAsync(Guid id, CancellationToken ct)
    {
        var d = await db.Deals.SingleOrDefaultAsync(x => x.Id == id, ct);
        return d is null ? new(ItemActionStatus.NotFound) : await ChangeStatusAsync(AuditEntityTypes.Deal, id, d.Status, ItemStatus.Withdrawn, s => d.Status = s, t => d.UpdatedAt = t, ct);
    }

    private async Task<ItemActionResult> ChangeStatusAsync(
        string entityType, Guid id, ItemStatus current, ItemStatus target, Action<ItemStatus> setStatus, Action<DateTimeOffset> touch, CancellationToken ct)
    {
        if (current == target) return new(ItemActionStatus.Done);
        if (target == ItemStatus.Expired && current == ItemStatus.Withdrawn) return new(ItemActionStatus.Conflict);

        setStatus(target);
        touch(clock.UtcNow);
        audit.Record(target == ItemStatus.Expired ? AuditActions.ItemExpire : AuditActions.ItemWithdraw, entityType, id,
            new { status = AdminStatus(current) }, new { status = AdminStatus(target) });
        return await SaveAsync(ct);
    }

    private sealed record Validated(CandidateFields? Fields, Guid? CityId, Guid? CategoryId, Dictionary<string, string[]>? Errors);

    private async Task<Validated> ValidateAsync(ItemKind kind, CandidateFieldsRequest? body, CancellationToken ct)
    {
        var (req, errors) = CandidateRequestValidator.Validate(body);
        if (req is null) return new(null, null, null, errors);
        var fields = CandidateRequestValidator.Apply(kind, new CandidateFields(), req);
        foreach (var (key, messages) in ItemFields.MissingFields(kind, fields)) errors.TryAdd(key, messages);

        Guid? cityId = null, categoryId = null;
        if (req.CitySlug is { } city)
        {
            cityId = await db.Cities.Where(x => x.Slug == city).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
            if (cityId is null) errors["citySlug"] = ["Unknown city."];
        }
        if (req.CategorySlug is { } cat)
        {
            categoryId = await db.Categories.Where(x => x.Slug == cat && x.Kind == kind).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
            if (categoryId is null) errors["categorySlug"] = ["Unknown category for this kind."];
        }
        return errors.Count > 0 ? new(null, null, null, errors) : new(fields, cityId, categoryId, null);
    }

    // The xmin token makes a parallel change fail instead of overwriting; the loser gets a 409.
    private async Task<ItemActionResult> SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return new(ItemActionStatus.Conflict);
        }
        return new(ItemActionStatus.Done);
    }

    private static object Snapshot(Event e) => new
    {
        status = AdminStatus(e.Status),
        e.Title,
        e.Description,
        e.StartsAt,
        e.StartTimeKnown,
        e.EndsAt,
        e.CityId,
        e.VenueId,
        e.CategoryId,
        e.PriceText,
        e.PriceAmount,
        e.Currency,
        e.ImageUrl,
        url = e.SourceUrl,
    };

    private static object Snapshot(Deal d) => new
    {
        status = AdminStatus(d.Status),
        d.Title,
        d.Description,
        d.MerchantName,
        d.CityId,
        d.CategoryId,
        d.OldPrice,
        d.NewPrice,
        d.PriceText,
        d.Currency,
        d.ValidFrom,
        d.ValidUntil,
        d.ImageUrl,
        url = d.SourceUrl,
    };

    private sealed record Row(
        ItemKind Kind, Guid Id, string Title, ItemStatus Status, string? CityName, DateTimeOffset? Date, string SourceName, DateTimeOffset LastVerifiedAt);
}
