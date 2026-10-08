using App.Application.Ai;

namespace App.Infrastructure.Ai;

/// <summary>
/// Keeps a minimum spacing between two calls of the wrapped provider in this process (<c>Ai__MinIntervalMs</c>, for
/// free-tier rate limits). Calls queue on the gate, so the caller's own timeout still applies while waiting.
/// </summary>
public sealed class ThrottledAiProvider(IAiProvider inner, TimeSpan minInterval, TimeProvider time) : IAiProvider
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _lastStart = long.MinValue;

    public string Name => inner.Name;
    public bool IsEnabled => inner.IsEnabled;

    public async Task<AiExtractionResult> ExtractAsync(AiExtractionRequest request, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_lastStart != long.MinValue)
            {
                var wait = minInterval - time.GetElapsedTime(_lastStart);
                if (wait > TimeSpan.Zero) await Task.Delay(wait, time, ct);
            }
            _lastStart = time.GetTimestamp();
        }
        finally
        {
            _gate.Release();
        }
        return await inner.ExtractAsync(request, ct);
    }
}
