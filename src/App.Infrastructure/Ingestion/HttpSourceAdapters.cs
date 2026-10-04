using App.Application.Ingestion;
using App.Domain.Sources;

namespace App.Infrastructure.Ingestion;

/// <summary>Fetches <see cref="Source.BaseUrl"/> over the guarded client; subclasses parse the payload.</summary>
internal abstract class HttpSourceAdapter(SourceHttpFetcher fetcher) : ISourceAdapter
{
    protected abstract SourceType Type { get; }

    public bool CanHandle(Source source) => source.Type == Type;

    public async Task<FetchResult> FetchAsync(Source source, CancellationToken ct)
    {
        if (!Uri.TryCreate(source.BaseUrl, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
            throw new SourceFetchException(RunErrorCodes.Unreachable, "The source URL is not a valid http(s) address.");
        return new FetchResult(await fetcher.GetStringAsync(url, ct));
    }

    public abstract Task<FeedParseResult> ParseAsync(Source source, FetchResult fetched, CancellationToken ct);
}

/// <summary>Radar JSON feed over HTTP (design.md §6).</summary>
internal sealed class JsonFeedSourceAdapter(SourceHttpFetcher fetcher) : HttpSourceAdapter(fetcher)
{
    protected override SourceType Type => SourceType.JsonFeed;

    public override Task<FeedParseResult> ParseAsync(Source source, FetchResult fetched, CancellationToken ct) =>
        Task.FromResult(RadarFeedParser.Parse(source.Id, fetched.Content));
}

/// <summary>
/// RSS/Atom sources are fetched (and guarded) like any other; parsing arrives with T025, until then the run fails
/// as a malformed payload.
/// </summary>
internal sealed class RssSourceAdapter(SourceHttpFetcher fetcher) : HttpSourceAdapter(fetcher)
{
    protected override SourceType Type => SourceType.Rss;

    public override Task<FeedParseResult> ParseAsync(Source source, FetchResult fetched, CancellationToken ct) =>
        throw new SourceFetchException(RunErrorCodes.MalformedPayload, "RSS and Atom feeds are not supported yet.");
}
