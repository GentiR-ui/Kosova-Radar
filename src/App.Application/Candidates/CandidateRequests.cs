using System.Text.RegularExpressions;
using App.Domain.Content;
using App.Domain.Reference;

namespace App.Application.Candidates;

/// <summary>design.md §5: <c>CandidateFields</c> without the read-only/derived members.</summary>
public sealed record CandidateFieldsRequest(
    string? Title, string? Description, string? Url, string? ImageUrl, string? CitySlug, string? VenueName,
    string? CategorySlug, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, string? PriceText, decimal? PriceAmount,
    string? Currency, string? MerchantName, decimal? OldPrice, decimal? NewPrice, int? StatedDiscountPercent,
    DateTimeOffset? ValidFrom, DateTimeOffset? ValidUntil, DateOnly? StartDate = null);

public sealed record RejectRequest(string? Reason);

public sealed record MergeRequest(string? TargetId);

/// <summary>Request rules of the candidate edit (REQ-065) and reject (REQ-074) calls.</summary>
public static partial class CandidateRequestValidator
{
    public const int MaxTitle = 300;
    public const int MaxDescription = 10_000;
    public const int MaxText = 300;
    public const int MaxUrl = 2000;
    public const int MaxReason = 500;
    public const decimal MaxPrice = 1_000_000m;

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex CurrencyPattern();

    /// <summary>Checks lengths and formats; reference slugs are checked by the caller against the database. Blank text becomes null.</summary>
    public static (CandidateFieldsRequest? Request, Dictionary<string, string[]> Errors) Validate(CandidateFieldsRequest? req)
    {
        var errors = new Dictionary<string, string[]>();
        if (req is null)
        {
            errors["request"] = ["A body is required."];
            return (null, errors);
        }

        var title = Text(req.Title, "title", MaxTitle, errors);
        if (title is null) errors.TryAdd("title", ["Title is required."]);
        var description = Text(req.Description, "description", MaxDescription, errors);
        var url = Url(req.Url, "url", errors);
        var imageUrl = Url(req.ImageUrl, "imageUrl", errors);
        var citySlug = Text(req.CitySlug, "citySlug", MaxText, errors);
        var venue = Text(req.VenueName, "venueName", MaxText, errors);
        var categorySlug = Text(req.CategorySlug, "categorySlug", MaxText, errors);
        var priceText = Text(req.PriceText, "priceText", MaxText, errors);
        var merchant = Text(req.MerchantName, "merchantName", MaxText, errors);

        var currency = req.Currency?.Trim();
        if (string.IsNullOrEmpty(currency)) currency = null;
        else if (!CurrencyPattern().IsMatch(currency)) errors["currency"] = ["Currency must be three upper-case letters."];

        if (req.StartDate is { Year: < 1900 or > 2200 })
            errors["startDate"] = ["The date must be between 1900 and 2200."];

        Price(req.PriceAmount, "priceAmount", errors);
        Price(req.OldPrice, "oldPrice", errors);
        Price(req.NewPrice, "newPrice", errors);
        if (req.StatedDiscountPercent is < 0 or > 100)
            errors["statedDiscountPercent"] = ["Discount must be between 0 and 100."];

        return errors.Count > 0 ? (null, errors) : (req with
        {
            Title = title,
            Description = description,
            Url = url,
            ImageUrl = imageUrl,
            CitySlug = citySlug,
            VenueName = venue,
            CategorySlug = categorySlug,
            PriceText = priceText,
            Currency = currency,
            MerchantName = merchant,
        }, errors);
    }

    /// <summary>The reason after trimming must be 1-500 characters.</summary>
    public static (string? Reason, Dictionary<string, string[]> Errors) ValidateReason(RejectRequest? req)
    {
        var errors = new Dictionary<string, string[]>();
        var reason = req?.Reason?.Trim();
        if (string.IsNullOrEmpty(reason)) errors["reason"] = ["A reason is required."];
        else if (reason.Length > MaxReason) errors["reason"] = [$"The reason must be at most {MaxReason} characters."];
        return (errors.Count > 0 ? null : reason, errors);
    }

    /// <summary>The fields a request may change; source text (city/category as written) stays, kind-foreign fields are cleared.</summary>
    public static CandidateFields Apply(ItemKind kind, CandidateFields current, CandidateFieldsRequest r)
    {
        var isEvent = kind == ItemKind.Event;
        return current with
        {
            Title = r.Title,
            Description = r.Description,
            Url = r.Url,
            ImageUrl = r.ImageUrl,
            CitySlug = r.CitySlug,
            CategorySlug = r.CategorySlug,
            PriceText = r.PriceText,
            PriceAmount = r.PriceAmount,
            Currency = r.Currency,
            VenueName = isEvent ? r.VenueName : null,
            StartsAt = isEvent ? r.StartsAt : null,
            StartDate = isEvent && r.StartsAt is null ? r.StartDate : null,
            EndsAt = isEvent ? r.EndsAt : null,
            MerchantName = isEvent ? null : r.MerchantName,
            OldPrice = isEvent ? null : r.OldPrice,
            NewPrice = isEvent ? null : r.NewPrice,
            StatedDiscountPercent = isEvent ? null : r.StatedDiscountPercent,
            ValidFrom = isEvent ? null : r.ValidFrom,
            ValidUntil = isEvent ? null : r.ValidUntil,
        };
    }

    private static string? Text(string? value, string field, int max, Dictionary<string, string[]> errors)
    {
        var t = value?.Trim();
        if (string.IsNullOrEmpty(t)) return null;
        if (t.Length > max) errors[field] = [$"Must be at most {max} characters."];
        else if (field != "description" && t.Any(char.IsControl)) errors[field] = ["Must not contain control characters."];
        return t;
    }

    private static string? Url(string? value, string field, Dictionary<string, string[]> errors)
    {
        var t = Text(value, field, MaxUrl, errors);
        if (t is not null && !errors.ContainsKey(field) && !CandidateValidator.IsHttpUrl(t))
            errors[field] = ["Must be an absolute http(s) URL."];
        return t;
    }

    private static void Price(decimal? value, string field, Dictionary<string, string[]> errors)
    {
        if (value is < 0 or > MaxPrice) errors[field] = [$"Must be between 0 and {MaxPrice:0}."];
    }
}
