using App.Application.Ai;
using App.Domain.Content;
using App.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace App.Infrastructure.Ai;

/// <summary>Saves each call in its own scope and context. A failure is logged, never thrown: the ledger must not break ingestion.</summary>
public sealed class AiLedger(IServiceScopeFactory scopes, ILogger<AiLedger> log) : IAiLedger
{
    public async Task RecordAsync(AiCall call)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AiCalls.Add(call);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception e)
        {
            log.LogError(e, "Could not record an AI call");
        }
    }
}
