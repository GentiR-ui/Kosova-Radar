using App.Application.Ai;

namespace App.Infrastructure.Ai;

/// <summary>Production without an API key (ADR-007): the pipeline does not call it.</summary>
internal sealed class NoAiProvider : IAiProvider
{
    public string Name => "None";
    public bool IsEnabled => false;

    public Task<AiExtractionResult> ExtractAsync(AiExtractionRequest request, CancellationToken ct) =>
        throw new InvalidOperationException("No AI provider is configured.");
}
