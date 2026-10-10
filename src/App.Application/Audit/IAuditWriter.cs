namespace App.Application.Audit;

/// <summary>
/// Records an audited action. The entry joins the caller's unit of work: it is saved by the same
/// <c>SaveChangesAsync</c> as the change itself, so an action is never audited without happening (or the reverse).
/// </summary>
public interface IAuditWriter
{
    /// <summary>Actor id and email stored with system actions.</summary>
    public static readonly Guid SystemActor = Guid.Empty;

    public const string SystemEmail = "system";

    /// <summary>
    /// The acting user comes from the current request. <paramref name="before"/>/<paramref name="after"/> are
    /// serialized as camelCase JSON; any property whose name suggests a secret (password, token, apiKey, ...) is dropped.
    /// </summary>
    void Record(string action, string entityType, Guid entityId, object? before = null, object? after = null);

    /// <summary>
    /// For actions the system takes without a person (auto-publish, ADR-039): the actor is the fixed user "system"
    /// (<see cref="SystemActor"/>), so it also works in scheduled runs that have no request.
    /// </summary>
    void RecordAsSystem(string action, string entityType, Guid entityId, object? before = null, object? after = null);
}
