namespace App.Domain.Content;

/// <summary>One call to the AI provider, kept for cost control and audit (design.md §4). Never stores the prompt or the text.</summary>
public sealed class AiCall
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The candidate the call was made for; no foreign key (the row is a ledger, saved at once and apart from the candidate).</summary>
    public Guid CandidateId { get; set; }

    public required string Provider { get; set; }
    public required string Model { get; set; }
    public string? RequestId { get; set; }
    public bool Success { get; set; }

    /// <summary><see cref="AiErrorCodes"/> when <see cref="Success"/> is false.</summary>
    public string? ErrorCode { get; set; }

    public double? Confidence { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }

    /// <summary>Wall clock, so the daily limit counts real days also under the X-Test-Now hook.</summary>
    public DateTimeOffset StartedAt { get; set; }

    public int DurationMs { get; set; }
}

/// <summary>Error codes stored on a failed <see cref="AiCall"/>.</summary>
public static class AiErrorCodes
{
    public const string Timeout = "Timeout";
    public const string ProviderError = "ProviderError";
    public const string InvalidOutput = "InvalidOutput";
}
