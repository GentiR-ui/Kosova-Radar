using App.Application.Abstractions;
using App.Application.Audit;
using App.Domain.Audit;
using App.Infrastructure.Persistence;

namespace App.Infrastructure.Audit;

internal sealed class AuditWriter(AppDbContext db, ICurrentUser user, TimeProvider time) : IAuditWriter
{
    // Wall-clock time (not IClock): the log records when the action really happened, even under the X-Test-Now hook.
    public void Record(string action, string entityType, Guid entityId, object? before = null, object? after = null)
    {
        var (userId, email) = user.Require();
        Add(userId, email, action, entityType, entityId, before, after);
    }

    public void RecordAsSystem(string action, string entityType, Guid entityId, object? before = null, object? after = null) =>
        Add(IAuditWriter.SystemActor, IAuditWriter.SystemEmail, action, entityType, entityId, before, after);

    private void Add(Guid userId, string email, string action, string entityType, Guid entityId, object? before, object? after)
    {
        db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            UserEmail = email,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Changes = AuditChanges.Serialize(before, after),
            At = time.GetUtcNow(),
        });
    }
}
