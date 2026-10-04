using App.Application.Ingestion;
using App.Domain.Content;
using App.Domain.Reference;

namespace App.Application.Ai;

/// <summary>What <see cref="AiFieldMerger.Merge"/> decided: the fields, which of them the AI filled and which values disagreed with the source.</summary>
public sealed record AiMergeResult(CandidateFields Fields, IReadOnlyList<string> Suggested, IReadOnlyList<Issue> Conflicts);

/// <summary>
/// Applies a validated AI answer to a candidate's fields (REQ-040, 042). The AI only fills fields that are still null;
/// a value the source states always wins, and a different AI value is reported as <c>ConflictingData</c>.
/// Event fields: startsAt, endsAt, city, venue, price text/amount. Deal fields: merchant, price text, old/new price,
/// validFrom/validUntil, city. Free-text values (venue, merchant, price text) are never compared.
/// </summary>
public static class AiFieldMerger
{
    public static AiMergeResult Merge(ItemKind kind, CandidateFields source, AiExtractedFields ai, ReferenceResolver resolver)
    {
        var suggested = new List<string>();
        var conflicts = new List<Issue>();
        var f = source;

        void Conflict(string field) => conflicts.Add(new(IssueCodes.ConflictingData, IssueSeverity.Review, field,
            $"The text suggests a different {field} than the source states; the source value was kept."));

        string? FillText(string? current, string? suggestion, string name)
        {
            if (current is null && !string.IsNullOrWhiteSpace(suggestion)) { suggested.Add(name); return suggestion.Trim(); }
            return current;
        }

        // Instants and amounts are compared by value: the same instant in another offset is no conflict.
        T? FillOrCompare<T>(T? current, T? suggestion, string name) where T : struct, IEquatable<T>
        {
            if (current is not null && suggestion is not null && !current.Value.Equals(suggestion.Value)) Conflict(name);
            if (current is null && suggestion is not null) { suggested.Add(name); return suggestion; }
            return current;
        }

        if (f.CityText is null)
        {
            var filled = FillText(null, ai.City, "cityText");
            var slug = resolver.CitySlug(filled);
            f = f with { CityText = filled, CitySlug = slug };
            if (slug is not null) suggested.Add("citySlug");
        }
        else if (resolver.CitySlug(ai.City) is { } aiSlug && f.CitySlug is { } sourceSlug && aiSlug != sourceSlug)
        {
            Conflict("citySlug");
        }

        var before = conflicts.Count;
        f = kind == ItemKind.Event
            ? f with
            {
                StartsAt = FillOrCompare(f.StartsAt, ai.StartsAt, "startsAt"),
                EndsAt = FillOrCompare(f.EndsAt, ai.EndsAt, "endsAt"),
                VenueName = FillText(f.VenueName, ai.Venue, "venueName"),
                PriceText = FillText(f.PriceText, ai.PriceText, "priceText"),
                PriceAmount = FillOrCompare(f.PriceAmount, ai.PriceAmount, "priceAmount"),
            }
            : f with
            {
                MerchantName = FillText(f.MerchantName, ai.Merchant, "merchantName"),
                PriceText = FillText(f.PriceText, ai.PriceText, "priceText"),
                OldPrice = FillOrCompare(f.OldPrice, ai.OldPrice, "oldPrice"),
                NewPrice = FillOrCompare(f.NewPrice, ai.NewPrice, "newPrice"),
                ValidFrom = FillOrCompare(f.ValidFrom, ai.ValidFrom, "validFrom"),
                ValidUntil = FillOrCompare(f.ValidUntil, ai.ValidUntil, "validUntil"),
            };

        // A currency is never assumed: it is taken from the AI only while no value disagreed with the source
        // and there is a price it belongs to.
        var hasPrice = f.PriceAmount is not null || f.OldPrice is not null || f.NewPrice is not null;
        if (f.Currency is null && ai.Currency is not null && hasPrice && conflicts.Count == before)
        {
            suggested.Add("currency");
            f = f with { Currency = ai.Currency };
        }

        return new(f, suggested, conflicts);
    }
}
