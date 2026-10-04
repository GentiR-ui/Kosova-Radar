using App.Application.Abstractions;
using App.Application.Ai;
using App.Application.Audit;
using App.Application.Ingestion;
using App.Domain.Content;
using App.Domain.Sources;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace App.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public class SchedulerTests(AppFactory app)
{
    private static readonly string Payload = "{\"items\":[]}";

    private async Task<Guid> AddSourceAsync(string name, bool active, DateTimeOffset? lastFetchedAt, int pollMinutes = 60)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var source = new Source
        {
            Name = name,
            Type = SourceType.Fixture,
            FixturePayload = Payload,
            IsActive = active,
            PollIntervalMinutes = pollMinutes,
            LastFetchedAt = lastFetchedAt,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Sources.Add(source);
        await db.SaveChangesAsync();
        return source.Id;
    }

    private async Task<List<JobExecution>> RunsAsync(Guid sourceId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().JobExecutions.AsNoTracking()
            .Where(j => j.SourceId == sourceId).ToListAsync();
    }

    private ScheduledRuns Runs(IServiceScopeFactory? scopes = null) =>
        new(scopes ?? app.Services.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, NullLogger<ScheduledRuns>.Instance);

    [Fact]
    public async Task Only_active_sources_that_are_never_fetched_or_due_are_run()
    {
        var now = DateTimeOffset.UtcNow;
        var t = Guid.NewGuid().ToString("N")[..8];
        var never = await AddSourceAsync($"never {t}", true, null);
        var due = await AddSourceAsync($"due {t}", true, now.AddMinutes(-61));
        var justDue = await AddSourceAsync($"edge {t}", true, now.AddMinutes(-60).AddSeconds(-1));
        var fresh = await AddSourceAsync($"fresh {t}", true, now.AddMinutes(-59));
        var inactive = await AddSourceAsync($"off {t}", false, null);

        await Runs().TickAsync(CancellationToken.None);

        foreach (var id in new[] { never, due, justDue })
        {
            var run = Assert.Single(await RunsAsync(id));
            Assert.Equal(JobTrigger.Schedule, run.Trigger);
            Assert.Equal(JobStatus.Succeeded, run.Status);
        }
        Assert.Empty(await RunsAsync(fresh));
        Assert.Empty(await RunsAsync(inactive));

        await Runs().TickAsync(CancellationToken.None);
        Assert.Single(await RunsAsync(never)); // just fetched, not due again
    }

    [Fact]
    public async Task A_source_whose_lock_is_held_is_skipped_by_the_tick()
    {
        var id = await AddSourceAsync($"busy {Guid.NewGuid():N}", true, null);
        var locks = app.Services.GetRequiredService<SourceRunLock>();
        using (await locks.TryAcquireAsync(id, TimeSpan.Zero, CancellationToken.None))
            await Runs().TickAsync(CancellationToken.None);

        Assert.Empty(await RunsAsync(id));
    }

    [Fact]
    public async Task A_crashing_source_does_not_stop_the_other_sources()
    {
        var t = Guid.NewGuid().ToString("N")[..8];
        var boom = await AddSourceAsync($"Boom {t}", true, null);
        var fine = await AddSourceAsync($"Fine {t}", true, DateTimeOffset.UtcNow.AddMinutes(-61)); // sorts after Boom (never fetched)

        await Runs(new ThrowingScopeFactory(app.Services.GetRequiredService<IServiceScopeFactory>())).TickAsync(CancellationToken.None);

        Assert.Equal(JobStatus.Failed, Assert.Single(await RunsAsync(boom)).Status);
        Assert.Equal(JobStatus.Succeeded, Assert.Single(await RunsAsync(fine)).Status);
    }

    [Fact]
    public async Task A_crashed_run_is_not_retried_before_the_poll_interval()
    {
        var boom = await AddSourceAsync($"Boom {Guid.NewGuid():N}", true, null);
        var factory = new ThrowingScopeFactory(app.Services.GetRequiredService<IServiceScopeFactory>());

        await Runs(factory).TickAsync(CancellationToken.None);
        await Runs(factory).TickAsync(CancellationToken.None);

        Assert.Single(await RunsAsync(boom));
    }

    [Fact]
    public async Task A_scheduled_run_rechecks_under_the_lock_that_the_source_is_still_due()
    {
        var inactive = await AddSourceAsync($"off {Guid.NewGuid():N}", false, null);
        var fresh = await AddSourceAsync($"fresh {Guid.NewGuid():N}", true, DateTimeOffset.UtcNow);
        await using var scope = app.Services.CreateAsyncScope();
        var ingestion = scope.ServiceProvider.GetRequiredService<IngestionService>();

        Assert.Equal(RunStatus.NotDue, (await ingestion.RunAsync(inactive, JobTrigger.Schedule, TimeSpan.Zero, CancellationToken.None)).Status);
        Assert.Equal(RunStatus.NotDue, (await ingestion.RunAsync(fresh, JobTrigger.Schedule, TimeSpan.Zero, CancellationToken.None)).Status);
        Assert.Empty(await RunsAsync(inactive));
        Assert.Empty(await RunsAsync(fresh));
    }

    /// <summary>Hands out the real scopes but swaps <see cref="IngestionService"/> for one whose adapter throws for "Boom" sources.</summary>
    private sealed class ThrowingScopeFactory(IServiceScopeFactory inner) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new Scope(inner.CreateScope());

        private sealed class Scope(IServiceScope real) : IServiceScope, IServiceProvider
        {
            public IServiceProvider ServiceProvider => this;

            public object? GetService(Type type)
            {
                if (type != typeof(IngestionService)) return real.ServiceProvider.GetService(type);
                var sp = real.ServiceProvider;
                return new IngestionService(sp.GetRequiredService<IAppDbContext>(), sp.GetRequiredService<IClock>(), TimeProvider.System,
                    sp.GetRequiredService<IAuditWriter>(), sp.GetRequiredService<AiEnricher>(), [new SelectiveAdapter()], sp.GetRequiredService<SourceRunLock>(),
                    NullLogger<IngestionService>.Instance);
            }

            public void Dispose() => real.Dispose();
        }
    }

    private sealed class SelectiveAdapter : ISourceAdapter
    {
        public bool CanHandle(Source source) => true;

        public Task<FetchResult> FetchAsync(Source source, CancellationToken ct) =>
            source.Name.StartsWith("Boom", StringComparison.Ordinal)
                ? throw new InvalidOperationException("boom")
                : Task.FromResult(new FetchResult(Payload));

        public Task<FeedParseResult> ParseAsync(Source source, FetchResult fetched, CancellationToken ct) =>
            Task.FromResult(RadarFeedParser.Parse(source.Id, fetched.Content));
    }
}
