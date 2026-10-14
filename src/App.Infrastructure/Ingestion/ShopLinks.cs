using System.Text.RegularExpressions;

namespace App.Infrastructure.Ingestion;

/// <summary>Product links of the shop deal parsers (design.md §6).</summary>
internal static class ShopLinks
{
    /// <summary>
    /// The path of a product link on the shop's own host, else null. Root-relative links are relative on every OS (a rooted
    /// path parses as an absolute <c>file</c> URI on Linux, so it is decided before <see cref="Uri"/> sees it); protocol-relative
    /// and non-http(s) links are refused. A null <paramref name="pageUrl"/> skips the host check (the caller repeats it in Map).
    /// </summary>
    public static string? ProductPath(string? href, Uri? pageUrl, Regex productPath)
    {
        if (string.IsNullOrWhiteSpace(href)) return null;
        href = href.Trim();
        string path;
        if (href.StartsWith("//", StringComparison.Ordinal)) return null;
        if (href.StartsWith('/'))
        {
            path = href.Split('?', '#')[0];
        }
        else if (Uri.TryCreate(href, UriKind.Absolute, out var abs) && abs.Scheme is "http" or "https")
        {
            if (pageUrl is not null && !abs.Host.Equals(pageUrl.Host, StringComparison.OrdinalIgnoreCase)) return null;
            path = abs.AbsolutePath;
        }
        else
        {
            return null;
        }
        return productPath.IsMatch(path) ? path : null;
    }
}
