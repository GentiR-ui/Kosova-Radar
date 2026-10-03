using App.Application.Abstractions;
using App.Application.Audit;
using App.Application.Auth;
using App.Application.Common;
using Microsoft.AspNetCore.Http.HttpResults;

namespace App.Api.Endpoints;

public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/admin/audit", List).RequireAuthorization(Policies.Reviewer).WithTags("Audit");
        return app;
    }

    private static async Task<Results<Ok<PagedResult<AuditEntryDto>>, ValidationProblem>> List(
        Guid? entityId, int? page, int? pageSize, IAppDbContext db, CancellationToken ct)
    {
        var (p, size, errors) = Paging.Parse(page, pageSize);
        if (errors is not null) return TypedResults.ValidationProblem(errors);
        return TypedResults.Ok(await AuditQueries.ListAsync(db, entityId, p, size, ct));
    }
}
