using System.Globalization;
using System.Text.RegularExpressions;

namespace App.Infrastructure.Ingestion;

/// <summary>
/// Prices as Kosovo online shops print them ("1.299,00 €", "89,00 €", "€ 12,5", "59.50"): the decimal comma and the point
/// as thousands separator, a currency sign before or after. Shared by the shop deal parsers (design.md §6).
/// </summary>
public static partial class ShopPrice
{
    [GeneratedRegex(@"\d[\d.,\s ]*\d|\d")]
    private static partial Regex Number();

    /// <summary>The first amount in <paramref name="text"/>, or null when there is none or it is not a plain price.</summary>
    public static decimal? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var match = Number().Match(text);
        if (!match.Success) return null;

        var raw = Regex.Replace(match.Value, @"[\s ]", "");
        var comma = raw.LastIndexOf(',');
        var point = raw.LastIndexOf('.');
        string normalized;
        if (comma >= 0 && point >= 0)
        {
            // Both present: the one that comes last is the decimal separator.
            var (decimalAt, thousands) = comma > point ? (comma, '.') : (point, ',');
            normalized = raw[..decimalAt].Replace(thousands.ToString(), "") + "." + raw[(decimalAt + 1)..];
        }
        else if (comma >= 0 || point >= 0)
        {
            var at = Math.Max(comma, point);
            var digitsAfter = raw.Length - at - 1;
            var single = raw.Count(c => c is ',' or '.') == 1;
            // "1.299" / "1,299" with exactly three digits after a single separator is a thousands group; "59,5" / "89.50" is not.
            normalized = single && digitsAfter != 3 ? raw.Replace(',', '.') : raw.Replace(",", "").Replace(".", "");
        }
        else
        {
            normalized = raw;
        }

        return decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) && value >= 0
            ? value
            : null;
    }
}
