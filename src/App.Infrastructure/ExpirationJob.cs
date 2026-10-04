using App.Application.Public;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace App.Infrastructure;

/// <summary>Runs <see cref="ExpirationRunner"/> every 5 minutes on the real clock (never the test clock).</summary>
public sealed class ExpirationJob(IServiceScopeFactory scopes, ILogger<ExpirationJob> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var count = await scope.ServiceProvider.GetRequiredService<ExpirationRunner>().RunAsync(DateTimeOffset.UtcNow, stoppingToken);
                if (count > 0) logger.LogInformation("Expired {Count} items", count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Expiration run failed");
            }
        }
    }
}
