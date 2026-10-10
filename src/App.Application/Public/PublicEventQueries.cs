using System.Globalization;
using App.Application.Abstractions;
using App.Application.Common;
using App.Application.Reference;
using App.Domain.Content;
using App.Domain.Reference;
using App.Domain.Text;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Public;

public sealed record EventSummaryDto(
    Guid Id, string Title, DateTimeOffset StartsAt, bool StartTimeKnown, DateTimeOffset? EndsAt, CityDto City, string? VenueName, CategoryDto? Category,
    string? PriceText, decimal? PriceAmount, string? Currency, string? ImageUrl, string SourceName, string SourceUrl,
    DateTimeOffset LastVerifiedAt, string Freshness, string Status, bool IsSynthetic);

/// <summary>Raw (unvalidated) query-string values of <c>GET /api/events</c>.</summary>
public sealed record EventListRequest(string? City, string? Category, string? From, string? To, string? Search, string? Page, string? PageSize);

public sealed class PublicEventQueries(IAppDbContext db, IClock clock)
{
    /// <summary>"Europe/Pristina" = IANA <c>Europe/Belgrade</c> (design.md §4).</summary>
    private static readonly TimeZoneInfo Pristina = TimeZoneInfo.FindSystemTimeZoneById("Europe/Belgrade");

    /// <summary>Returns the page, or the field errors of an invalid request (REQ-082).</summary>
    public async Task<(PagedResult<EventSummaryDto>? Result, Dictionary<string, string[]>? Errors)> ListAsync(
        EventListRequest request, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var pageNumber = ParseInt(request.Page, "page", errors);
        var sizeNumber = ParseInt(request.PageSize, "pageSize", errors);
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
            categoryId = await db.Categories.AsNoTracking().Where(c => c.Kind == ItemKind.Event && c.Slug == request.Category)
                .Select(c => (Guid?)c.Id).SingleOrDefaultAsync(ct);
            if (categoryId is null) errors["category"] = ["Unknown category."];
        }
        var from = ParseDate(request.From, "from", errors);
        var to = ParseDate(request.To, "to", errors);
        if (from is { } f && to is { } t && f > t) errors["from"] = ["Must not be after 'to'."];
        if (errors.Count > 0) return (null, errors);

        var now = clock.UtcNow;
        var query = db.Events.AsNoTracking().Where(ActiveItems.Event(now));
        if (cityId is { } cid) query = query.Where(e => e.CityId == cid);
        if (categoryId is { } catId) query = query.Where(e => e.CategoryId == catId);
        if (from is { } fd) { var start = StartOfDay(fd); query = query.Where(e => e.StartsAt >= start); }
        if (to is { } td)
        {
            // Midnight of the next day, not +24 h: a day is 23 or 25 h long on DST changes.
            var end = StartOfDay(td.AddDays(1));
            query = query.Where(e => e.StartsAt < end);
        }
        var needle = TextNormalizer.Fold(request.Search);
        if (needle.Length > 0) query = query.Where(e => e.SearchText.Contains(needle));

        var total = await query.CountAsync(ct);
        var skip = (long)(page - 1) * pageSize;
        var events = skip >= total
            ? []
            : await query.OrderBy(e => e.StartsAt).ThenBy(e => e.Id).Skip((int)skip).Take(pageSize).ToListAsync(ct);
        return (new PagedResult<EventSummaryDto>(await MapAsync(events, now, ct), page, pageSize, total), null);
    }

    /// <summary>Midnight Europe/Pristina of the date, as a UTC instant.</summary>
    private static DateTimeOffset StartOfDay(DateOnly date) =>
        new(TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue), Pristina), TimeSpan.Zero);

    private static DateOnly? ParseDate(string? value, string name, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            && date.Year is >= 1900 and <= 2200) return date;
        errors[name] = ["Must be a date in the form yyyy-MM-dd (years 1900-2200)."];
        return null;
    }

    internal static int? ParseInt(string? value, string name, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return n;
        errors[name] = ["Must be a whole number."];
        return null;
    }

    private async Task<List<EventSummaryDto>> MapAsync(List<Event> events, DateTimeOffset now, CancellationToken ct)
    {
        var cityIds = events.Select(e => e.CityId).Distinct().ToList();
        var categoryIds = events.Where(e => e.CategoryId != null).Select(e => e.CategoryId!.Value).Distinct().ToList();
        var venueIds = events.Where(e => e.VenueId != null).Select(e => e.VenueId!.Value).Distinct().ToList();
        var sourceIds = events.Select(e => e.PrimarySourceId).Distinct().ToList();
        var cities = await db.Cities.AsNoTracking().Where(c => cityIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => new CityDto(c.Slug, c.Name), ct);
        var categories = await db.Categories.AsNoTracking().Where(c => categoryIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => new CategoryDto(c.Slug, c.Name, c.Kind), ct);
        var venues = await db.Venues.AsNoTracking().Where(v => venueIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => v.Name, ct);
        var sources = await db.Sources.AsNoTracking().Where(s => sourceIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        return events.Select(e => new EventSummaryDto(
            e.Id, e.Title, e.StartsAt, e.StartTimeKnown, e.EndsAt, cities[e.CityId], e.VenueId is { } v ? venues.GetValueOrDefault(v) : null,
            e.CategoryId is { } c ? categories.GetValueOrDefault(c) : null, e.PriceText, e.PriceAmount, e.Currency, e.ImageUrl,
            sources[e.PrimarySourceId], e.SourceUrl, e.LastVerifiedAt,
            ContentRules.IsFresh(e.LastVerifiedAt, now) ? "fresh" : "stale", e.IsActive(now) ? "active" : "expired", e.IsSynthetic)).ToList();
    }
}
