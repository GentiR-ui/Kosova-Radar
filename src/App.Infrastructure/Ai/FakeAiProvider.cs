using System.Globalization;
using System.Text.RegularExpressions;
using App.Application.Ai;
using App.Domain.Content;
using App.Domain.Reference;
using App.Domain.Text;

namespace App.Infrastructure.Ai;

/// <summary>
/// Deterministic provider for Development and Testing (design.md §6 "Fake provider"; the acceptance tests rely on these rules).
/// <c>#ai-fail</c> in the text throws, <c>#ai-invalid</c> returns an answer that violates the extraction schema.
/// </summary>
internal sealed partial class FakeAiProvider : IAiProvider
{
    public const string ModelName = "fake-1";

    // Folded, space-padded variants of every seeded city: " mitrovice e veriut ", " mitrovice ", ... (whole-word search).
    private static readonly (string Variant, string Name)[] CityVariants = ReferenceData.Cities
        .SelectMany(c => c.Aliases.Prepend(c.Slug).Prepend(c.Name).Select(v => (Variant: $" {TextNormalizer.Fold(v)} ", c.Name)))
        .ToArray();

    public string Name => "Fake";
    public bool IsEnabled => true;

    public Task<AiExtractionResult> ExtractAsync(AiExtractionRequest request, CancellationToken ct)
    {
        var text = request.Text;
        if (text.Contains("#ai-fail", StringComparison.Ordinal))
            throw new InvalidOperationException("Simulated AI provider failure.");

        var price = FindPrice(text);
        var isEvent = request.Kind == ItemKind.Event;
        var fields = new AiExtractedFields
        {
            StartsAt = FindStart(text),
            City = FindCity(text),
            Currency = price is null ? null : "EUR",
            PriceAmount = isEvent ? price : null,
            NewPrice = isEvent ? null : price,
        };
        var result = new AiExtractionResult(ModelName, "fake-" + Guid.NewGuid(), 0.9, fields, FindCategory(request.Kind, text));

        // Confidence outside 0..1 and a negative price: both break the extraction schema.
        return Task.FromResult(text.Contains("#ai-invalid", StringComparison.Ordinal)
            ? result with { Confidence = 7, Fields = fields with { PriceAmount = -1 } }
            : result);
    }

    private static DateTimeOffset? FindStart(string text)
    {
        var m = IsoDateTime().Match(text);
        return m.Success && DateTimeOffset.TryParse(m.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ? start : null;
    }

    private static decimal? FindPrice(string text)
    {
        var m = EuroPrice().Match(text);
        return m.Success && decimal.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var p) ? p : null;
    }

    // The longest matching city wins ("Mitrovicë e Veriut" over "Mitrovicë"), then the earliest position.
    private static string? FindCity(string text)
    {
        var folded = $" {TextNormalizer.Fold(text)} ";
        return CityVariants
            .Select(v => (v.Name, v.Variant, At: folded.IndexOf(v.Variant, StringComparison.Ordinal)))
            .Where(m => m.At >= 0)
            .OrderByDescending(m => m.Variant.Length).ThenBy(m => m.At)
            .Select(m => m.Name)
            .FirstOrDefault();
    }

    private static string? FindCategory(ItemKind kind, string text)
    {
        var folded = TextNormalizer.Fold(text);
        bool Has(params string[] parts) => parts.Any(folded.Contains);
        if (kind == ItemKind.Event)
            return Has("koncert", "muzik") ? "muzike" : Has("teat", "film") ? "teater" : Has("ndeshje", "sport") ? "sport" : null;
        return Has("laptop", "telefon") ? "teknologji" : null;
    }

    [GeneratedRegex(@"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?(?:Z|[+-]\d{2}:\d{2})", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex IsoDateTime();

    [GeneratedRegex(@"(\d+(?:[.,]\d{1,2})?)\s?€", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex EuroPrice();
}
