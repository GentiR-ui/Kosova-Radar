using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using App.Domain.Content;
using App.Domain.Reference;

namespace App.Application.Ingestion;

/// <summary>
/// Parses the Radar JSON feed (design.md §6). Unknown values stay null; city and category slugs are resolved later
/// against reference data, so only the free-text fields are filled here.
/// </summary>
public static partial class RadarFeedParser
{
    public const int MaxItemBytes = 64 * 1024;
    public const int MaxExternalIdLength = 200;

    public const int MaxUrlLength = 2000;

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d{1,7})?)?(Z|[+-]\d{2}:\d{2})$")]
    private static partial Regex IsoWithOffset();

    public static FeedParseResult Parse(Guid sourceId, string payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return FeedParseResult.Malformed;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 256 });
        }
        catch (JsonException)
        {
            return FeedParseResult.Malformed;
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("items", out var items)
                || items.ValueKind != JsonValueKind.Array)
                return FeedParseResult.Malformed;

            var parsed = new List<NormalizedCandidate>();
            var failures = new List<ItemFailure>();
            var index = 0;
            foreach (var item in items.EnumerateArray())
            {
                var (candidate, failure) = ParseItem(sourceId, item);
                if (candidate is not null) parsed.Add(candidate);
                else failures.Add(new ItemFailure(index, failure!));
                index++;
            }

            return new FeedParseResult(index, parsed, failures);
        }
    }

    private static (NormalizedCandidate?, string?) ParseItem(Guid sourceId, JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object) return (null, "NotAnObject");

        var evidence = item.GetRawText();
        if (Encoding.UTF8.GetByteCount(evidence) > MaxItemBytes) return (null, "TooLarge");

        try
        {
            var kind = ReadKind(item);
            if (kind is null) return (null, "InvalidKind");

            var externalId = ReadString(item, "externalId");
            if (externalId is { Length: > MaxExternalIdLength }) return (null, "InvalidField:externalId");
            var url = ReadString(item, "url");
            if (url is { Length: > MaxUrlLength }) return (null, "InvalidField:url");
            var fingerprint = ItemIdentity.Fingerprint(sourceId, externalId, url);

            var isEvent = kind == ItemKind.Event;
            var price = ReadDecimal(item, "price");
            var oldPrice = ReadDecimal(item, "oldPrice");
            var newPrice = ReadDecimal(item, "newPrice");
            var discount = ReadInt(item, "discountPercent");
            var startsAt = ReadDate(item, "startsAt");
            var startDate = ReadDay(item, "startDate");
            var endsAt = ReadDate(item, "endsAt");
            var validFrom = ReadDate(item, "validFrom");
            var validUntil = ReadDate(item, "validUntil");
            var merchant = ReadString(item, "merchant");
            var venue = ReadString(item, "venue");

            var fields = new CandidateFields
            {
                Title = ReadString(item, "title"),
                Description = ReadString(item, "description"),
                Url = url,
                ImageUrl = ReadString(item, "imageUrl"),
                CityText = ReadString(item, "city"),
                CategoryText = ReadString(item, "category"),
                PriceText = ReadString(item, "priceText"),
                Currency = ReadString(item, "currency"),
                VenueName = isEvent ? venue : null,
                StartsAt = isEvent ? startsAt : null,
                StartDate = isEvent && startsAt is null ? startDate : null,
                EndsAt = isEvent ? endsAt : null,
                PriceAmount = isEvent ? price : null,
                MerchantName = isEvent ? null : merchant,
                OldPrice = isEvent ? null : oldPrice,
                NewPrice = isEvent ? null : newPrice,
                StatedDiscountPercent = isEvent ? null : discount,
                ValidFrom = isEvent ? null : validFrom,
                ValidUntil = isEvent ? null : validUntil,
            };
            var text = ReadString(item, "text");

            // Checked after the field types so that a wrong-typed field is reported as such.
            if (fingerprint is null) return (null, "MissingIdentity");

            return (new NormalizedCandidate(kind.Value, externalId, url ?? string.Empty, fingerprint,
                ItemIdentity.ContentHash(kind.Value, fields, text, evidence), fields, text, evidence), null);
        }
        catch (FieldException e)
        {
            return (null, $"InvalidField:{e.Field}");
        }
    }

    private static ItemKind? ReadKind(JsonElement item)
    {
        if (!item.TryGetProperty("kind", out var v) || v.ValueKind != JsonValueKind.String) return null;
        return v.GetString()?.Trim().ToLowerInvariant() switch
        {
            "event" => ItemKind.Event,
            "deal" => ItemKind.Deal,
            _ => null,
        };
    }

    /// <summary>Absent or JSON null → null (unknown); a non-string value is a failure; blank text counts as unknown.</summary>
    private static string? ReadString(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null) return null;
        if (v.ValueKind != JsonValueKind.String) throw new FieldException(name);
        string raw;
        try
        {
            raw = v.GetString()!;
        }
        catch (InvalidOperationException)
        {
            throw new FieldException(name); // not transcodable text (e.g. a lone surrogate escape)
        }

        if (!IsStorableText(raw)) throw new FieldException(name);
        var s = raw.Trim();
        return s.Length == 0 ? null : s;
    }

    /// <summary>PostgreSQL text/jsonb cannot hold NUL, and a lone surrogate is not valid Unicode; one such item must not abort the whole run.</summary>
    public static bool IsStorableText(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\0') return false;
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) i++;
            else if (char.IsSurrogate(c)) return false;
        }

        return true;
    }

    private static decimal? ReadDecimal(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null) return null;
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetDecimal(out var d)) throw new FieldException(name);
        return d;
    }

    private static int? ReadInt(JsonElement item, string name)
    {
        var d = ReadDecimal(item, name);
        if (d is null) return null;
        if (d != decimal.Truncate(d.Value) || d < int.MinValue || d > int.MaxValue) throw new FieldException(name);
        return (int)d.Value;
    }

    /// <summary>Strict ISO-8601 date-time with an explicit offset; anything else (no offset, time only, other formats) fails.</summary>
    private static DateTimeOffset? ReadDate(JsonElement item, string name)
    {
        var s = ReadString(item, name);
        if (s is null) return null;
        if (!IsoWithOffset().IsMatch(s)
            || !DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            throw new FieldException(name);
        return d;
    }

    private static DateOnly? ReadDay(JsonElement item, string name)
    {
        var s = ReadString(item, name);
        if (s is null) return null;
        if (!DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            || d.Year is < 1900 or > 2200)
            throw new FieldException(name);
        return d;
    }

    private sealed class FieldException(string field) : Exception
    {
        public string Field { get; } = field;
    }
}
