using App.Application.Abstractions;
using App.Application.Auth;
using App.Application.Candidates;
using App.Application.Common;
using Microsoft.AspNetCore.Http.HttpResults;

namespace App.Api.Endpoints;

public static class CandidateEndpoints
{
    public static IEndpointRouteBuilder MapCandidateEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/admin/candidates").RequireAuthorization(Policies.Reviewer).WithTags("Candidates");
        g.MapGet("/", List);
        // A malformed id is a 404 (design.md §5), so the id is bound as a string.
        g.MapGet("/{id}", Get);
        return app;
    }

    private static async Task<Results<Ok<PagedResult<CandidateSummaryDto>>, ValidationProblem>> List(
        string? status, string? validation, string? kind, string? sourceId, int? page, int? pageSize,
        IAppDbContext db, CancellationToken ct)
    {
        var (filter, filterErrors) = CandidateQueries.ParseFilter(status, validation, kind, sourceId);
        var (p, size, pageErrors) = Paging.Parse(page, pageSize);
        if (filter is null || pageErrors is not null)
            return TypedResults.ValidationProblem(
                (filterErrors ?? []).Concat(pageErrors ?? []).ToDictionary(e => e.Key, e => e.Value));
        return TypedResults.Ok(await CandidateQueries.ListAsync(db, filter, p, size, ct));
    }

    private static async Task<Results<Ok<CandidateDto>, NotFound>> Get(string id, IAppDbContext db, CancellationToken ct) =>
        Guid.TryParse(id, out var guid) && await CandidateQueries.GetAsync(db, guid, ct) is { } c
            ? TypedResults.Ok(c)
            : TypedResults.NotFound();
}
