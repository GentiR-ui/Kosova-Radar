using System.Collections.Concurrent;

namespace App.Application.Ingestion;

/// <summary>
/// One lock per source: runs of the same source never overlap (design.md §2). Manual runs wait for it,
/// scheduled runs pass a zero wait and skip a locked source.
/// </summary>
public sealed class SourceRunLock
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    /// <summary>Returns a handle to dispose when the run ends, or null when the lock was not free within <paramref name="wait"/>.</summary>
    public async Task<IDisposable?> TryAcquireAsync(Guid sourceId, TimeSpan wait, CancellationToken ct)
    {
        var gate = _locks.GetOrAdd(sourceId, _ => new SemaphoreSlim(1, 1));
        return await gate.WaitAsync(wait, ct) ? new Releaser(gate) : null;
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) gate.Release();
        }
    }
}
