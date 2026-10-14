using App.Application.Ingestion;
using App.Domain.Sources;

namespace App.Infrastructure.Ingestion;

/// <summary>Fetches <see cref="Source.BaseUrl"/> over the guarded client after the robots.txt check; subclasses parse the payload.</summary>
internal abstract class HttpSourceAdapter(SourceHttpFetcher fetcher, RobotsChecker robots) : ISourceAdapter
{
    protected abstract SourceType Type { get; }

    /// <summary>For adapters that follow more than the configured URL (paginated HTML listings).</summary>
    protected SourceHttpFetcher Fetcher => fetcher;

    protected RobotsChecker Robots => robots;

    public bool CanHandle(Source source) => source.Type == Type;

    public virtual async Task<FetchResult> FetchAsync(Source source, CancellationToken ct)
    {
        if (!Uri.TryCreate(source.BaseUrl, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
            throw new SourceFetchException(RunErrorCodes.Unreachable, "The source URL is not a valid http(s) address.");
        await robots.EnsureAllowedAsync(url, ct);
        return new FetchResult(await fetcher.GetStringAsync(url, ct));
    }

    public abstract Task<FeedParseResult> ParseAsync(Source source, FetchResult fetched, CancellationToken ct);
}

/// <summary>Radar JSON feed over HTTP (design.md §6).</summary>
internal sealed class JsonFeedSourceAdapter(SourceHttpFetcher fetcher, RobotsChecker robots) : HttpSourceAdapter(fetcher, robots)
{
    protected override SourceType Type => SourceType.JsonFeed;

    public override Task<FeedParseResult> ParseAsync(Source source, FetchResult fetched, CancellationToken ct) =>
        Task.FromResult(RadarFeedParser.Parse(source.Id, fetched.Content));
}

/// <summary>RSS 2.0 / Atom feed over HTTP; every item becomes a candidate of the source's default kind (design.md §6).</summary>
internal sealed class RssSourceAdapter(SourceHttpFetcher fetcher, RobotsChecker robots) : HttpSourceAdapter(fetcher, robots)
{
    protected override SourceType Type => SourceType.Rss;

    public override Task<FeedParseResult> ParseAsync(Source source, FetchResult fetched, CancellationToken ct) =>
        Task.FromResult(RssFeedParser.Parse(source.Id, source.DefaultKind, fetched.Content));
}
