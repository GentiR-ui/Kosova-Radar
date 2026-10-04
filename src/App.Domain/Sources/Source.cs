using App.Domain.Reference;

namespace App.Domain.Sources;

public enum SourceType
{
    Fixture,
    JsonFeed,
    Rss,
}

public enum SourceHealth
{
    Never,
    Healthy,
    Degraded,
    Failing,
    Disabled,
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
    public string? FixturePayload { get; set; }

    /// <summary>Kind of the items of an RSS source; unused by the other types.</summary>
    public ItemKind DefaultKind { get; set; } = ItemKind.Event;

    public bool IsActive { get; set; }
    public bool IsUnreliable { get; set; }
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
