using App.Application.Ingestion;
using App.Application.Sources;
using App.Domain.Sources;

namespace App.Infrastructure.Ingestion;

/// <summary>
/// Reads the payload stored on a <see cref="SourceType.Fixture"/> source (no network); its items are synthetic.
/// A fixture source is refused unless fixtures are allowed in this environment (design.md §5, T013 note).
/// </summary>
internal sealed class FixtureSourceAdapter(IngestionOptions options) : ISourceAdapter
{
    public bool CanHandle(Source source) => source.Type == SourceType.Fixture && options.AllowFixtureSources;

    public Task<FetchResult> FetchAsync(Source source, CancellationToken ct) =>
        Task.FromResult(new FetchResult(source.FixturePayload ?? string.Empty));

    public Task<FeedParseResult> ParseAsync(Source source, FetchResult fetched, CancellationToken ct) =>
        Task.FromResult(RadarFeedParser.Parse(source.Id, fetched.Content));
}
