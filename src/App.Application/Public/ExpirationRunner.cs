using App.Application.Abstractions;
using App.Domain.Content;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Public;

/// <summary>Marks published items past their end as <c>Expired</c> (design.md §2). Public queries filter by time anyway.</summary>
public sealed class ExpirationRunner(IAppDbContext db)
{
    public async Task<int> RunAsync(DateTimeOffset now, CancellationToken ct)
    {
        var started = DateTimeOffset.UtcNow;
        var noEndCutoff = now - ContentRules.DefaultEventDuration;
        var todayStart = ContentRules.PristinaDay(ContentRules.PristinaDate(now)).Start;
        var events = await db.Events
            .Where(e => e.Status == ItemStatus.Published
                && (e.EndsAt != null && e.EndsAt >= e.StartsAt ? e.EndsAt <= now
                    : e.StartTimeKnown ? e.StartsAt <= noEndCutoff : e.StartsAt < todayStart))
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, ItemStatus.Expired).SetProperty(e => e.UpdatedAt, now), ct);
        var deals = await db.Deals
            .Where(d => d.Status == ItemStatus.Published && d.ValidUntil != null && d.ValidUntil <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, ItemStatus.Expired).SetProperty(d => d.UpdatedAt, now), ct);
        var total = events + deals;
        if (total > 0)
        {
            db.JobExecutions.Add(new JobExecution
            {
                JobName = JobName.Expiration,
                Trigger = JobTrigger.Schedule,
                StartedAt = started,
                FinishedAt = DateTimeOffset.UtcNow,
                Status = JobStatus.Succeeded,
                Received = total,
                Updated = total,
                CorrelationId = Guid.NewGuid().ToString("N"),
            });
            await db.SaveChangesAsync(ct);
        }
        return total;
    }
}
