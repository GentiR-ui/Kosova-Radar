using Microsoft.AspNetCore.Identity;

namespace App.Infrastructure.Identity;

/// <summary>Admin or Reviewer account. The role is an Identity role (exactly one per user).</summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
}
