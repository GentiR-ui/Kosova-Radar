using App.Domain.Content;

namespace App.Application.Ai;

/// <summary>
/// Stores an <see cref="AiCall"/> at once, independently of the run's unit of work: the call was made and paid for even
/// when the run crashes later, and the daily limit counts these rows.
/// </summary>
public interface IAiLedger
{
    Task RecordAsync(AiCall call);
}
