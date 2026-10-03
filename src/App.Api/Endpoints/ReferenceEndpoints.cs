using App.Application.Abstractions;
using App.Application.Reference;
using App.Domain.Reference;
using Microsoft.AspNetCore.Http.HttpResults;

namespace App.Api.Endpoints;

public static class ReferenceEndpoints
{
    public static IEndpointRouteBuilder MapReferenceEndpoints(this IEndpointRouteBuilder app)
    {
        // Public reference data: anonymous by design (REQ-088).
        app.MapGet("/api/cities", Cities).AllowAnonymous().WithTags("Reference");
        app.MapGet("/api/categories", Categories).AllowAnonymous().WithTags("Reference");
        return app;
    }

    private static async Task<Ok<List<CityDto>>> Cities(IAppDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await ReferenceQueries.CitiesAsync(db, ct));

    private static async Task<Results<Ok<List<CategoryDto>>, ValidationProblem>> Categories(
        string? kind, IAppDbContext db, CancellationToken ct)
    {
        if (!TryParseKind(kind, out var parsed))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["kind"] = ["Must be 'event' or 'deal'."] });
        return TypedResults.Ok(await ReferenceQueries.CategoriesAsync(db, parsed, ct));
    }

    /// <summary>Optional wire kind ("event" | "deal"); empty means "no filter".</summary>
    internal static bool TryParseKind(string? value, out ItemKind? kind)
    {
        kind = value switch { "event" => ItemKind.Event, "deal" => ItemKind.Deal, _ => null };
        return kind is not null || string.IsNullOrEmpty(value);
    }
}
