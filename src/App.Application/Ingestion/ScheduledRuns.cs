using App.Application.Abstractions;
using App.Domain.Content;
using App.Domain.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace App.Application.Ingestion;

/// <summary>
/// One scheduler tick (design.md §2): finds the active sources that were never fetched or whose last fetch is at least
/// their poll interval old, and runs each of them in its own scope so that one failing source cannot stop the others.
/// Uses the wall clock (never the X-Test-Now hook); a source whose lock is taken (manual run) is skipped until the next tick.
/// </summary>
public sealed class ScheduledRuns(IServiceScopeFactory scopes, TimeProvider wallClock, ILogger<ScheduledRuns> log)
{
    /// <summary>Returns the number of runs that were carried out (failed runs included, skipped ones not).</summary>
    public async Task<int> TickAsync(CancellationToken ct)
    {
        List<Guid> due;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var now = wallClock.GetUtcNow();
            var active = await db.Sources.AsNoTracking().Where(s => s.IsActive)
                .Select(s => new { s.Id, s.LastFetchedAt, s.PollIntervalMinutes }).ToListAsync(ct);
            // Oldest first, never fetched first: a source that keeps failing cannot starve the others.
            due = active.Where(s => Source.IsDue(true, s.LastFetchedAt, s.PollIntervalMinutes, now))
                .OrderBy(s => s.LastFetchedAt).ThenBy(s => s.Id)
                .Select(s => s.Id).ToList();
        }

        var ran = 0;
        foreach (var id in due)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<IngestionService>()
                    .RunAsync(id, JobTrigger.Schedule, TimeSpan.Zero, ct);
                if (result.Status == RunStatus.Completed) ran++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception e)
            {
                // The run already recorded itself as failed; isolation means the next source still runs.
                log.LogError(e, "Scheduled run of source {SourceId} crashed", id);
            }
        }

        return ran;
    }
}
