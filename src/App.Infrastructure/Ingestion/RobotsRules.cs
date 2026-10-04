namespace App.Infrastructure.Ingestion;

/// <summary>
/// The robots.txt rules that apply to one crawler (RFC 9309): the group naming our product token, else the <c>*</c>
/// group; the longest matching pattern wins and <c>Allow</c> wins a tie; <c>*</c> and a trailing <c>$</c> are wildcards.
/// </summary>
public sealed class RobotsRules
{
    /// <summary>Product token of the crawler (the User-Agent is <c>KosovaRadarBot/1.0 (+…)</c>).</summary>
    public const string AgentToken = "KosovaRadarBot";

    private readonly List<(string Pattern, bool Allow)> _rules;

    private RobotsRules(List<(string, bool)> rules) => _rules = rules;

    public static RobotsRules AllowAll { get; } = new([]);

    // A hostile file must not cost memory or CPU: RFC 9309 asks crawlers to read at least 500 KiB; the rest is ignored.
    private const int MaxChars = 512_000;
    private const int MaxLineChars = 4096;
    private const int MaxRulesPerGroup = 2000;

    public static RobotsRules Parse(string text)
    {
        var own = new List<(string, bool)>();
        var any = new List<(string, bool)>();
        var ownSeen = false;
        // The group being read: which of the two lists collect its rules (one flag each, so a rule is stored once).
        bool collectsOwn = false, collectsAny = false, lastWasAgent = false;

        foreach (var raw in (text.Length > MaxChars ? text[..MaxChars] : text).Split(['\r', '\n']))
        {
            if (raw.Length > MaxLineChars) { lastWasAgent = false; continue; }
            var line = raw.Trim().TrimStart('﻿');
            var hash = line.IndexOf('#');
            if (hash >= 0) line = line[..hash];
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var field = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();

            if (field.Equals("user-agent", StringComparison.OrdinalIgnoreCase))
            {
                if (!lastWasAgent) collectsOwn = collectsAny = false;
                lastWasAgent = true;
                var name = value.Split('/', ' ')[0];
                if (name == "*") collectsAny = true;
                else if (name.Equals(AgentToken, StringComparison.OrdinalIgnoreCase))
                {
                    collectsOwn = true;
                    ownSeen = true;
                }
            }
            else if (field.Equals("allow", StringComparison.OrdinalIgnoreCase) || field.Equals("disallow", StringComparison.OrdinalIgnoreCase))
            {
                lastWasAgent = false;
                if (value.Length == 0) continue; // "Disallow:" with no value allows everything
                var rule = (value, field.Equals("allow", StringComparison.OrdinalIgnoreCase));
                if (collectsOwn && own.Count < MaxRulesPerGroup) own.Add(rule);
                if (collectsAny && any.Count < MaxRulesPerGroup) any.Add(rule);
            }
            else lastWasAgent = false; // Sitemap, Crawl-delay, unknown fields
        }

        return new RobotsRules(ownSeen ? own : any);
    }

    /// <summary>Whether <paramref name="url"/> may be fetched (path and query are matched).</summary>
    public bool IsAllowed(Uri url)
    {
        // Rules and URL are compared percent-decoded, so "/é" and "/%C3%A9" are the same path (RFC 9309).
        var path = Uri.UnescapeDataString(url.PathAndQuery);
        var bestLength = -1;
        var allowed = true;
        foreach (var (pattern, allow) in _rules)
        {
            if (pattern.Length < bestLength || !Matches(pattern, path)) continue;
            if (pattern.Length > bestLength || allow) allowed = allow;
            bestLength = pattern.Length;
        }

        return allowed;
    }

    private static bool Matches(string pattern, string path)
    {
        pattern = Uri.UnescapeDataString(pattern);
        var anchored = pattern.EndsWith('$');
        if (anchored) pattern = pattern[..^1];

        int p = 0, s = 0, star = -1, mark = 0;
        while (true)
        {
            if (p == pattern.Length)
            {
                if (!anchored || s == path.Length) return true;
            }
            else if (pattern[p] == '*')
            {
                star = p++;
                mark = s;
                continue;
            }
            else if (s < path.Length && pattern[p] == path[s])
            {
                p++;
                s++;
                continue;
            }

            if (star < 0 || ++mark > path.Length) return false;
            p = star + 1;
            s = mark;
        }
    }
}
