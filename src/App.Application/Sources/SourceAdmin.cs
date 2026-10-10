using App.Application.Abstractions;
using App.Application.Audit;
using App.Domain.Content;
using App.Domain.Sources;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Sources;

public enum SourceSaveStatus { Saved, Invalid, NotFound }

public sealed record SourceSaveResult(SourceSaveStatus Status, SourceDto? Source = null, IReadOnlyDictionary<string, string[]>? Errors = null);

/// <summary>Admin source management (REQ-010, 011, 013, 073). Each change and its audit entry commit together.</summary>
public sealed class SourceAdmin(IAppDbContext db, IAuditWriter audit, IClock clock, IngestionOptions options)
{
    public async Task<List<SourceDto>> ListAsync(CancellationToken ct) =>
        // The list never shows the payload (up to 1 MB per source), so it is not loaded.
        (await db.Sources.AsNoTracking().Select(s => new Source
        {
            Id = s.Id,
            Name = s.Name,
            Type = s.Type,
            Adapter = s.Adapter,
            BaseUrl = s.BaseUrl,
            DefaultKind = s.DefaultKind,
            IsActive = s.IsActive,
            IsUnreliable = s.IsUnreliable,
            AutoPublish = s.AutoPublish,
            PollIntervalMinutes = s.PollIntervalMinutes,
            LastFetchedAt = s.LastFetchedAt,
            LastSuccessAt = s.LastSuccessAt,
            LastErrorAt = s.LastErrorAt,
            LastError = s.LastError,
            ConsecutiveFailures = s.ConsecutiveFailures,
        }).ToListAsync(ct))
        .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Id)
        .Select(SourceDto.From).ToList();

    public async Task<SourceDetailDto?> GetAsync(Guid id, CancellationToken ct)
    {
        // Runs first: a run's result and the source's bookkeeping commit together, so a finished run seen here is
        // always reflected in the source read next (the other order could show a finished run with lastFetchedAt null).
        var runs = await db.JobExecutions.AsNoTracking()
            .Where(j => j.SourceId == id && j.JobName == JobName.SourceFetch)
            .OrderByDescending(j => j.StartedAt).ThenByDescending(j => j.Id)
            .Take(10).ToListAsync(ct);
        var source = await db.Sources.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, ct);
        if (source is null) return null;
        return SourceDetailDto.From(source, runs.Select(RunDto.From).ToList());
    }

    public async Task<SourceSaveResult> CreateAsync(SourceRequest req, CancellationToken ct)
    {
        var (valid, errors) = SourceValidator.Validate(req, options.AllowFixtureSources);
        if (valid is null) return new(SourceSaveStatus.Invalid, Errors: errors);

        var source = new Source { Name = valid.Name, CreatedAt = clock.UtcNow };
        Apply(source, valid);
        db.Sources.Add(source);
        audit.Record(AuditActions.SourceCreate, AuditEntityTypes.Source, source.Id, after: SourceAuditState.From(source));
        await db.SaveChangesAsync(ct);
        return new(SourceSaveStatus.Saved, SourceDto.From(source));
    }

    public async Task<SourceSaveResult> UpdateAsync(Guid id, SourceRequest req, CancellationToken ct)
    {
        var source = await db.Sources.SingleOrDefaultAsync(s => s.Id == id, ct);
        if (source is null) return new(SourceSaveStatus.NotFound);
        var (valid, errors) = SourceValidator.Validate(req, options.AllowFixtureSources);
        if (valid is null) return new(SourceSaveStatus.Invalid, Errors: errors);

        var before = SourceAuditState.From(source);
        Apply(source, valid);
        audit.Record(AuditActions.SourceUpdate, AuditEntityTypes.Source, source.Id, before, SourceAuditState.From(source));
        await db.SaveChangesAsync(ct);
        return new(SourceSaveStatus.Saved, SourceDto.From(source));
    }

    // Run bookkeeping (LastFetchedAt, failures, ...) is owned by the ingestion run, never by an edit.
    private static void Apply(Source source, ValidSource v)
    {
        source.Name = v.Name;
        source.Type = v.Type;
        source.Adapter = v.Adapter;
        source.BaseUrl = v.BaseUrl;
        source.FixturePayload = v.FixturePayload;
        source.DefaultKind = v.DefaultKind;
        source.PollIntervalMinutes = v.PollIntervalMinutes;
        source.IsActive = v.IsActive;
        source.IsUnreliable = v.IsUnreliable;
        source.AutoPublish = v.AutoPublish;
    }
}
