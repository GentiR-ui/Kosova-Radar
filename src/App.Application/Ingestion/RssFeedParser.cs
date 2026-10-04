using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using App.Domain.Content;
using App.Domain.Reference;

namespace App.Application.Ingestion;

/// <summary>
/// Parses RSS 2.0 and Atom 1.0 (design.md §6). Every item becomes a candidate of the source's default kind with the
/// title, link and HTML-stripped text; start time, city and prices are left to the AI step.
/// </summary>
public static partial class RssFeedParser
{
    public const int MaxPayloadChars = 5 * 1024 * 1024;
    public const int MaxTextLength = 20_000;
    public const int MaxDepth = 32;
    public const int MaxItems = 1000;

    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace ContentNs = "http://purl.org/rss/1.0/modules/content/";

    [GeneratedRegex(@"<(script|style)\b[^>]*>.*?</\1\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase, 200)]
    private static partial Regex ScriptOrStyle();

    [GeneratedRegex(@"<[^>]*>", RegexOptions.None, 200)]
    private static partial Regex Tag();

    [GeneratedRegex(@"\s+", RegexOptions.None, 200)]
    private static partial Regex Whitespace();

    public static FeedParseResult Parse(Guid sourceId, ItemKind defaultKind, string payload)
    {
        if (string.IsNullOrWhiteSpace(payload) || payload.Length > MaxPayloadChars) return FeedParseResult.Malformed;

        XDocument doc;
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaxPayloadChars,
            };
            // Depth is checked on a cheap streaming pass: building (and reading .Value of) a deeply nested tree is slow and recursive.
            using (var probe = XmlReader.Create(new StringReader(payload), settings))
            {
                while (probe.Read())
                    if (probe.Depth > MaxDepth) return FeedParseResult.Malformed;
            }

            using var reader = XmlReader.Create(new StringReader(payload), settings);
            doc = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            return FeedParseResult.Malformed;
        }

        var root = doc.Root;
        IEnumerable<XElement> items;
        bool atom;
        if (root is { Name.LocalName: "rss", Name.NamespaceName: "" })
        {
            items = root.Element("channel")?.Elements("item") ?? [];
            atom = false;
        }
        else if (root?.Name == Atom + "feed")
        {
            items = root.Elements(Atom + "entry");
            atom = true;
        }
        else
        {
            return FeedParseResult.Malformed;
        }

        var parsed = new List<NormalizedCandidate>();
        var failures = new List<ItemFailure>();
        var index = 0;
        foreach (var item in items)
        {
            if (index == MaxItems)
            {
                failures.Add(new ItemFailure(index, "TooManyItems"));
                index = items.Count();
                break;
            }

            var (candidate, failure) = ParseItem(sourceId, defaultKind, item, atom);
            if (candidate is not null) parsed.Add(candidate);
            else failures.Add(new ItemFailure(index, failure!));
            index++;
        }

        return new FeedParseResult(index, parsed, failures);
    }

    private static (NormalizedCandidate?, string?) ParseItem(Guid sourceId, ItemKind kind, XElement item, bool atom)
    {
        var evidence = item.ToString(SaveOptions.DisableFormatting);
        if (Encoding.UTF8.GetByteCount(evidence) > RadarFeedParser.MaxItemBytes) return (null, "TooLarge");

        var ns = atom ? Atom : XNamespace.None;
        var link = atom ? AtomLink(item) : Clean(item.Element("link")?.Value);
        if (link is not null && (link.Length > RadarFeedParser.MaxUrlLength || !IsHttpUrl(link)))
            return (null, "InvalidField:url");

        var id = Clean(item.Element(ns + (atom ? "id" : "guid"))?.Value) ?? link;
        if (id is { Length: > RadarFeedParser.MaxExternalIdLength }) return (null, "InvalidField:externalId");

        var fingerprint = ItemIdentity.Fingerprint(sourceId, id, link);
        if (fingerprint is null) return (null, "MissingIdentity");

        string? text, title;
        try
        {
            // The first non-blank body wins, so an empty content element does not hide the summary.
            text = (atom ? new[] { item.Element(Atom + "content"), item.Element(Atom + "summary") }
                    : [item.Element(ContentNs + "encoded"), item.Element("description")])
                .Select(e => StripHtml(e?.Value)).FirstOrDefault(t => t is not null);
            title = StripHtml(item.Element(ns + "title")?.Value);
        }
        catch (RegexMatchTimeoutException)
        {
            return (null, "InvalidField:text");
        }

        if (text is { Length: > MaxTextLength })
        {
            var cut = char.IsHighSurrogate(text[MaxTextLength - 1]) ? MaxTextLength - 1 : MaxTextLength;
            text = text[..cut];
        }

        // Entity decoding can produce NUL or lone surrogates (e.g. &amp;#0;) that PostgreSQL cannot store.
        if (title is not null && !RadarFeedParser.IsStorableText(title)) return (null, "InvalidField:title");
        if (text is not null && !RadarFeedParser.IsStorableText(text)) return (null, "InvalidField:text");

        var fields = new CandidateFields { Title = title, Url = link };

        return (new NormalizedCandidate(kind, id, link ?? string.Empty, fingerprint,
            ItemIdentity.ContentHash(kind, fields, text, evidence), fields, text, evidence), null);
    }

    /// <summary>The alternate link (no <c>rel</c> counts as alternate); other relations such as <c>self</c> are not the item page.</summary>
    private static string? AtomLink(XElement entry) =>
        Clean(entry.Elements(Atom + "link")
            .FirstOrDefault(l => (string?)l.Attribute("rel") is null or "alternate")?.Attribute("href")?.Value);

    private static bool IsHttpUrl(string s) =>
        Uri.TryCreate(s, UriKind.Absolute, out var u) && u.Scheme is "http" or "https";

    private static string? Clean(string? s)
    {
        var t = s?.Trim();
        return string.IsNullOrEmpty(t) ? null : t;
    }

    /// <summary>Removes tags (and script/style content), decodes entities and collapses whitespace; blank → null.</summary>
    private static string? StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;
        var text = Tag().Replace(ScriptOrStyle().Replace(html, " "), " ");
        text = Whitespace().Replace(WebUtility.HtmlDecode(text), " ").Trim();
        return text.Length == 0 ? null : text;
    }
}
