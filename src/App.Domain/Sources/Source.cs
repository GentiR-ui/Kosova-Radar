using App.Domain.Reference;

namespace App.Domain.Sources;

public enum SourceType
{
    Fixture,
    JsonFeed,
    Rss,
    Html,
}

public enum SourceHealth
{
    Never,
    Healthy,
    Degraded,
    Failing,
    Disabled,
}

/// <summary>Adapter keys of <see cref="SourceType.Html"/> sources (design.md §6, ADR-035).</summary>
public static class HtmlAdapters
{
    public const string PrishtinaEvents = "prishtinaEvents";
    public const string Kapodalim = "kapodalim";
    public const string GjirafaMall = "gjirafaMall";
    public const string Foleja = "foleja";

    public static readonly IReadOnlyList<string> All = [PrishtinaEvents, Kapodalim, GjirafaMall, Foleja];
}

/// <summary>A place content is fetched from (design.md §4).</summary>
public sealed class Source
{
    /// <summary>From this many consecutive failed runs a source is <see cref="SourceHealth.Failing"/>.</summary>
    public const int FailingThreshold = 3;

    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public SourceType Type { get; set; }
    public string? BaseUrl { get; set; }

    /// <summary>Key of the site parser of an <see cref="SourceType.Html"/> source (<see cref="HtmlAdapters"/>); null for the other types.</summary>
    public string? Adapter { get; set; }

    public string? FixturePayload { get; set; }

    /// <summary>Kind of the items of an RSS source; unused by the other types.</summary>
    public ItemKind DefaultKind { get; set; } = ItemKind.Event;

    public bool IsActive { get; set; }
    public bool IsUnreliable { get; set; }

    /// <summary>REQ-023/024: new <c>Valid</c> candidates and changes whose only review issue is <c>ChangedSinceApproval</c> are published without a person (ADR-039).</summary>
    public bool AutoPublish { get; set; }

    public int PollIntervalMinutes { get; set; }
    public DateTimeOffset? LastFetchedAt { get; set; }
    public DateTimeOffset? LastSuccessAt { get; set; }
    public DateTimeOffset? LastErrorAt { get; set; }
    public string? LastError { get; set; }
    public int ConsecutiveFailures { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>REQ-012/013: active, and never fetched or last fetched at least the poll interval ago.</summary>
    public bool IsDue(DateTimeOffset now) => IsDue(IsActive, LastFetchedAt, PollIntervalMinutes, now);

    public static bool IsDue(bool isActive, DateTimeOffset? lastFetchedAt, int pollIntervalMinutes, DateTimeOffset now) =>
        isActive && (lastFetchedAt is null || lastFetchedAt.Value.AddMinutes(pollIntervalMinutes) <= now);

    /// <summary>Derived on read, never stored (design.md §2).</summary>
    public SourceHealth Health =>
        !IsActive ? SourceHealth.Disabled
        : LastFetchedAt is null ? SourceHealth.Never
        : ConsecutiveFailures == 0 ? SourceHealth.Healthy
        : ConsecutiveFailures < FailingThreshold ? SourceHealth.Degraded
        : SourceHealth.Failing;
}
