using App.Application.Abstractions;
using App.Application.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace App.Infrastructure.Identity;

/// <summary>Ensures the roles exist and creates the first Admin when no user exists (REQ-P01).</summary>
public sealed class BootstrapAdmin(
    UserManager<AppUser> users,
    RoleManager<IdentityRole<Guid>> roles,
    IConfiguration config,
    IClock clock,
    ILogger<BootstrapAdmin> logger)
{
    public async Task EnsureAsync(CancellationToken ct = default)
    {
        foreach (var role in Roles.All)
        {
            if (!await roles.RoleExistsAsync(role))
                Check(await roles.CreateAsync(new IdentityRole<Guid>(role)), $"create role {role}");
        }

        if (await users.Users.AnyAsync(ct)) return;

        var email = config["Bootstrap:AdminEmail"];
        var password = config["Bootstrap:AdminPassword"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            const string message =
                "No user exists and Bootstrap__AdminEmail / Bootstrap__AdminPassword are not set. Set them to create the first Admin.";
            logger.LogCritical(message);
            throw new InvalidOperationException(message);
        }

        var admin = new AppUser { UserName = email, Email = email, IsActive = true, CreatedAt = clock.UtcNow };
        Check(await users.CreateAsync(admin, password), "create bootstrap admin");
        Check(await users.AddToRoleAsync(admin, Roles.Admin), "assign Admin role");
        logger.LogInformation("Created bootstrap admin {UserId}", admin.Id);
    }

    private static void Check(IdentityResult result, string what)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Bootstrap failed to {what}: {string.Join("; ", result.Errors.Select(e => e.Code))}");
    }
}
