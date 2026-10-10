using App.Domain.Reference;
using App.Domain.Sources;

namespace App.Application.Sources;

/// <summary>A request that passed <see cref="SourceValidator"/>, with defaults applied.</summary>
public sealed record ValidSource(
    string Name, SourceType Type, string? Adapter, string? BaseUrl, string? FixturePayload, ItemKind DefaultKind,
    int PollIntervalMinutes, bool IsActive, bool IsUnreliable, bool AutoPublish);

/// <summary>Per-type rules of <see cref="SourceRequest"/> (REQ-010, REQ-011, design.md §5).</summary>
public static class SourceValidator
{
    public const int MaxNameLength = 120;
    public const int MaxUrlLength = 2000;
    public const int MaxFixtureChars = 1024 * 1024;

    public static (ValidSource? Source, Dictionary<string, string[]> Errors) Validate(SourceRequest req, bool allowFixtureSources)
    {
        var errors = new Dictionary<string, string[]>();

        var name = req.Name?.Trim();
        if (string.IsNullOrEmpty(name)) errors["name"] = ["Name is required."];
        else if (name.Length > MaxNameLength) errors["name"] = [$"Name must be at most {MaxNameLength} characters."];
        else if (name.Any(char.IsControl)) errors["name"] = ["Name must not contain control characters."];

        var type = ParseType(req.Type);
        if (type is null) errors["type"] = ["Type must be one of: fixture, jsonFeed, rss, html."];
        else if (type == SourceType.Fixture && !allowFixtureSources) errors["type"] = ["Fixture sources are not allowed in this environment."];

        var kind = ItemKind.Event;
        if (req.DefaultKind is not null)
        {
            if (ParseName<ItemKind>(req.DefaultKind) is { } parsed) kind = parsed;
            else errors["defaultKind"] = ["Default kind must be one of: event, deal."];
        }

        var (min, max) = type is null or SourceType.Fixture ? (1, 1440) : (15, 1440);
        if (req.PollIntervalMinutes is not { } poll) errors["pollIntervalMinutes"] = ["Poll interval is required."];
        else if (poll < min || poll > max) errors["pollIntervalMinutes"] = [$"Poll interval must be between {min} and {max} minutes."];

        string? baseUrl = null, payload = null;
        if (type == SourceType.Fixture)
        {
            if (string.IsNullOrWhiteSpace(req.FixturePayload)) errors["fixturePayload"] = ["Fixture payload is required."];
            else if (req.FixturePayload.Length > MaxFixtureChars) errors["fixturePayload"] = ["Fixture payload must be at most 1 MB."];
            else if (req.FixturePayload.Contains((char)0)) errors["fixturePayload"] = ["Fixture payload must not contain NUL characters."]; // PostgreSQL text cannot store them
            else payload = req.FixturePayload;
        }
        else if (type is not null)
        {
            var url = req.BaseUrl?.Trim();
            if (string.IsNullOrEmpty(url)) errors["baseUrl"] = ["URL is required."];
            else if (!IsAcceptableUrl(url)) errors["baseUrl"] = [$"URL must be an absolute http(s) address of at most {MaxUrlLength} characters, without credentials."];
            else baseUrl = url;
        }

        // Only html sources have a site parser; for the other types the field is ignored and stored null.
        string? adapter = null;
        if (type == SourceType.Html)
        {
            adapter = HtmlAdapters.All.FirstOrDefault(a => string.Equals(a, req.Adapter?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (adapter is null) errors["adapter"] = [$"Adapter must be one of: {string.Join(", ", HtmlAdapters.All)}."];
        }

        if (errors.Count > 0) return (null, errors);
        return (new ValidSource(name!, type!.Value, adapter, baseUrl, payload, kind, req.PollIntervalMinutes!.Value, req.IsActive, req.IsUnreliable, req.AutoPublish), errors);
    }

    private static SourceType? ParseType(string? value) => ParseName<SourceType>(value);

    // Names only: Enum.TryParse would also accept "1" or "Fixture, Rss".
    private static T? ParseName<T>(string? value) where T : struct, Enum =>
        value is not null && value.All(char.IsLetter) && Enum.TryParse(value, ignoreCase: true, out T parsed) ? parsed : null;

    // Credentials in the URL would end up in the audit log and in the list responses.
    private static bool IsAcceptableUrl(string url) =>
        url.Length <= MaxUrlLength
        && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https"
        && string.IsNullOrEmpty(uri.UserInfo);
}
