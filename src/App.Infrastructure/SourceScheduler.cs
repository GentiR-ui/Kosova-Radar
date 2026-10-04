using App.Application.Ingestion;
using App.Application.Sources;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace App.Infrastructure;

/// <summary>Calls <see cref="ScheduledRuns.TickAsync"/> every <c>Ingestion__SchedulerTickSeconds</c> on the real clock.</summary>
public sealed class SourceScheduler(ScheduledRuns runs, IngestionOptions options, ILogger<SourceScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, options.SchedulerTickSeconds)));
        // Tick once right away so a source that was never fetched does not wait a whole interval after startup.
        do
        {
            try
            {
                await runs.TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scheduler tick failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}
