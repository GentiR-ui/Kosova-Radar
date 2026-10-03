using System.ComponentModel.DataAnnotations;
using App.Application.Auth;
using App.Application.Users;
using App.Infrastructure.Identity;
using Microsoft.AspNetCore.Http.HttpResults;

namespace App.Api.Endpoints;

public sealed record CreateUserRequest(
    [property: Required, EmailAddress, MaxLength(256)] string? Email,
    [property: Required, MinLength(12), MaxLength(128)] string? Password,
    [property: Required, AllowedValues("admin", "reviewer")] string? Role);

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/admin/users").RequireAuthorization(Policies.Admin).WithTags("Users");
        g.MapGet("/", List);
        g.MapPost("/", Create);
        g.MapPost("/{id:guid}/deactivate", Deactivate);
        return app;
    }

    private static async Task<Ok<List<UserDto>>> List(UserAdmin admin, CancellationToken ct) =>
        TypedResults.Ok(await admin.ListAsync(ct));

    private static async Task<Results<Created<UserDto>, ValidationProblem, ProblemHttpResult>> Create(
        CreateUserRequest req, UserAdmin admin, CancellationToken ct)
    {
        var role = req.Role == "admin" ? Roles.Admin : Roles.Reviewer;
        var result = await admin.CreateAsync(req.Email!.Trim(), req.Password!, role, ct);
        return result.Status switch
        {
            CreateUserStatus.Created => TypedResults.Created($"/api/admin/users/{result.User!.Id}", result.User),
            CreateUserStatus.EmailTaken => TypedResults.Problem(title: "Email already in use",
                detail: "A user with this email already exists.", statusCode: StatusCodes.Status409Conflict),
            _ => TypedResults.ValidationProblem(result.Errors!.ToDictionary(e => e.Key, e => e.Value)),
        };
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> Deactivate(
        Guid id, UserAdmin admin, CancellationToken ct) =>
        await admin.DeactivateAsync(id, ct) switch
        {
            DeactivateUserStatus.NotFound => TypedResults.NotFound(),
            DeactivateUserStatus.Self => TypedResults.Problem(title: "Cannot deactivate yourself",
                detail: "You cannot deactivate your own account.", statusCode: StatusCodes.Status400BadRequest),
            _ => TypedResults.NoContent(),
        };
}
