using App.Application.Abstractions;
using App.Application.Audit;
using App.Application.Auth;
using App.Application.Users;
using App.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Identity;

public enum CreateUserStatus { Created, EmailTaken, Invalid }

public sealed record CreateUserResult(CreateUserStatus Status, UserDto? User = null, IReadOnlyDictionary<string, string[]>? Errors = null);

public enum DeactivateUserStatus { Deactivated, NotFound, Self }

/// <summary>Admin user management (REQ-001..004). Each change and its audit entry commit together.</summary>
public sealed class UserAdmin(
    AppDbContext db, UserManager<AppUser> users, IAuditWriter audit, ICurrentUser current, IClock clock)
{
    public async Task<List<UserDto>> ListAsync(CancellationToken ct)
    {
        var rows = await (from u in db.Users
                          join ur in db.UserRoles on u.Id equals ur.UserId
                          join r in db.Roles on ur.RoleId equals r.Id
                          orderby u.Email
                          select new { u.Id, u.Email, Role = r.Name, u.IsActive, u.CreatedAt })
            .AsNoTracking().ToListAsync(ct);
        return rows.Select(r => new UserDto(r.Id, r.Email!, Roles.ToWire(r.Role!), r.IsActive, r.CreatedAt)).ToList();
    }

    /// <param name="role">Identity role name (<see cref="Roles.Admin"/> or <see cref="Roles.Reviewer"/>).</param>
    public async Task<CreateUserResult> CreateAsync(string email, string password, string role, CancellationToken ct)
    {
        if (await users.FindByEmailAsync(email) is not null) return new(CreateUserStatus.EmailTaken);

        var user = new AppUser { Id = Guid.NewGuid(), UserName = email, Email = email, IsActive = true, CreatedAt = clock.UtcNow };
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            // The audit entry is saved by the SaveChanges inside CreateAsync, in the same transaction as the user and its role.
            audit.Record(AuditActions.UserCreate, AuditEntityTypes.User, user.Id,
                after: new UserAuditState(email, Roles.ToWire(role), true));
            var created = await users.CreateAsync(user, password);
            if (!created.Succeeded) return Rejected(created);
            var assigned = await users.AddToRoleAsync(user, role);
            if (!assigned.Succeeded)
                throw new InvalidOperationException("Assigning the role failed: " + string.Join("; ", assigned.Errors.Select(e => e.Code)));
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            await tx.RollbackAsync(ct); // an aborted PostgreSQL transaction rejects every further statement
            if (!await EmailExistsAsync(email, ct)) throw;
            return new(CreateUserStatus.EmailTaken); // lost a race against the same email; the unique index held
        }
        return new(CreateUserStatus.Created, new UserDto(user.Id, email, Roles.ToWire(role), true, user.CreatedAt));
    }

    public async Task<DeactivateUserStatus> DeactivateAsync(Guid id, CancellationToken ct)
    {
        if (id == current.Require().Id) return DeactivateUserStatus.Self;
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null) return DeactivateUserStatus.NotFound;
        if (!user.IsActive) return DeactivateUserStatus.Deactivated; // already done: idempotent, nothing to audit

        var role = Roles.ToWire((await users.GetRolesAsync(user)).Single());
        audit.Record(AuditActions.UserDeactivate, AuditEntityTypes.User, user.Id,
            before: new UserAuditState(user.Email!, role, true), after: new UserAuditState(user.Email!, role, false));
        user.IsActive = false;
        // Rotating the security stamp ends the user's sessions on their next request (REQ-003);
        // the save inside also persists IsActive and the audit entry.
        var result = await users.UpdateSecurityStampAsync(user);
        if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.ConcurrencyFailure)))
        {
            // Another admin changed the user at the same moment; if they deactivated it, the outcome is the same (idempotent).
            db.ChangeTracker.Clear();
            if (await db.Users.AnyAsync(u => u.Id == id && !u.IsActive, ct)) return DeactivateUserStatus.Deactivated;
        }
        if (!result.Succeeded)
            throw new InvalidOperationException("Deactivating the user failed: " + string.Join("; ", result.Errors.Select(e => e.Code)));
        return DeactivateUserStatus.Deactivated;
    }

    private Task<bool> EmailExistsAsync(string email, CancellationToken ct)
    {
        db.ChangeTracker.Clear(); // the failed save left the new user in the tracker
        return db.Users.AnyAsync(u => u.NormalizedEmail == users.NormalizeEmail(email), ct);
    }

    private static CreateUserResult Rejected(IdentityResult result)
    {
        if (result.Errors.Any(e => e.Code is nameof(IdentityErrorDescriber.DuplicateEmail) or nameof(IdentityErrorDescriber.DuplicateUserName)))
            return new(CreateUserStatus.EmailTaken);
        var errors = result.Errors
            .GroupBy(e => e.Code.StartsWith("Password", StringComparison.Ordinal) ? "password" : "email")
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());
        return new(CreateUserStatus.Invalid, Errors: errors);
    }
}
