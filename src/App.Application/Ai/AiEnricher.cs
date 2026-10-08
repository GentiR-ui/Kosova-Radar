using System.Diagnostics;
using App.Application.Abstractions;
using App.Application.Ingestion;
using App.Domain.Content;
using App.Domain.Reference;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace App.Application.Ai;

/// <summary>What the AI recorded on a candidate (design.md §5 <c>ai</c> block).</summary>
public sealed record AiMetadata(
    string Model, string? RequestId, double Confidence, DateTimeOffset ProcessedAt, IReadOnlyList<string> SuggestedFields);

/// <summary>
/// Result of the AI step for one item. <see cref="Issues"/> are added to the validation result;
/// <see cref="Skipped"/> means there was text but no provider (the pipeline then flags what only the AI could have filled).
/// </summary>
public sealed record AiEnrichment(
    CandidateFields Fields, IReadOnlyList<Issue> Issues, Guid? SuggestedCategoryId, AiMetadata? Metadata, bool Skipped = false)
{
    public static AiEnrichment Unchanged(CandidateFields fields, bool skipped = false) => new(fields, [], null, null, skipped);
}

/// <summary>
/// The AI step of the ingestion pipeline (design.md §2, §6; REQ-040..043): fills fields that are still null, suggests a
/// category, and on any failure (provider error, timeout, invalid output, daily limit) leaves the fields untouched and
/// reports <c>AiUnavailable</c>. Every call is stored as an <see cref="AiCall"/> through the ledger, at once and apart from the run.
/// </summary>
public sealed class AiEnricher(
    IAppDbContext db, IAiProvider provider, IAiLedger ledger, AiOptions options, TimeProvider wallClock, ILogger<AiEnricher> log)
{
    private int _callsToday;
    private bool _limitLogged;

    /// <summary>Counts today's stored calls (UTC day); call once at the start of a run.</summary>
    public async Task BeginRunAsync(CancellationToken ct)
    {
        if (!provider.IsEnabled) return;
        var dayStart = new DateTimeOffset(wallClock.GetUtcNow().UtcDateTime.Date, TimeSpan.Zero);
        _limitLogged = false;
        _callsToday = await db.AiCalls.CountAsync(c => c.StartedAt >= dayStart, ct);
    }

    /// <summary>True when a provider is enabled and today's daily limit still has room.</summary>
    public bool CanCall => provider.IsEnabled && _callsToday < options.DailyCallLimit;

    public async Task<AiEnrichment> EnrichAsync(
        Guid candidateId, ItemKind kind, string? text, CandidateFields fields, ReferenceResolver resolver, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return AiEnrichment.Unchanged(fields);
        if (!provider.IsEnabled) return AiEnrichment.Unchanged(fields, skipped: true);
        if (_callsToday >= options.DailyCallLimit)
        {
            if (!_limitLogged) log.LogWarning("AI daily call limit of {Limit} reached", options.DailyCallLimit);
            _limitLogged = true;
            return Unavailable(fields);
        }

        var categories = resolver.CategorySlugs(kind);
        var call = new AiCall
        {
            CandidateId = candidateId,
            Provider = provider.Name,
            Model = options.Model,
            StartedAt = wallClock.GetUtcNow(),
        };
        _callsToday++;

        var watch = Stopwatch.StartNew();
        AiExtractionResult? result = null;
        string? invalid = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
            // The token is cooperative; WaitAsync makes the limit hard for a provider that ignores it.
            result = await provider.ExtractAsync(new AiExtractionRequest(kind, text, fields, categories), timeout.Token)
                .WaitAsync(TimeSpan.FromSeconds(options.TimeoutSeconds), ct);
            invalid = AiOutputValidator.Validate(result, categories);
            if (invalid is not null) call.ErrorCode = AiErrorCodes.InvalidOutput;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // the run was cancelled, not the provider slow
        }
        catch (Exception e) when (e is OperationCanceledException or TimeoutException)
        {
            call.ErrorCode = AiErrorCodes.Timeout;
        }
        catch (AiInvalidOutputException e)
        {
            call.ErrorCode = AiErrorCodes.InvalidOutput;
            log.LogWarning("AI output rejected: {Reason}", e.Message);
        }
        catch (Exception e)
        {
            call.ErrorCode = AiErrorCodes.ProviderError;
            log.LogWarning(e, "AI provider failed");
        }
        call.DurationMs = (int)watch.ElapsedMilliseconds;

        if (result is not null)
        {
            // Recorded also when the answer is unusable: the call was made and paid for.
            call.Model = result.Model.Length <= 120 ? result.Model : call.Model;
            call.RequestId = result.RequestId is { Length: <= 200 } id ? id : null;
            call.InputTokens = result.InputTokens;
            call.OutputTokens = result.OutputTokens;
        }

        if (result is null || invalid is not null)
        {
            if (invalid is not null) log.LogWarning("AI output violates the schema at {Field}", invalid);
            await ledger.RecordAsync(call);
            return Unavailable(fields);
        }

        call.Success = true;
        call.Confidence = result.Confidence;
        await ledger.RecordAsync(call);

        var merged = AiFieldMerger.Merge(kind, fields, result.Fields, resolver);
        var suggestedCategory = result.CategorySlug is { } slug ? resolver.CategoryId(kind, slug) : null;
        var meta = new AiMetadata(result.Model, result.RequestId, result.Confidence, wallClock.GetUtcNow(), merged.Suggested);
        return new(merged.Fields, merged.Conflicts, suggestedCategory, meta);
    }

    private static AiEnrichment Unavailable(CandidateFields fields) =>
        new(fields, [new(IssueCodes.AiUnavailable, IssueSeverity.Review, null,
            "The AI could not extract data from the text; fields it would have filled stay empty.")], null, null);
}
