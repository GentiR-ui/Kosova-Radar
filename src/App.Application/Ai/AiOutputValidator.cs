using System.Text.RegularExpressions;
using App.Application.Ingestion;

namespace App.Application.Ai;

/// <summary>
/// Server-side check of an AI answer against the extraction schema and the domain rules (design.md §6): the model is
/// never the authority. Any violation makes the whole answer unusable (REQ-041).
/// </summary>
public static partial class AiOutputValidator
{
    private const int MaxTextLength = 300;
    private const decimal MaxPrice = 1_000_000m;
    private static readonly DateTimeOffset MinDate = new(1900, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset MaxDate = new(2200, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Null when the answer is acceptable, else the name of the first violating field.</summary>
    public static string? Validate(AiExtractionResult r, IReadOnlyCollection<string> categorySlugs)
    {
        if (string.IsNullOrWhiteSpace(r.Model) || r.Model.Length > 120 || !RadarFeedParser.IsStorableText(r.Model)) return "model";
        if (r.RequestId is { } rid && (rid.Length > 200 || !RadarFeedParser.IsStorableText(rid))) return "requestId";
        if (double.IsNaN(r.Confidence) || r.Confidence is < 0 or > 1) return "confidence";
        if (r.CategorySlug is { } slug && !categorySlugs.Contains(slug)) return "categorySlug";

        var f = r.Fields;
        foreach (var (name, value) in new[]
                 {
                     ("city", f.City), ("venue", f.Venue), ("priceText", f.PriceText), ("merchant", f.Merchant),
                 })
            if (value is not null && (value.Length > MaxTextLength || value.Any(char.IsControl) || !RadarFeedParser.IsStorableText(value))) return name;

        foreach (var (name, value) in new[]
                 {
                     ("startsAt", f.StartsAt), ("endsAt", f.EndsAt), ("validFrom", f.ValidFrom), ("validUntil", f.ValidUntil),
                 })
            if (value is { } d && (d < MinDate || d >= MaxDate)) return name;

        foreach (var (name, value) in new[]
                 {
                     ("priceAmount", f.PriceAmount), ("oldPrice", f.OldPrice), ("newPrice", f.NewPrice),
                 })
            if (value is < 0 or > MaxPrice) return name;

        if (f.Currency is { } c && !CurrencyPattern().IsMatch(c)) return "currency";
        return null;
    }

    [GeneratedRegex("^[A-Z]{3}$", RegexOptions.None, matchTimeoutMilliseconds: 100)]
    private static partial Regex CurrencyPattern();
}
