using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using App.Api.Http;
using App.Application.Auth;
using App.Infrastructure.Identity;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace App.Api.Endpoints;

public sealed record LoginRequest(
    [property: Required, MaxLength(256)] string? Email,
    [property: Required, MaxLength(512)] string? Password);

public sealed record MeDto(Guid UserId, string Email, string Role);

public static class AuthEndpoints
{
    // Verified for unknown/inactive emails so every failed login costs one hash check (no timing oracle, REQ-P02).
    private static readonly string DummyHash = new PasswordHasher<AppUser>().HashPassword(new AppUser(), "dummy-password-for-timing");

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth").WithTags("Auth");
        auth.MapPost("/login", Login).AllowAnonymous(); // anonymous by definition; throttled by the login limiter
        auth.MapPost("/logout", Logout).RequireAuthorization();
        app.MapGet("/api/me", Me).RequireAuthorization().WithTags("Auth");
        return app;
    }

    // Same body for unknown email, wrong password and inactive user (REQ-P02).
    private static ProblemHttpResult InvalidCredentials() =>
        TypedResults.Problem(title: "Invalid credentials", detail: "Email or password is incorrect.",
            statusCode: StatusCodes.Status401Unauthorized);

    private static ProblemHttpResult TooManyAttempts() =>
        TypedResults.Problem(title: "Too many attempts", detail: "Too many failed sign-in attempts. Try again later.",
            statusCode: StatusCodes.Status429TooManyRequests);

    private static async Task<Results<Ok<MeDto>, ProblemHttpResult>> Login(
        LoginRequest req, HttpContext http, LoginThrottle throttle,
        UserManager<AppUser> users, SignInManager<AppUser> signIn, ILoggerFactory loggers)
    {
        var logger = loggers.CreateLogger("App.Api.Auth");
        var clientIp = ClientIp.Key(http);
        if (!throttle.TryBegin(clientIp, req.Email!)) // reserves this attempt; released below on success
        {
            logger.LogWarning("Login throttled");
            http.Response.Headers.RetryAfter = "60";
            return TooManyAttempts();
        }
        var user = await users.FindByEmailAsync(req.Email!);
        if (user is null || !user.IsActive)
        {
            users.PasswordHasher.VerifyHashedPassword(new AppUser(), DummyHash, req.Password!);
            logger.LogWarning("Login failed: unknown or inactive account");
            return InvalidCredentials();
        }

        var me = await ToMeAsync(user, users); // resolve the role before signing in (no half-signed-in 500)
        var result = await signIn.PasswordSignInAsync(user, req.Password!, isPersistent: false, lockoutOnFailure: false);
        if (!result.Succeeded)
        {
            logger.LogWarning("Login failed for user {UserId}", user.Id);
            return InvalidCredentials();
        }

        throttle.Release(clientIp, req.Email!);
        return TypedResults.Ok(me);
    }

    private static async Task<NoContent> Logout(SignInManager<AppUser> signIn)
    {
        // Removes this session from the server-side store, so replayed copies of the cookie get 401 (REQ-P11, ADR-017).
        await signIn.SignOutAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<MeDto>, UnauthorizedHttpResult>> Me(ClaimsPrincipal principal, UserManager<AppUser> users)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null) return TypedResults.Unauthorized();
        return TypedResults.Ok(await ToMeAsync(user, users));
    }

    private static async Task<MeDto> ToMeAsync(AppUser user, UserManager<AppUser> users)
    {
        var roles = await users.GetRolesAsync(user);
        var role = roles.Contains(Roles.Admin) ? Roles.Admin
            : roles.Contains(Roles.Reviewer) ? Roles.Reviewer
            : throw new InvalidOperationException($"User {user.Id} has no role.");
        return new MeDto(user.Id, user.Email!, Roles.ToWire(role));
    }
}
