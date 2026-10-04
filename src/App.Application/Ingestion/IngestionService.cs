using System.Diagnostics;
using App.Application.Abstractions;
using App.Application.Audit;
using App.Application.Sources;
using App.Domain.Content;
using App.Domain.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace App.Application.Ingestion;

public enum RunStatus { Completed, NotFound, Busy }

/// <summary><see cref="Run"/> is set when <see cref="Status"/> is <see cref="RunStatus.Completed"/> (also for a failed run).</summary>
public sealed record RunResult(RunStatus Status, RunDto? Run = null);

/// <summary>
/// One ingestion run of one source (design.md §2): fetch, parse, look each item up by fingerprint, store candidates,
/// record the run and the source's health. Candidates, run and source bookkeeping are saved together at the end, so a
/// failed run leaves no half-processed feed behind.
/// Lookup rows 1 (open candidate) and 5 (nothing known) are implemented; rows 2-4 land with T028, AI with T026, validation with T017.
/// </summary>
public sealed class IngestionService(
    IAppDbContext db,
    IClock clock,
    TimeProvider wallClock,
    IAuditWriter audit,
    IEnumerable<ISourceAdapter> adapters,
    SourceRunLock locks,
    ILogger<IngestionService> log)
{
    /// <summary>A manual run waits this long for a run in progress, then gives up (ADR-015).</summary>
    public static readonly TimeSpan ManualLockWait = TimeSpan.FromSeconds(30);

    private const int MaxErrorLength = 1000;

    /// <summary>Marks runs left <c>Running</c> by a previous process as failed (<c>Interrupted</c>). Call once at startup, single instance.</summary>
    public async Task MarkInterruptedAsync(CancellationToken ct)
    {
        var now = wallClock.GetUtcNow();
        var count = await db.JobExecutions.Where(j => j.Status == JobStatus.Running)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, JobStatus.Failed)
                .SetProperty(j => j.ErrorCode, RunErrorCodes.Interrupted)
                .SetProperty(j => j.Error, "The run did not finish before the application stopped.")
                .SetProperty(j => j.FinishedAt, now), ct);
        if (count > 0) log.LogWarning("Marked {Count} interrupted run(s) as failed", count);
    }

    public async Task<RunResult> RunAsync(Guid sourceId, JobTrigger trigger, TimeSpan lockWait, CancellationToken ct)
    {
        if (!await db.Sources.AnyAsync(s => s.Id == sourceId, ct)) return new(RunStatus.NotFound);

        using var held = await locks.TryAcquireAsync(sourceId, lockWait, ct);
        if (held is null) return new(RunStatus.Busy);

        // Loaded after the lock so that the bookkeeping of the run that just ended is visible.
        var source = await db.Sources.SingleOrDefaultAsync(s => s.Id == sourceId, ct);
        if (source is null) return new(RunStatus.NotFound);

        var run = new JobExecution
        {
            JobName = JobName.SourceFetch,
            SourceId = source.Id,
            Trigger = trigger,
            // Wall clock: run history shows when it really ran, also under the X-Test-Now hook (like the audit log).
            StartedAt = wallClock.GetUtcNow(),
            Status = JobStatus.Running,
            CorrelationId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N"),
        };
        db.JobExecutions.Add(run);
        await db.SaveChangesAsync(ct);

        using var scope = log.BeginScope(new Dictionary<string, object> { ["RunId"] = run.Id, ["SourceId"] = source.Id });
        var fetchedAt = clock.UtcNow;
        try
        {
            string? errorCode = null, error = null;
            try
            {
                await ProcessAsync(source, run, fetchedAt, ct);
            }
            catch (SourceFetchException e)
            {
                (errorCode, error) = (e.ErrorCode, Truncate(e.Message));
            }

            Finish(source, run, fetchedAt, errorCode, error);
            if (trigger == JobTrigger.Manual)
                audit.Record(AuditActions.SourceFetch, AuditEntityTypes.Source, source.Id, after: RunAuditState.From(run));
            await db.SaveChangesAsync(ct);
        }
        catch (Exception e)
        {
            // Not a fetch failure but a bug or an outage of the database: do not leave the run "Running" until the next restart.
            log.LogError(e, "Ingestion run crashed");
            await TryMarkCrashedAsync(source.Id, run.Id, countAsFailure: e is not OperationCanceledException);
            throw;
        }

        log.LogInformation(
            "Run finished: {Status} received {Received} created {Created} updated {Updated} unchanged {Unchanged} failed {Failed} error {ErrorCode}",
            run.Status, run.Received, run.Created, run.Updated, run.Unchanged, run.Failed, run.ErrorCode);
        return new(RunStatus.Completed, RunDto.From(run));
    }

    /// <summary>Best effort: a failure here must not hide the exception that crashed the run.</summary>
    private async Task TryMarkCrashedAsync(Guid sourceId, Guid runId, bool countAsFailure)
    {
        try
        {
            var now = wallClock.GetUtcNow();
            await db.JobExecutions.Where(j => j.Id == runId).ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, JobStatus.Failed)
                .SetProperty(j => j.ErrorCode, RunErrorCodes.Interrupted)
                .SetProperty(j => j.Error, "The run stopped unexpectedly.")
                .SetProperty(j => j.FinishedAt, now), CancellationToken.None);
            // A client that gave up waiting is not the source's fault; a crash is, so repeated crashes make it "failing".
            if (countAsFailure)
                await db.Sources.Where(x => x.Id == sourceId).ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.ConsecutiveFailures, x => x.ConsecutiveFailures + 1)
                    .SetProperty(x => x.LastErrorAt, clock.UtcNow)
                    .SetProperty(x => x.LastError, "The run stopped unexpectedly."), CancellationToken.None);
        }
        catch (Exception e)
        {
            log.LogError(e, "Could not mark the crashed run as failed");
        }
    }

    private async Task ProcessAsync(Source source, JobExecution run, DateTimeOffset fetchedAt, CancellationToken ct)
    {
        // Unreachable is the closest contract code for "nothing can fetch this source": JsonFeed/Rss adapters come
        // with T023/T025, and a fixture source is refused when fixtures are disallowed (T013 note).
        var adapter = adapters.FirstOrDefault(a => a.CanHandle(source))
            ?? throw new SourceFetchException(RunErrorCodes.Unreachable, "No adapter can fetch this source.");

        var fetched = await adapter.FetchAsync(source, ct);
        var parsed = await adapter.ParseAsync(source, fetched, ct);
        if (parsed.ErrorCode is { } code)
            throw new SourceFetchException(code, "The payload is not a valid feed.");

        run.Received = parsed.Received;
        run.Failed = parsed.Failures.Count;
        await StoreAsync(source, run, parsed.Items, fetchedAt, ct);
    }

    private async Task StoreAsync(
        Source source, JobExecution run, IReadOnlyList<NormalizedCandidate> items, DateTimeOffset fetchedAt, CancellationToken ct)
    {
        var fingerprints = items.Select(i => i.Fingerprint).Distinct().ToList();
        var known = (await db.Candidates.Where(c => fingerprints.Contains(c.Fingerprint)).ToListAsync(ct))
            .GroupBy(c => c.Fingerprint).ToDictionary(g => g.Key, g => g.ToList());
        var resolver = await ReferenceResolver.LoadAsync(db, ct);
        var now = clock.UtcNow;

        foreach (var item in items)
        {
            var fields = resolver.Resolve(item.Kind, item.Fields);
            known.TryGetValue(item.Fingerprint, out var existing);

            // Row 1: a pending or auto-rejected candidate is the one to compare with (also for a repeat inside one feed).
            var open = existing?
                .Where(c => c.Status == CandidateStatus.Pending || (c.Status == CandidateStatus.Rejected && c.RejectedByUserId is null))
                .OrderByDescending(c => c.CreatedAt).ThenBy(c => c.Id).FirstOrDefault();
            if (open is not null)
            {
                if (open.ContentHash == item.ContentHash)
                {
                    run.Unchanged++;
                    continue;
                }

                open.Kind = item.Kind;
                open.ExternalId = item.ExternalId;
                open.ContentHash = item.ContentHash;
                open.Fields = fields;
                open.Evidence = item.Evidence;
                open.FetchedAt = fetchedAt;
                open.UpdatedAt = now;
                run.Updated++;
            }
            else if (existing is { Count: > 0 })
            {
                // Rows 2-4 (decided, withdrawn or published items) are T028. Until then such an item is left alone
                // rather than becoming a new candidate on every run.
                run.Unchanged++;
            }
            else
            {
                var candidate = new Candidate
                {
                    Kind = item.Kind,
                    SourceId = source.Id,
                    ExternalId = item.ExternalId,
                    SourceUrl = item.SourceUrl,
                    Fingerprint = item.Fingerprint,
                    ContentHash = item.ContentHash,
                    Fields = fields,
                    Evidence = item.Evidence,
                    Status = CandidateStatus.Pending,
                    // Real validation is T017; until then nothing is ever "Valid" without having been checked.
                    Validation = ValidationOutcome.NeedsReview,
                    IsSynthetic = source.Type == SourceType.Fixture,
                    FetchedAt = fetchedAt,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                db.Candidates.Add(candidate);
                known[item.Fingerprint] = [candidate];
                run.Created++;
            }
        }
    }

    private void Finish(Source source, JobExecution run, DateTimeOffset fetchedAt, string? errorCode, string? error)
    {
        run.FinishedAt = wallClock.GetUtcNow();
        source.LastFetchedAt = fetchedAt;
        if (errorCode is null)
        {
            run.Status = JobStatus.Succeeded;
            source.LastSuccessAt = fetchedAt;
            source.ConsecutiveFailures = 0;
        }
        else
        {
            run.Status = JobStatus.Failed;
            run.ErrorCode = errorCode;
            run.Error = error;
            source.LastErrorAt = fetchedAt;
            source.LastError = error;
            source.ConsecutiveFailures++;
        }
    }

    private static string Truncate(string message) =>
        message.Length <= MaxErrorLength ? message : message[..MaxErrorLength];
}

/// <summary>What the audit log keeps about a manual fetch: the outcome, never the payload.</summary>
public sealed record RunAuditState(
    Guid RunId, string Status, int Received, int Created, int Updated, int Unchanged, int Failed, string? ErrorCode)
{
    public static RunAuditState From(JobExecution r) =>
        new(r.Id, r.Status.ToString().ToLowerInvariant(), r.Received, r.Created, r.Updated, r.Unchanged, r.Failed, r.ErrorCode);
}
