using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using App.Application.Public;

namespace App.Api.Http;

/// <summary>HTML head injection, sitemap and robots.txt (design.md §5 "SEO"). All item text is encoded for its context.</summary>
public static partial class SeoPages
{
    private const string SiteName = "Kosova Radar";
    private static readonly TimeZoneInfo Pristina = TimeZoneInfo.FindSystemTimeZoneById("Europe/Belgrade");

    /// <summary>Minimal shell used when no SPA build is present.</summary>
    public const string BuiltInShell =
        "<!doctype html><html lang=\"sq\"><head><meta charset=\"UTF-8\" /><meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\" />"
        + "<title>Kosova Radar</title></head><body><div id=\"root\"></div></body></html>";

    [GeneratedRegex("<title>.*?</title>|<meta\\s+name=\"description\"[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 200)]
    private static partial Regex ReplacedHeadTags();

    public static string EventPage(string shell, EventDetailDto e, string baseUrl)
    {
        var description = Truncate(e.Description) ?? Join(Format(e.StartsAt, e.StartTimeKnown), e.City.Name);
        var json = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "Event",
            ["name"] = e.Title,
            ["startDate"] = e.StartTimeKnown ? e.StartsAt.ToString("O", CultureInfo.InvariantCulture) : Format(e.StartsAt, "yyyy-MM-dd"),
            ["location"] = new Dictionary<string, object?>
            {
                ["@type"] = "Place",
                ["name"] = e.VenueName ?? e.City.Name,
                ["address"] = new Dictionary<string, object?> { ["@type"] = "PostalAddress", ["addressLocality"] = e.City.Name },
            },
            ["url"] = $"{baseUrl}/events/{e.Id}",
        };
        if (e.EndsAt is { } end) json["endDate"] = end.ToString("O", CultureInfo.InvariantCulture);
        return Inject(shell, e.Title, description ?? "", $"{baseUrl}/events/{e.Id}", e.ImageUrl, e.IsSynthetic, json);
    }

    public static string DealPage(string shell, DealDetailDto d, string baseUrl)
    {
        var until = d.ValidUntil is { } v ? $"Vlen deri më {Format(v, false)}" : null;
        var description = Truncate(d.Description) ?? Join(d.MerchantName, until);
        return Inject(shell, d.Title, description ?? "", $"{baseUrl}/deals/{d.Id}", d.ImageUrl, d.IsSynthetic, null);
    }

    public static string Sitemap(string baseUrl, IEnumerable<Guid> eventIds, IEnumerable<Guid> dealIds)
    {
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\n");
        void Url(string path) => sb.Append("<url><loc>").Append(WebUtility.HtmlEncode(baseUrl + path)).Append("</loc></url>\n");
        Url("/"); Url("/events"); Url("/deals");
        foreach (var id in eventIds) Url($"/events/{id}");
        foreach (var id in dealIds) Url($"/deals/{id}");
        return sb.Append("</urlset>\n").ToString();
    }

    public static string Robots(string baseUrl) =>
        $"User-agent: *\nDisallow: /admin\nDisallow: /api/admin\nSitemap: {baseUrl}/sitemap.xml\n";

    private static string Inject(string shell, string title, string description, string url, string? imageUrl, bool noindex,
        Dictionary<string, object?>? jsonLd)
    {
        var enc = HtmlEncoder.Default;
        var head = new StringBuilder();
        head.Append($"<title>{enc.Encode(title)} · {SiteName}</title>\n");
        head.Append($"<meta name=\"description\" content=\"{enc.Encode(description)}\" />\n");
        head.Append($"<meta property=\"og:title\" content=\"{enc.Encode(title)}\" />\n");
        head.Append($"<meta property=\"og:description\" content=\"{enc.Encode(description)}\" />\n");
        head.Append($"<meta property=\"og:url\" content=\"{enc.Encode(url)}\" />\n");
        if (imageUrl is not null && imageUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            head.Append($"<meta property=\"og:image\" content=\"{enc.Encode(imageUrl)}\" />\n");
        if (noindex) head.Append("<meta name=\"robots\" content=\"noindex\" />\n");
        // The default JSON encoder escapes < > & and the apostrophe, so "</script>" cannot occur inside the block.
        if (jsonLd is not null)
            head.Append("<script type=\"application/ld+json\">").Append(JsonSerializer.Serialize(jsonLd)).Append("</script>\n");

        var stripped = ReplacedHeadTags().Replace(shell, "");
        var at = stripped.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        return at < 0 ? head + stripped : stripped.Insert(at, head.ToString());
    }

    /// <summary>Plain shell for a missing item, kept out of search indexes.</summary>
    public static string NoIndex(string shell)
    {
        var at = shell.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        return at < 0 ? shell : shell.Insert(at, "<meta name=\"robots\" content=\"noindex\" />\n");
    }

    private static string? Truncate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim();
        return t.Length <= 200 ? t : t[..200];
    }

    private static string? Join(string? a, string? b) => a is null ? b : b is null ? a : $"{a} · {b}";

    private static string Format(DateTimeOffset instant, bool time = true) =>
        Format(instant, time ? "dd.MM.yyyy, HH:mm" : "dd.MM.yyyy");

    private static string Format(DateTimeOffset instant, string pattern) =>
        TimeZoneInfo.ConvertTime(instant, Pristina).ToString(pattern, CultureInfo.InvariantCulture);
}
