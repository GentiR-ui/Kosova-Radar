using App.Domain.Content;
using App.Domain.Reference;
using App.Domain.Sources;

namespace App.Application.Sources;

/// <summary>
/// design.md §5. <c>Type</c> and <c>DefaultKind</c> are strings so that an unknown value is reported as a
/// validation error on its own field instead of a body binding failure.
/// </summary>
public sealed record SourceRequest(
    string? Name, string? Type, string? Adapter, string? BaseUrl, string? FixturePayload, string? DefaultKind,
    int? PollIntervalMinutes, bool IsActive, bool IsUnreliable, bool AutoPublish = false);

public record SourceDto(
    Guid Id, string Name, SourceType Type, string? Adapter, string? BaseUrl, ItemKind DefaultKind, bool IsActive, bool IsUnreliable, bool AutoPublish,
    int PollIntervalMinutes, SourceHealth Health, DateTimeOffset? LastFetchedAt, DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastErrorAt, string? LastError, int ConsecutiveFailures)
{
    public static SourceDto From(Source s) =>
        new(s.Id, s.Name, s.Type, s.Adapter, s.BaseUrl, s.DefaultKind, s.IsActive, s.IsUnreliable, s.AutoPublish, s.PollIntervalMinutes,
            s.Health, s.LastFetchedAt, s.LastSuccessAt, s.LastErrorAt, s.LastError, s.ConsecutiveFailures);
}

public sealed record SourceDetailDto(
    Guid Id, string Name, SourceType Type, string? Adapter, string? BaseUrl, ItemKind DefaultKind, bool IsActive, bool IsUnreliable, bool AutoPublish,
    int PollIntervalMinutes, SourceHealth Health, DateTimeOffset? LastFetchedAt, DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastErrorAt, string? LastError, int ConsecutiveFailures,
    string? FixturePayload, IReadOnlyList<RunDto> RecentRuns)
    : SourceDto(Id, Name, Type, Adapter, BaseUrl, DefaultKind, IsActive, IsUnreliable, AutoPublish, PollIntervalMinutes, Health,
        LastFetchedAt, LastSuccessAt, LastErrorAt, LastError, ConsecutiveFailures)
{
    public static SourceDetailDto From(Source s, IReadOnlyList<RunDto> recentRuns) =>
        new(s.Id, s.Name, s.Type, s.Adapter, s.BaseUrl, s.DefaultKind, s.IsActive, s.IsUnreliable, s.AutoPublish, s.PollIntervalMinutes,
            s.Health, s.LastFetchedAt, s.LastSuccessAt, s.LastErrorAt, s.LastError, s.ConsecutiveFailures,
            s.FixturePayload, recentRuns);
}

/// <summary>One ingestion run (design.md §5).</summary>
public sealed record RunDto(
    Guid Id, Guid SourceId, string Trigger, string Status, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt,
    int Received, int Created, int Updated, int Unchanged, int Failed, string? ErrorCode, string? Error)
{
    // Enum names are single words, so lower-casing them gives the camelCase wire value ("manual", "succeeded").
    public static RunDto From(JobExecution r) =>
        new(r.Id, r.SourceId ?? Guid.Empty, r.Trigger.ToString().ToLowerInvariant(), r.Status.ToString().ToLowerInvariant(),
            r.StartedAt, r.FinishedAt, r.Received, r.Created, r.Updated, r.Unchanged, r.Failed, r.ErrorCode, r.Error);
}

/// <summary>What the audit log keeps about a source. The fixture payload can be 1 MB, so only its length is kept.</summary>
public sealed record SourceAuditState(
    string Name, SourceType Type, string? Adapter, string? BaseUrl, ItemKind DefaultKind, bool IsActive, bool IsUnreliable,
    bool AutoPublish, int PollIntervalMinutes, int? FixturePayloadLength)
{
    public static SourceAuditState From(Source s) =>
        new(s.Name, s.Type, s.Adapter, s.BaseUrl, s.DefaultKind, s.IsActive, s.IsUnreliable, s.AutoPublish, s.PollIntervalMinutes,
            s.FixturePayload?.Length);
}

/// <summary>Ingestion settings from the <c>Ingestion__*</c> environment variables (design.md §2).</summary>
public sealed class IngestionOptions
{
    public const string Section = "Ingestion";

    /// <summary>Fixture sources carry synthetic data; they are refused unless this is on (Development/Testing).</summary>
    public bool AllowFixtureSources { get; set; }

    /// <summary>Seconds between scheduler ticks (design.md §2: 60, Development 10, Testing 2).</summary>
    public int SchedulerTickSeconds { get; set; } = 60;

    /// <summary>Retries of a failed source request (5xx, 408, 429, network errors) with exponential backoff.</summary>
    public int HttpRetries { get; set; } = 2;

    /// <summary>Total time for one source fetch including retries and reading the body.</summary>
    public int HttpTimeoutSeconds { get; set; } = 20;

    /// <summary>Minimum seconds between two requests to the same host (design.md §6: 10, Testing 0).</summary>
    public int PerHostDelaySeconds { get; set; } = 10;

    /// <summary>
    /// Events of <c>Html</c> and auto-publish sources starting on a Europe/Pristina date more than this many days after the
    /// fetch date are skipped, to be taken by a later fetch (REQ-026, ADR-042; replaces <c>Ingestion__HtmlHorizonDays</c>).
    /// </summary>
    public int EventHorizonDays { get; set; } = 3;

    /// <summary>
    /// Pagination pages one run of an HTML source may fetch, the first included (design.md §6; server-rendered "next page"
    /// links only). 1 turns following pages off.
    /// </summary>
    public int HtmlMaxPages { get; set; } = 5;
}
