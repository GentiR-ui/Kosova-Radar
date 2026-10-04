using App.Application.Abstractions;
using App.Application.Audit;
using App.Domain.Content;
using App.Domain.Reference;
using App.Domain.Text;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Candidates;

public enum ApprovalStatus { Done, Invalid, NotFound, Conflict }

public sealed record ApprovalResult(ApprovalStatus Status, ItemKind? Kind = null, Guid? ItemId = null, IReadOnlyDictionary<string, string[]>? Errors = null);

/// <summary>Approve a candidate and publish it as an event or deal (REQ-062, 063, 067). Publishing, candidate update and audit commit together.</summary>
public sealed class CandidateApproval(IAppDbContext db, IAuditWriter audit, IClock clock)
{
    public async Task<ApprovalResult> ApproveAsync(Guid id, CancellationToken ct)
    {
        var c = await db.Candidates.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return new(ApprovalStatus.NotFound);
        if (!CandidateDecisions.IsOpen(c)) return new(ApprovalStatus.Conflict);

        var f = c.Fields;
        var errors = MissingFields(c.Kind, f);
        // Ingested fields were never run through the edit rules: lengths, currency and price ranges must fit the columns.
        foreach (var (key, messages) in CandidateRequestValidator.Validate(ToRequest(f)).Errors) errors.TryAdd(key, messages);
        if (c.Validation == ValidationOutcome.Rejected) errors["validation"] = ["The candidate is rejected by validation."];

        var cityId = f.CitySlug is null ? null : await db.Cities.Where(x => x.Slug == f.CitySlug).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        if (f.CitySlug is not null && cityId is null) errors["citySlug"] = ["Unknown city."];
        if (errors.Count > 0) return new(ApprovalStatus.Invalid, Errors: errors);

        // Own category first, then the AI suggestion (design.md §5); an unknown own slug is only reachable via ingested data and stays empty.
        var categoryId = f.CategorySlug is null
            ? c.SuggestedCategoryId
            : await db.Categories.Where(x => x.Slug == f.CategorySlug && x.Kind == c.Kind).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);

        var now = clock.UtcNow;
        Guid itemId;
        if (c.Kind == ItemKind.Event)
        {
            var venueId = await FindOrCreateVenueAsync(f.VenueName, cityId!.Value, ct);
            var e = new Event
            {
                Title = f.Title!,
                Description = f.Description,
                VenueId = venueId,
                CityId = cityId.Value,
                CategoryId = categoryId,
                StartsAt = f.StartsAt!.Value.ToUniversalTime(),
                EndsAt = f.EndsAt?.ToUniversalTime(),
                PriceText = f.PriceText,
                PriceAmount = f.PriceAmount,
                Currency = f.Currency,
                ImageUrl = f.ImageUrl,
                Status = ItemStatus.Published,
                PrimarySourceId = c.SourceId,
                SourceUrl = f.Url!,
                Fingerprint = c.Fingerprint,
                IsSynthetic = c.IsSynthetic,
                SearchText = TextNormalizer.Fold($"{f.Title} {f.Description} {f.VenueName}"),
                LastVerifiedAt = c.FetchedAt,
                PublishedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.Events.Add(e);
            itemId = e.Id;
        }
        else
        {
            var d = new Deal
            {
                Title = f.Title!,
                Description = f.Description,
                MerchantName = f.MerchantName,
                CityId = cityId,
                CategoryId = categoryId,
                OldPrice = f.OldPrice,
                NewPrice = f.NewPrice,
                PriceText = f.PriceText,
                Currency = f.Currency,
                DiscountPercent = ContentRules.DiscountPercent(f.OldPrice, f.NewPrice),
                ValidFrom = f.ValidFrom?.ToUniversalTime(),
                ValidUntil = f.ValidUntil?.ToUniversalTime(),
                ImageUrl = f.ImageUrl,
                Status = ItemStatus.Published,
                PrimarySourceId = c.SourceId,
                SourceUrl = f.Url!,
                Fingerprint = c.Fingerprint,
                IsSynthetic = c.IsSynthetic,
                SearchText = TextNormalizer.Fold($"{f.Title} {f.Description} {f.MerchantName}"),
                LastVerifiedAt = c.FetchedAt,
                PublishedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.Deals.Add(d);
            itemId = d.Id;
        }

        db.ItemSources.Add(new ItemSource
        {
            ItemKind = c.Kind,
            ItemId = itemId,
            SourceId = c.SourceId,
            SourceUrl = f.Url!,
            Fingerprint = c.Fingerprint,
            CandidateId = c.Id,
            AddedAt = now,
        });

        var before = new { status = c.Status, publishedItemId = c.PublishedItemId };
        c.Status = CandidateStatus.Published;
        c.PublishedItemId = itemId;
        c.UpdatedAt = now;
        audit.Record(AuditActions.CandidateApprove, AuditEntityTypes.Candidate, c.Id, before,
            new { status = c.Status, publishedItemId = itemId, kind = c.Kind });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Decided concurrently (xmin), or a parallel approval created the same venue first: the reviewer retries.
            return new(ApprovalStatus.Conflict);
        }
        return new(ApprovalStatus.Done, c.Kind, itemId);
    }

    private static CandidateFieldsRequest ToRequest(CandidateFields f) =>
        new(f.Title, f.Description, f.Url, f.ImageUrl, f.CitySlug, f.VenueName, f.CategorySlug, f.StartsAt, f.EndsAt, f.PriceText,
            f.PriceAmount, f.Currency, f.MerchantName, f.OldPrice, f.NewPrice, f.StatedDiscountPercent, f.ValidFrom, f.ValidUntil);

    /// <summary>Required fields by kind (design.md §5); the keys are the field names.</summary>
    private static Dictionary<string, string[]> MissingFields(ItemKind kind, CandidateFields f)
    {
        var errors = new Dictionary<string, string[]>();
        void Require(bool present, string key, string message) { if (!present) errors[key] = [message]; }
        Require(!string.IsNullOrWhiteSpace(f.Title), "title", "Title is required.");
        Require(!string.IsNullOrWhiteSpace(f.Url), "url", "Url is required.");
        if (kind == ItemKind.Event)
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

    private async Task<Guid?> FindOrCreateVenueAsync(string? name, Guid cityId, CancellationToken ct)
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
