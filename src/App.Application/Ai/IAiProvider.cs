using App.Domain.Content;
using App.Domain.Reference;

namespace App.Application.Ai;

/// <summary>What a provider is asked to extract (design.md §6). <see cref="Categories"/> are the category slugs of the item's kind.</summary>
public sealed record AiExtractionRequest(
    ItemKind Kind, string Text, CandidateFields KnownFields, IReadOnlyList<string> Categories);

/// <summary>Field values found in the text; null = not stated. Which of them apply depends on the item kind (see <see cref="AiFieldMerger"/>).</summary>
public sealed record AiExtractedFields
{
    public DateTimeOffset? StartsAt { get; init; }
    public DateTimeOffset? EndsAt { get; init; }
    public string? City { get; init; }
    public string? Venue { get; init; }
    public string? PriceText { get; init; }
    public decimal? PriceAmount { get; init; }
    public string? Currency { get; init; }
    public string? Merchant { get; init; }
    public decimal? OldPrice { get; init; }
    public decimal? NewPrice { get; init; }
    public DateTimeOffset? ValidFrom { get; init; }
    public DateTimeOffset? ValidUntil { get; init; }
}

public sealed record AiExtractionResult(
    string Model,
    string? RequestId,
    double Confidence,
    AiExtractedFields Fields,
    string? CategorySlug = null,
    bool? IsKosovoRelevant = null,
    string? Summary = null,
    int? InputTokens = null,
    int? OutputTokens = null);

/// <summary>
/// Vendor-neutral AI extraction (ADR-007). A provider returns what it found; the pipeline decides what to trust
/// (<see cref="AiOutputValidator"/>, <see cref="AiFieldMerger"/>). Any exception counts as a provider failure.
/// </summary>
public interface IAiProvider
{
    /// <summary>Name stored on every <see cref="AiCall"/> (<c>Gemini</c>, <c>Anthropic</c>, <c>Fake</c>, <c>None</c>).</summary>
    string Name { get; }

    /// <summary>False for the <c>None</c> provider: the pipeline does not call it.</summary>
    bool IsEnabled { get; }

    Task<AiExtractionResult> ExtractAsync(AiExtractionRequest request, CancellationToken ct);
}

/// <summary>A provider throws this when the response does not match the extraction schema (recorded as <see cref="AiErrorCodes.InvalidOutput"/>).</summary>
public sealed class AiInvalidOutputException(string message) : Exception(message);

/// <summary>Settings from the <c>Ai__*</c> environment variables (design.md §2).</summary>
public sealed class AiOptions
{
    public const string Section = "Ai";

    /// <summary><c>None</c>, <c>Fake</c>, <c>Gemini</c> or <c>Anthropic</c>; empty = <c>Gemini</c> when <see cref="ApiKey"/> is set, else <c>None</c> (ADR-034).</summary>
    public string? Provider { get; set; }

    public string? ApiKey { get; set; }

    /// <summary>The provider that is really used: <see cref="Provider"/>, else <c>Gemini</c> with an API key, else <c>None</c>.</summary>
    public string EffectiveProvider => string.IsNullOrWhiteSpace(Provider) ? (string.IsNullOrWhiteSpace(ApiKey) ? "None" : "Gemini") : Provider.Trim();

    /// <summary>Empty = the default model of the provider; registration fills it in.</summary>
    public string Model { get; set; } = "";

    /// <summary>Minimum spacing between two provider calls in this process (free-tier rate limits); 0 = none.</summary>
    public int MinIntervalMs { get; set; }

    /// <summary>Calls per UTC day, counted from <see cref="AiCall"/> rows; 0 = no calls at all.</summary>
    public int DailyCallLimit { get; set; } = 500;

    /// <summary>One call, including reading the response.</summary>
    public int TimeoutSeconds { get; set; } = 15;
}
