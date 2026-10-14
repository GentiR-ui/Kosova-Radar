using System.Diagnostics;
using App.Application.Abstractions;
using App.Application.Ai;
using App.Application.Audit;
using App.Application.Sources;
using App.Domain.Content;
using App.Domain.Reference;
using App.Domain.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace App.Application.Ingestion;

/// <summary><c>NotDue</c>: a scheduled run found, under the lock, that the source is inactive or was fetched meanwhile.</summary>
public enum RunStatus { Completed, NotFound, Busy, NotDue }

/// <summary><see cref="Run"/> is set when <see cref="Status"/> is <see cref="RunStatus.Completed"/> (also for a failed run).</summary>
public sealed record RunResult(RunStatus Status, RunDto? Run = null);

/// <summary>
/// One ingestion run of one source (design.md §2): fetch, parse, look each item up by fingerprint, store candidates,
/// record the run and the source's health. Candidates, run and source bookkeeping are saved together at the end, so a
/// failed run leaves no half-processed feed behind.
/// All five lookup rows, the AI step (T026), validation (T017), the duplicate check (T028) and auto-publish (T049) are implemented.
/// </summary>
public sealed class IngestionService(
    IAppDbContext db,
    IClock clock,
    TimeProvider wallClock,
    IAuditWriter audit,
    AiEnricher ai,
    DuplicateDetector duplicates,
    AutoPublisher autoPublisher,
    IEnumerable<ISourceAdapter> adapters,
    SourceRunLock locks,
    ILogger<IngestionService> log)
{
    /// <summary>A manual run waits this long for a run in progress, then gives up (ADR-015).</summary>
    public static readonly TimeSpan ManualLockWait = TimeSpan.FromSeconds(30);

    private const int MaxErrorLength = 1000;
    private const int MaxAiAttempts = 3;

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
        if (trigger == JobTrigger.Schedule && !source.IsDue(wallClock.GetUtcNow())) return new(RunStatus.NotDue);

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
            // A reviewer or the expiration job changing a staged item while the run was busy is a race, not a source failure;
            // the next run starts again from the same feed (ADR-039).
            await TryMarkCrashedAsync(source.Id, run.Id, countAsFailure: e is not (OperationCanceledException or DbUpdateConcurrencyException));
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
                    .SetProperty(x => x.LastFetchedAt, clock.UtcNow) // not due again until the poll interval has passed
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
        // Unreachable is the closest contract code for "nothing can fetch this source": a fixture source is
        // refused when fixtures are disallowed (T013 note).
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
        var published = await LoadPublishedAsync(fingerprints, ct);
        var resolver = await ReferenceResolver.LoadAsync(db, ct);
        var pending = await duplicates.LoadPendingAsync(ct);
        var verified = new List<PublishedLink>();
        var publishedInRun = new List<PublishedRef>();
        var auto = new AutoPublishRun();
        var delisted = new HashSet<Guid>();
        var delistedAt = autoPublisher.DelistedVerifiedAt(fetchedAt);
        await ai.BeginRunAsync(ct);
        var now = clock.UtcNow;

        foreach (var item in items)
        {
            // REQ-026: counted in `received` only; a later fetch takes the event once it is inside the window.
            if (autoPublisher.IsBeyondHorizon(source, item, fetchedAt)) continue;

            known.TryGetValue(item.Fingerprint, out var existing);
            existing ??= [];
            // A person said no to this listing before: a changed version of it is reviewed again, never published by the system.
            var personRejected = existing.Any(c => c.Status == CandidateStatus.Rejected && c.RejectedByUserId is not null);

            // Row 1: a pending or auto-rejected candidate is the one to compare with (also for a repeat inside one feed).
            var open = existing
                .Where(c => c.Status == CandidateStatus.Pending || (c.Status == CandidateStatus.Rejected && c.RejectedByUserId is null))
                .OrderByDescending(c => c.CreatedAt).ThenBy(c => c.Id).FirstOrDefault();
            var retryAi = false;
            if (open is not null && open.ContentHash == item.ContentHash)
            {
                // ADR-036: unchanged content, but the AI failed earlier: try again (bounded), still counted unchanged.
                retryAi = await NeedsAiRetryAsync(open, item, ct);
                if (!retryAi)
                {
                    run.Unchanged++;
                    continue;
                }
            }

            Guid? matchedItemId = null;
            if (open is null)
            {
                // Row 2: a person rejected exactly this content.
                if (existing.Any(c => c.Status == CandidateStatus.Rejected && c.ContentHash == item.ContentHash))
                {
                    run.Unchanged++;
                    continue;
                }

                // Rows 3-4: a published, expired or withdrawn item (also one this source was merged into).
                // The fingerprint ignores the kind: a feed that turns an event into a deal is a new item, not a change.
                if (published.TryGetValue(item.Fingerprint, out var linked) && linked.Kind == item.Kind)
                {
                    var last = existing.Where(c => c.Status is CandidateStatus.Published or CandidateStatus.Merged)
                        .OrderByDescending(c => c.UpdatedAt).FirstOrDefault();
                    if (linked.Status == ItemStatus.Withdrawn) { run.Unchanged++; continue; }
                    if (last?.ContentHash == item.ContentHash)
                    {
                        verified.Add(linked);
                        run.Unchanged++;
                        continue;
                    }
                    matchedItemId = linked.Id;
                }
            }
            else
            {
                matchedItemId = open.MatchedItemId;
            }

            // Only items that are stored or updated reach the AI, so unchanged feeds cost nothing.
            var id = open?.Id ?? Guid.NewGuid();
            var (validation, enrichment) = await EnrichAndValidateAsync(
                id, item, resolver, fetchedAt, source.IsUnreliable, retryAi ? open!.Fields : null, ct);
            var match = await duplicates.ApplyAsync(validation, item.Kind, id, source.Id, matchedItemId, pending, ct, publishedInRun);
            validation = match.Validation;
            var fields = validation.Fields;

            // REQ-045: the shop still lists its own published deal, but no longer with enough off: it stops being today's deal
            // now, not when it goes stale (its LastVerifiedAt moves back to the start of the stale window; the status is a
            // person's to change). A change it comes back with is a new candidate on the same item.
            if (matchedItemId is { } matchedId && source.AutoPublish && item.Kind == ItemKind.Deal && fields.ValidUntil is null
                && published.TryGetValue(item.Fingerprint, out var own) && own.Id == matchedId && own.PrimarySourceId == source.Id
                && autoPublisher.IsBelowDealThreshold(fields))
                delisted.Add(matchedId);

            if (open is not null)
            {
                open.Kind = item.Kind;
                open.ExternalId = item.ExternalId;
                open.ContentHash = item.ContentHash;
                open.Fields = fields;
                ApplyValidation(open, validation, enrichment, match.Duplicate);
                open.Evidence = item.Evidence;
                open.FetchedAt = fetchedAt;
                open.UpdatedAt = now;
                if (retryAi) run.Unchanged++; else run.Updated++;
                await AutoPublishAsync(source, open, personRejected, published, publishedInRun, auto, ct);
            }
            else
            {
                var candidate = new Candidate
                {
                    Id = id,
                    Kind = item.Kind,
                    SourceId = source.Id,
                    ExternalId = item.ExternalId,
                    SourceUrl = item.SourceUrl,
                    Fingerprint = item.Fingerprint,
                    ContentHash = item.ContentHash,
                    Fields = fields,
                    Evidence = item.Evidence,
                    MatchedItemId = matchedItemId,
                    IsSynthetic = source.Type == SourceType.Fixture,
                    FetchedAt = fetchedAt,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                ApplyValidation(candidate, validation, enrichment, match.Duplicate);
                db.Candidates.Add(candidate);
                known[item.Fingerprint] = [.. existing, candidate];
                run.Created++;
                await AutoPublishAsync(source, candidate, personRejected, published, publishedInRun, auto, ct);
            }
        }

        // A targeted update (not the tracked entity): a reviewer editing the item meanwhile must not fail the whole run.
        // Items staged by auto-publish are tracked and get their LastVerifiedAt from the approval; touching their row first
        // would change xmin and fail the run's save with a concurrency error.
        var eventIds = verified.Where(v => v.Kind == ItemKind.Event && !auto.Staged.Contains(v.Id)).Select(v => v.Id).Distinct().ToList();
        var dealIds = verified.Where(v => v.Kind == ItemKind.Deal && !auto.Staged.Contains(v.Id)).Select(v => v.Id).Distinct().ToList();
        if (eventIds.Count > 0)
            await db.Events.Where(e => eventIds.Contains(e.Id) && e.Status != ItemStatus.Withdrawn)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.LastVerifiedAt, fetchedAt), ct);
        if (dealIds.Count > 0)
            await db.Deals.Where(d => dealIds.Contains(d.Id) && d.Status != ItemStatus.Withdrawn)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.LastVerifiedAt, fetchedAt), ct);
        if (delisted.Count > 0)
        {
            var ended = await db.Deals
                .Where(d => delisted.Contains(d.Id) && d.Status == ItemStatus.Published && d.ValidUntil == null && d.LastVerifiedAt > delistedAt)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.LastVerifiedAt, delistedAt), ct);
            if (ended > 0) log.LogInformation("{Count} listing deal(s) of source {SourceId} are no longer today's deals", ended, source.Id);
        }
    }

    /// <summary>
    /// Auto-publish (REQ-023, 024, 044): approves the candidate as the system when its source allows it and the rules say so.
    /// The new item joins <paramref name="published"/> and <paramref name="publishedInRun"/>, so a repeat of the same listing in
    /// this feed is an unchanged item and a similar one is flagged as a duplicate, as after a person's approval.
    /// </summary>
    private async Task AutoPublishAsync(
        Source source, Candidate candidate, bool personRejected, Dictionary<string, PublishedLink> published,
        List<PublishedRef> publishedInRun, AutoPublishRun auto, CancellationToken ct)
    {
        if (personRejected || !source.AutoPublish) return;
        if (auto.Staged.Count >= autoPublisher.MaxPerRun)
        {
            if (!auto.LimitLogged)
                log.LogWarning("Auto-publish limit of {Limit} items per run reached; the rest stays pending", autoPublisher.MaxPerRun);
            auto.LimitLogged = true;
            return;
        }

        // A change is applied automatically only to an item this source published itself: an item that was merged
        // into someone else's is not the source's to rewrite (a merge says "same item", not "may edit").
        if (candidate.MatchedItemId is { } matchedId
            && !(published.TryGetValue(candidate.Fingerprint, out var link) && link.Id == matchedId && link.PrimarySourceId == source.Id))
            return;

        if (await autoPublisher.TryPublishAsync(source, candidate, ct) is not { } itemId) return;
        auto.Staged.Add(itemId);
        published[candidate.Fingerprint] = new(candidate.Kind, itemId, ItemStatus.Published, source.Id);
        publishedInRun.Add(new(itemId, candidate.Kind, candidate.Fields));
    }

    /// <summary>A published, expired or withdrawn item reachable by fingerprint through its <c>ItemSource</c> rows.</summary>
    private sealed record PublishedLink(ItemKind Kind, Guid Id, ItemStatus Status, Guid PrimarySourceId);

    /// <summary>What auto-publish did in one run: the items staged (counted against the limit) and whether the limit was logged.</summary>
    private sealed class AutoPublishRun
    {
        public HashSet<Guid> Staged { get; } = [];
        public bool LimitLogged { get; set; }
    }

    private async Task<Dictionary<string, PublishedLink>> LoadPublishedAsync(List<string> fingerprints, CancellationToken ct)
    {
        var sources = await db.ItemSources.AsNoTracking().Where(s => fingerprints.Contains(s.Fingerprint)).ToListAsync(ct);
        var eventIds = sources.Where(s => s.ItemKind == ItemKind.Event).Select(s => s.ItemId).Distinct().ToList();
        var dealIds = sources.Where(s => s.ItemKind == ItemKind.Deal).Select(s => s.ItemId).Distinct().ToList();
        var events = await db.Events.AsNoTracking().Where(e => eventIds.Contains(e.Id)).Select(e => new { e.Id, e.Status, e.PrimarySourceId }).ToDictionaryAsync(e => e.Id, ct);
        var deals = await db.Deals.AsNoTracking().Where(d => dealIds.Contains(d.Id)).Select(d => new { d.Id, d.Status, d.PrimarySourceId }).ToDictionaryAsync(d => d.Id, ct);

        var result = new Dictionary<string, PublishedLink>();
        foreach (var s in sources.OrderBy(s => s.AddedAt))
        {
            if (s.ItemKind == ItemKind.Event && events.TryGetValue(s.ItemId, out var e))
                result[s.Fingerprint] = new(ItemKind.Event, e.Id, e.Status, e.PrimarySourceId);
            else if (s.ItemKind == ItemKind.Deal && deals.TryGetValue(s.ItemId, out var d))
                result[s.Fingerprint] = new(ItemKind.Deal, d.Id, d.Status, d.PrimarySourceId);
        }
        return result;
    }

    /// <summary>ADR-036: an open candidate that still has <c>AiUnavailable</c> and fewer than 3 stored AI calls is sent to the AI again.</summary>
    private async Task<bool> NeedsAiRetryAsync(Candidate open, NormalizedCandidate item, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(item.Text) || !open.Issues.Any(i => i.Code == IssueCodes.AiUnavailable) || !ai.CanCall) return false;
        return await db.AiCalls.CountAsync(a => a.CandidateId == open.Id, ct) < MaxAiAttempts;
    }

    /// <summary>AI step, then the validation rules, then the issues that come from the AI step (design.md §2 order).</summary>
    private async Task<(ValidationResult Validation, AiEnrichment Enrichment)> EnrichAndValidateAsync(
        Guid candidateId, NormalizedCandidate item, ReferenceResolver resolver, DateTimeOffset fetchedAt, bool unreliable,
        CandidateFields? current, CancellationToken ct)
    {
        // A retry starts from the stored fields so that a reviewer's edits survive.
        var enrichment = await ai.EnrichAsync(candidateId, item.Kind, item.Text, resolver.Resolve(item.Kind, current ?? item.Fields), resolver, ct);
        var validation = CandidateValidator.Validate(item.Kind, enrichment.Fields, fetchedAt, unreliable).WithIssues(enrichment.Issues);

        // Without a provider nothing could have filled a gap: say so next to the gap (design.md §6, RSS items).
        if (enrichment.Skipped && validation.Issues.Any(i => i.Code is IssueCodes.MissingStartsAt or IssueCodes.MissingPrice
                || (i.Code == IssueCodes.UnknownCity && enrichment.Fields.CityText is null)))
            validation = validation.WithIssues([new(IssueCodes.AiUnavailable, IssueSeverity.Review, null, "No AI provider is enabled to fill the missing fields from the text.")]);
        return (validation, enrichment);
    }

    /// <summary>Re-validation of an open candidate: an auto-rejected one may become pending again and vice versa (no reviewer involved).</summary>
    private static void ApplyValidation(Candidate c, ValidationResult v, AiEnrichment ai, DuplicateMatch? duplicate)
    {
        c.Validation = v.Outcome;
        c.Issues = v.Issues;
        c.Status = v.Status;
        c.DuplicateOfKind = duplicate?.Kind;
        c.DuplicateOfId = duplicate?.Id;
        c.DuplicateOfIsCandidate = duplicate?.IsCandidate;

        // The AI fields belong to the content just processed: a changed item without AI output must not keep the old ones.
        c.SuggestedCategoryId = ai.SuggestedCategoryId;
        c.AiModel = ai.Metadata?.Model;
        c.AiRequestId = ai.Metadata?.RequestId;
        c.AiConfidence = ai.Metadata?.Confidence;
        c.AiProcessedAt = ai.Metadata?.ProcessedAt;
        c.AiSuggestedFields = ai.Metadata?.SuggestedFields.ToList() ?? [];
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
