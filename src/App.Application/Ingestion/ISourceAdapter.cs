using App.Domain.Sources;

namespace App.Application.Ingestion;

/// <summary>Run error codes (design.md §5); PascalCase strings on the wire.</summary>
public static class RunErrorCodes
{
    public const string Unreachable = "Unreachable";
    public const string Timeout = "Timeout";
    public const string HttpStatus = "HttpStatus";
    public const string MalformedPayload = "MalformedPayload";
    public const string BlockedAddress = "BlockedAddress";
    public const string RobotsDisallowed = "RobotsDisallowed";
    public const string TooLarge = "TooLarge";
    public const string Interrupted = "Interrupted";
}

/// <summary>
/// A fetch that failed as a whole. <see cref="Exception.Message"/> is stored on the source and the run and shown to
/// admins: it must never contain response bodies, query strings or credentials.
/// </summary>
public sealed class SourceFetchException(string errorCode, string message, int? httpStatus = null) : Exception(message)
{
    public string ErrorCode { get; } = errorCode;

    /// <summary>The numeric status of an <see cref="RunErrorCodes.HttpStatus"/> failure (the robots.txt rules depend on it).</summary>
    public int? HttpStatus { get; } = httpStatus;
}

/// <summary>The raw payload of one fetch, before parsing. <paramref name="MorePages"/>: further pages of a paginated HTML listing (§6).</summary>
public sealed record FetchResult(string Content, IReadOnlyList<FetchedPage>? MorePages = null);

/// <summary>A further page of a paginated HTML listing with the address it was read from.</summary>
public sealed record FetchedPage(Uri Url, string Content);

/// <summary>
/// Turns one source into normalized candidates (design.md §2). Parsing and normalization are one step because
/// every feed format produces normalized candidates directly (ADR-023).
/// </summary>
public interface ISourceAdapter
{
    bool CanHandle(Source source);

    /// <exception cref="SourceFetchException">The whole fetch failed.</exception>
    Task<FetchResult> FetchAsync(Source source, CancellationToken ct);

    /// <summary>Per-item failures are returned in the result; an unusable payload has <c>ErrorCode</c> set.</summary>
    Task<FeedParseResult> ParseAsync(Source source, FetchResult fetched, CancellationToken ct);
}
