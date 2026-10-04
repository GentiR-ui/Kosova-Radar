using App.Application.Abstractions;
using App.Application.Auth;
using App.Application.Candidates;
using App.Application.Common;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace App.Api.Endpoints;

public sealed record ApprovedDto(App.Domain.Reference.ItemKind Kind, Guid ItemId);

public static class CandidateEndpoints
{
    public static IEndpointRouteBuilder MapCandidateEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/admin/candidates").RequireAuthorization(Policies.Reviewer).WithTags("Candidates");
        g.MapGet("/", List);
        // A malformed id is a 404 (design.md §5), so the id is bound as a string.
        g.MapGet("/{id}", Get);
        g.MapPut("/{id}", Edit).WithMetadata(new RequestSizeLimitAttribute(256 * 1024));
        g.MapPost("/{id}/approve", Approve);
        g.MapPost("/{id}/reject", Reject);
        g.MapPost("/{id}/merge", Merge);
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

    private static async Task<Results<Ok<CandidateDto>, NotFound, Conflict, ValidationProblem>> Edit(
        string id, CandidateFieldsRequest? body, CandidateDecisions decisions, CancellationToken ct) =>
        ToResult(Guid.TryParse(id, out var guid) ? await decisions.EditAsync(guid, body, ct) : new(DecisionStatus.NotFound));

    private static async Task<Results<Ok<CandidateDto>, NotFound, Conflict, ValidationProblem>> Reject(
        string id, RejectRequest? body, CandidateDecisions decisions, CancellationToken ct) =>
        ToResult(Guid.TryParse(id, out var guid) ? await decisions.RejectAsync(guid, body, ct) : new(DecisionStatus.NotFound));

    private static async Task<Results<Ok<CandidateDto>, NotFound, Conflict, ValidationProblem>> Merge(
        string id, MergeRequest? body, CandidateDecisions decisions, CancellationToken ct) =>
        ToResult(Guid.TryParse(id, out var guid) ? await decisions.MergeAsync(guid, body, ct) : new(DecisionStatus.NotFound));

    private static async Task<Results<Ok<ApprovedDto>, NotFound, Conflict, ValidationProblem>> Approve(
        string id, CandidateApproval approval, CancellationToken ct)
    {
        var r = Guid.TryParse(id, out var guid) ? await approval.ApproveAsync(guid, ct) : new(ApprovalStatus.NotFound);
        return r.Status switch
        {
            ApprovalStatus.NotFound => TypedResults.NotFound(),
            ApprovalStatus.Conflict => TypedResults.Conflict(),
            ApprovalStatus.Invalid => TypedResults.ValidationProblem(r.Errors!.ToDictionary(e => e.Key, e => e.Value)),
            _ => TypedResults.Ok(new ApprovedDto(r.Kind!.Value, r.ItemId!.Value)),
        };
    }

    private static Results<Ok<CandidateDto>, NotFound, Conflict, ValidationProblem> ToResult(DecisionResult r) => r.Status switch
    {
        DecisionStatus.NotFound => TypedResults.NotFound(),
        DecisionStatus.Conflict => TypedResults.Conflict(),
        DecisionStatus.Invalid => TypedResults.ValidationProblem(r.Errors!.ToDictionary(e => e.Key, e => e.Value)),
        _ => TypedResults.Ok(r.Candidate!),
    };
}
