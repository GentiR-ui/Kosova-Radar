namespace App.Domain.Audit;

/// <summary>Append-only record of one audited action (REQ-072). Never updated or deleted.</summary>
public sealed class AuditLog
{
    /// <summary>Version 7 GUID: roughly time-ordered; used only as a tie-breaker for entries with the same <see cref="At"/>.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }

    /// <summary>The actor's email when the action happened (the log must stay readable if accounts change).</summary>
    public required string UserEmail { get; set; }

    public required string Action { get; set; }
    public required string EntityType { get; set; }
    public Guid EntityId { get; set; }

    /// <summary>JSON text <c>{ "before": ..., "after": ... }</c> in camelCase, stored as jsonb. Never contains passwords.</summary>
    public required string Changes { get; set; }

    public DateTimeOffset At { get; set; }
}
