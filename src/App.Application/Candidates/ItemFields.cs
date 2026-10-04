using App.Application.Abstractions;
using App.Domain.Content;
using App.Domain.Text;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Candidates;

/// <summary>Copies normalized fields onto a published event or deal; shared by approval and the published-item edit.</summary>
internal static class ItemFields
{
    public static void ApplyEvent(Event e, CandidateFields f, Guid cityId, Guid? venueId, Guid? categoryId, DateTimeOffset now)
    {
        e.Title = f.Title!;
        e.Description = f.Description;
        e.VenueId = venueId;
        e.CityId = cityId;
        e.CategoryId = categoryId;
        e.StartsAt = f.StartsAt!.Value.ToUniversalTime();
        e.EndsAt = f.EndsAt?.ToUniversalTime();
        e.PriceText = f.PriceText;
        e.PriceAmount = f.PriceAmount;
        e.Currency = f.Currency;
        e.ImageUrl = f.ImageUrl;
        e.SourceUrl = f.Url!;
        e.SearchText = TextNormalizer.Fold($"{f.Title} {f.Description} {f.VenueName}");
        e.UpdatedAt = now;
    }

    public static void ApplyDeal(Deal d, CandidateFields f, Guid? cityId, Guid? categoryId, DateTimeOffset now)
    {
        d.Title = f.Title!;
        d.Description = f.Description;
        d.MerchantName = f.MerchantName;
        d.CityId = cityId;
        d.CategoryId = categoryId;
        d.OldPrice = f.OldPrice;
        d.NewPrice = f.NewPrice;
        d.PriceText = f.PriceText;
        d.Currency = f.Currency;
        d.DiscountPercent = ContentRules.DiscountPercent(f.OldPrice, f.NewPrice);
        d.ValidFrom = f.ValidFrom?.ToUniversalTime();
        d.ValidUntil = f.ValidUntil?.ToUniversalTime();
        d.ImageUrl = f.ImageUrl;
        d.SourceUrl = f.Url!;
        d.SearchText = TextNormalizer.Fold($"{f.Title} {f.Description} {f.MerchantName}");
        d.UpdatedAt = now;
    }

    /// <summary>Required fields by kind (design.md §5); the keys are the field names.</summary>
    public static Dictionary<string, string[]> MissingFields(Domain.Reference.ItemKind kind, CandidateFields f)
    {
        var errors = new Dictionary<string, string[]>();
        void Require(bool present, string key, string message) { if (!present) errors[key] = [message]; }
        Require(!string.IsNullOrWhiteSpace(f.Title), "title", "Title is required.");
        Require(!string.IsNullOrWhiteSpace(f.Url), "url", "Url is required.");
        if (kind == Domain.Reference.ItemKind.Event)
        {
            Require(!string.IsNullOrWhiteSpace(f.CitySlug), "citySlug", "A known city is required.");
            Require(f.StartsAt is not null, "startsAt", "Start time is required.");
        }
        else
        {
            Require(f.NewPrice is not null || !string.IsNullOrWhiteSpace(f.PriceText), "newPrice", "A new price or a price text is required.");
        }
        return errors;
    }

    public static async Task<Guid?> FindOrCreateVenueAsync(IAppDbContext db, string? name, Guid cityId, CancellationToken ct)
    {
        var normalized = TextNormalizer.Fold(name);
        if (normalized.Length == 0) return null;
        var existing = await db.Venues.Where(v => v.CityId == cityId && v.NormalizedName == normalized).Select(v => (Guid?)v.Id).FirstOrDefaultAsync(ct);
        if (existing is not null) return existing;
        var venue = new Venue { Name = name!.Trim(), NormalizedName = normalized, CityId = cityId };
        db.Venues.Add(venue);
        return venue.Id;
    }
}
