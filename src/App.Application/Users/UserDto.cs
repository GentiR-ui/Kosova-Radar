namespace App.Application.Users;

/// <summary>design.md §5. <c>Role</c> is the wire format ("admin" | "reviewer").</summary>
public sealed record UserDto(Guid Id, string Email, string Role, bool IsActive, DateTimeOffset CreatedAt);

/// <summary>What the audit log keeps about a user: never credentials.</summary>
public sealed record UserAuditState(string Email, string Role, bool IsActive);
