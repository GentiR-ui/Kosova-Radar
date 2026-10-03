using System.Globalization;
using System.Text;

namespace App.Domain.Text;

/// <summary>
/// One normalization for matching, deduplication and search (design.md §4): lower-case, Albanian/Latin diacritics
/// folded (ë→e, ç→c, č/ć→c, š→s, ž→z, đ→d), punctuation removed, whitespace collapsed.
/// </summary>
public static class TextNormalizer
{
    public static string Fold(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var decomposed = text.Normalize(NormalizationForm.FormKD).ToLowerInvariant();
        var sb = new StringBuilder(decomposed.Length);
        var pendingSpace = false;
        foreach (var ch in decomposed)
        {
            var c = ch switch { 'đ' => 'd', 'ð' => 'd', 'ł' => 'l', 'ø' => 'o', 'ß' => 's', _ => ch };
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark) continue;
            if (char.IsLetterOrDigit(c))
            {
                if (pendingSpace && sb.Length > 0) sb.Append(' ');
                pendingSpace = false;
                sb.Append(c);
            }
            else
            {
                pendingSpace = true; // whitespace and punctuation both separate words
            }
        }
        return sb.ToString();
    }

    /// <summary>URL slug: folded words joined with "-".</summary>
    public static string Slug(string? text) => Fold(text).Replace(' ', '-');
}
