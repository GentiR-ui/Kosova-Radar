using App.Application.Auth;
using App.Application.Ingestion;
using App.Application.Sources;
using App.Domain.Content;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace App.Api.Endpoints;

public static class SourceEndpoints
{
    public static IEndpointRouteBuilder MapSourceEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/admin/sources").RequireAuthorization(Policies.Admin).WithTags("Sources");
        g.MapGet("/", List);
        g.MapGet("/{id:guid}", Get);
        // A fixture payload is capped at 1 M characters; allow for JSON escaping and multi-byte text, but not 30 MB.
        g.MapPost("/", Create).WithMetadata(new RequestSizeLimitAttribute(8 * 1024 * 1024));
        g.MapPut("/{id:guid}", Update).WithMetadata(new RequestSizeLimitAttribute(8 * 1024 * 1024));
        g.MapPost("/{id:guid}/fetch", Fetch);
        return app;
    }

    private static async Task<Ok<List<SourceDto>>> List(SourceAdmin admin, CancellationToken ct) =>
        TypedResults.Ok(await admin.ListAsync(ct));

    private static async Task<Results<Ok<SourceDetailDto>, NotFound>> Get(Guid id, SourceAdmin admin, CancellationToken ct) =>
        await admin.GetAsync(id, ct) is { } source ? TypedResults.Ok(source) : TypedResults.NotFound();

    private static async Task<Results<Created<SourceDto>, ValidationProblem>> Create(
        SourceRequest req, SourceAdmin admin, CancellationToken ct)
    {
        var result = await admin.CreateAsync(req, ct);
        return result.Status == SourceSaveStatus.Saved
            ? TypedResults.Created($"/api/admin/sources/{result.Source!.Id}", result.Source)
            : TypedResults.ValidationProblem(result.Errors!.ToDictionary(e => e.Key, e => e.Value));
    }

    private static async Task<Results<Ok<SourceDto>, ValidationProblem, NotFound>> Update(
        Guid id, SourceRequest req, SourceAdmin admin, CancellationToken ct)
    {
        var result = await admin.UpdateAsync(id, req, ct);
        return result.Status switch
        {
            SourceSaveStatus.NotFound => TypedResults.NotFound(),
            SourceSaveStatus.Invalid => TypedResults.ValidationProblem(result.Errors!.ToDictionary(e => e.Key, e => e.Value)),
            _ => TypedResults.Ok(result.Source!),
        };
    }

    // A failed run is still a 200 with status "failed"; 409 only when another run of the source does not finish in time.
    private static async Task<Results<Ok<RunDto>, NotFound, ProblemHttpResult>> Fetch(
        Guid id, IngestionService ingestion, CancellationToken ct)
    {
        var result = await ingestion.RunAsync(id, JobTrigger.Manual, IngestionService.ManualLockWait, ct);
        return result.Status switch
        {
            RunStatus.NotFound => TypedResults.NotFound(),
            RunStatus.Busy => TypedResults.Problem(
                detail: "A fetch of this source is already running.", statusCode: StatusCodes.Status409Conflict),
            _ => TypedResults.Ok(result.Run!),
        };
    }
}
