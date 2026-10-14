using App.Application.Candidates;
using App.Application.Public;
using App.Application.Sources;
using App.Domain.Content;
using App.Domain.Reference;
using App.Domain.Sources;
using Microsoft.Extensions.Logging;

namespace App.Application.Ingestion;

/// <summary>Settings from the <c>AutoPublish__*</c> environment variables (design.md §2).</summary>
public sealed class AutoPublishOptions
{
    public const string Section = "AutoPublish";

    /// <summary>A daily deal is published automatically only with at least this discount in percent (REQ-044, ADR-042).</summary>
    public int MinDealDiscountPercent { get; set; } = 35;

    /// <summary>
    /// At most this many items are published automatically per run; the rest stay pending for a person. A feed that
    /// suddenly lists thousands of "valid" items must not flood the public site unreviewed.
    /// </summary>
    public int MaxPerRun { get; set; } = 100;
}

/// <summary>
/// The auto-publish step of an ingestion run (REQ-023, 024, 026, 044; ADR-039, ADR-042): the horizon check before an item is
/// stored, and the system approval after validation and the duplicate check. It publishes through <see cref="CandidateApproval"/>,
/// the code path a person's approval uses, but leaves saving to the run.
/// </summary>
public sealed class AutoPublisher(
    CandidateApproval approval, IngestionOptions ingestion, AutoPublishOptions options, DealOptions deals, ILogger<AutoPublisher> log)
{
    /// <summary>Items one run may publish automatically.</summary>
    public int MaxPerRun => options.MaxPerRun;

    /// <summary>
    /// REQ-045: a listing deal (no <c>validUntil</c>) that a fetch lists with both prices and less than the minimum discount
    /// is no longer today's deal. A missing price is not a lower discount (a layout glitch must not end every deal).
    /// </summary>
    public bool IsBelowDealThreshold(CandidateFields f) =>
        f.OldPrice is not null && f.NewPrice is not null
        && !(ContentRules.DiscountPercent(f.OldPrice, f.NewPrice) >= options.MinDealDiscountPercent);

    /// <summary>
    /// The <c>LastVerifiedAt</c> that makes a listing deal inactive at once: the start of its stale window. The status stays
    /// <c>Published</c>, so a person's "Skado" (<c>Expired</c>) remains distinguishable and the next fetch that lists the
    /// deal with enough off activates it again (ADR-045).
    /// </summary>
    public DateTimeOffset DelistedVerifiedAt(DateTimeOffset fetchedAt) => fetchedAt - deals.ListingWindow;

    /// <summary>REQ-026: an event of an auto-publish source that starts after the horizon waits for a later fetch.</summary>
    public bool IsBeyondHorizon(Source source, NormalizedCandidate item, DateTimeOffset fetchedAt) =>
        source.AutoPublish && item.Kind == ItemKind.Event && item.Fields.EventStart() is { } start
        && ContentRules.IsBeyondHorizon(start, fetchedAt, ingestion.EventHorizonDays);

    /// <summary>The id of the item the candidate was published as, or null when it stays for a person.</summary>
    public async Task<Guid?> TryPublishAsync(Source source, Candidate candidate, CancellationToken ct)
    {
        if (!source.AutoPublish
            || !AutoPublishRules.CanPublish(candidate, ingestion.EventHorizonDays, options.MinDealDiscountPercent)
            || !IsOnSourceHost(source, candidate.Fields.Url))
            return null;
        var result = await approval.StageAutoPublishAsync(candidate, ct);
        if (result.Status == ApprovalStatus.Done) return result.ItemId;

        // Valid at ingestion but not publishable (a field the edit rules refuse, a linked item that is gone): stays pending.
        log.LogWarning("Auto-publish of candidate {CandidateId} skipped: {Status} {Fields}",
            candidate.Id, result.Status, result.Errors is null ? "" : string.Join(",", result.Errors.Keys));
        return null;
    }

    /// <summary>
    /// The public "Burimi" link is shown under the source's name, so an unreviewed item must link to the source's own site
    /// (the configured host, its subdomains or its parent domain) and carry no credentials. Sources without a location
    /// (fixtures) have nothing to compare with.
    /// </summary>
    public static bool IsOnSourceHost(Source source, string? url)
    {
        if (source.BaseUrl is null) return true;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var item) || !string.IsNullOrEmpty(item.UserInfo)) return false;
        if (!Uri.TryCreate(source.BaseUrl, UriKind.Absolute, out var origin)) return false;
        var a = Bare(item.IdnHost);
        var b = Bare(origin.IdnHost);
        return a == b || a.EndsWith("." + b, StringComparison.Ordinal) || b.EndsWith("." + a, StringComparison.Ordinal);
    }

    private static string Bare(string host)
    {
        var h = host.ToLowerInvariant();
        return h.StartsWith("www.", StringComparison.Ordinal) ? h[4..] : h;
    }
}
