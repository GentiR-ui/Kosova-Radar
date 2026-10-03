using System.Text.Json;
using App.Application.Abstractions;
using App.Application.Common;
using App.Domain.Audit;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Audit;

public sealed record AuditChangesDto(JsonElement? Before, JsonElement? After);

public sealed record AuditEntryDto(
    Guid Id, Guid UserId, string UserEmail, string Action, string EntityType, Guid EntityId,
    DateTimeOffset At, AuditChangesDto Changes);

public static class AuditQueries
{
    /// <summary>Newest first (ties broken by the time-ordered id), optionally for one entity.</summary>
    public static async Task<PagedResult<AuditEntryDto>> ListAsync(
        IAppDbContext db, Guid? entityId, int page, int pageSize, CancellationToken ct)
    {
        var query = db.AuditLogs.AsNoTracking();
        if (entityId is { } id) query = query.Where(a => a.EntityId == id);
        var total = await query.CountAsync(ct);
        var offset = (long)(page - 1) * pageSize; // long: a huge page number must not overflow
        var rows = offset >= total ? [] : await query.OrderByDescending(a => a.At).ThenByDescending(a => a.Id)
            .Skip((int)offset).Take(pageSize).ToListAsync(ct);
        return new PagedResult<AuditEntryDto>(rows.Select(ToDto).ToList(), page, pageSize, total);
    }

    private static AuditEntryDto ToDto(AuditLog a)
    {
        using var doc = JsonDocument.Parse(a.Changes);
        var root = doc.RootElement;
        return new AuditEntryDto(a.Id, a.UserId, a.UserEmail, a.Action, a.EntityType, a.EntityId, a.At,
            new AuditChangesDto(Value(root, "before"), Value(root, "after")));
    }

    private static JsonElement? Value(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v.Clone() : null;
}
