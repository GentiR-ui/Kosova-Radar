using App.Application.Abstractions;
using App.Application.Public;
using App.Domain.Content;
using App.Domain.Sources;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Dashboard;

public sealed record DashboardDto(
    int PendingCandidates, int NeedsReviewCandidates, int ActiveEvents, int ActiveDeals, int ExpiredLast7Days,
    int FailingSources, int DegradedSources, int AiCallsToday, int AiErrorsToday);

/// <summary>Counts for the admin dashboard (design.md §5). Active items use the same rules as the public API.</summary>
public sealed class DashboardQueries(IAppDbContext db, IClock clock, TimeProvider wallClock)
{
    public async Task<DashboardDto> GetAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var weekAgo = now.AddDays(-7);
        // AI calls are stamped with the wall clock (see AiCall.StartedAt), so "today" is the real UTC day.
        var dayStart = new DateTimeOffset(wallClock.GetUtcNow().UtcDateTime.Date, TimeSpan.Zero);

        var pending = db.Candidates.AsNoTracking().Where(c => c.Status == CandidateStatus.Pending);
        var pendingCount = await pending.CountAsync(ct);
        var needsReview = await pending.CountAsync(c => c.Validation == ValidationOutcome.NeedsReview, ct);
        var activeEvents = await db.Events.AsNoTracking().CountAsync(ActiveItems.Event(now), ct);
        var activeDeals = await db.Deals.AsNoTracking().CountAsync(ActiveItems.Deal(now), ct);
        // Expiry time is not stored; UpdatedAt is set when an item expires and is not touched afterwards by the system.
        var expired = await db.Events.AsNoTracking().CountAsync(e => e.Status == ItemStatus.Expired && e.UpdatedAt > weekAgo, ct)
            + await db.Deals.AsNoTracking().CountAsync(d => d.Status == ItemStatus.Expired && d.UpdatedAt > weekAgo, ct);

        // Same thresholds as Source.Health: only active sources, failing from FailingThreshold consecutive failures.
        var sources = db.Sources.AsNoTracking().Where(s => s.IsActive && s.LastFetchedAt != null && s.ConsecutiveFailures > 0);
        var failing = await sources.CountAsync(s => s.ConsecutiveFailures >= Source.FailingThreshold, ct);
        var degraded = await sources.CountAsync(s => s.ConsecutiveFailures < Source.FailingThreshold, ct);

        var aiToday = db.AiCalls.AsNoTracking().Where(c => c.StartedAt >= dayStart);
        var aiCalls = await aiToday.CountAsync(ct);
        var aiErrors = await aiToday.CountAsync(c => !c.Success, ct);

        return new DashboardDto(pendingCount, needsReview, activeEvents, activeDeals, expired, failing, degraded, aiCalls, aiErrors);
    }
}
