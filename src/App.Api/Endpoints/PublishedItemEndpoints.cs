using App.Application.Auth;
using App.Application.Candidates;
using App.Application.Common;
using App.Application.Items;
using App.Application.Public;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace App.Api.Endpoints;

public static class PublishedItemEndpoints
{
    public static IEndpointRouteBuilder MapPublishedItemEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/admin").RequireAuthorization(Policies.Reviewer).WithTags("Published items");
        g.MapGet("/items", List);
        // A malformed id is a 404 (design.md §5), so ids are bound as strings.
        g.MapGet("/events/{id}", async Task<Results<Ok<EventDetailDto>, NotFound>> (string id, PublicItemQueries q, CancellationToken ct) =>
            Guid.TryParse(id, out var guid) && await q.GetEventAsync(guid, ct, admin: true) is { } e ? TypedResults.Ok(e) : TypedResults.NotFound());
        g.MapGet("/deals/{id}", async Task<Results<Ok<DealDetailDto>, NotFound>> (string id, PublicItemQueries q, CancellationToken ct) =>
            Guid.TryParse(id, out var guid) && await q.GetDealAsync(guid, ct, admin: true) is { } d ? TypedResults.Ok(d) : TypedResults.NotFound());

        g.MapPut("/events/{id}", async Task<IResult> (string id, CandidateFieldsRequest? body, PublishedItems items, PublicItemQueries q, CancellationToken ct) =>
        {
            var r = Guid.TryParse(id, out var guid) ? await items.EditEventAsync(guid, body, ct) : new(ItemActionStatus.NotFound);
            return r.Status == ItemActionStatus.Done ? TypedResults.Ok(await q.GetEventAsync(guid, ct, admin: true)) : Failure(r);
        }).WithMetadata(new RequestSizeLimitAttribute(256 * 1024));
        g.MapPut("/deals/{id}", async Task<IResult> (string id, CandidateFieldsRequest? body, PublishedItems items, PublicItemQueries q, CancellationToken ct) =>
        {
            var r = Guid.TryParse(id, out var guid) ? await items.EditDealAsync(guid, body, ct) : new(ItemActionStatus.NotFound);
            return r.Status == ItemActionStatus.Done ? TypedResults.Ok(await q.GetDealAsync(guid, ct, admin: true)) : Failure(r);
        }).WithMetadata(new RequestSizeLimitAttribute(256 * 1024));

        g.MapPost("/events/{id}/expire", async Task<IResult> (string id, PublishedItems items, CancellationToken ct) =>
            NoContent(Guid.TryParse(id, out var guid) ? await items.ExpireEventAsync(guid, ct) : new(ItemActionStatus.NotFound)));
        g.MapPost("/deals/{id}/expire", async Task<IResult> (string id, PublishedItems items, CancellationToken ct) =>
            NoContent(Guid.TryParse(id, out var guid) ? await items.ExpireDealAsync(guid, ct) : new(ItemActionStatus.NotFound)));
        g.MapPost("/events/{id}/withdraw", async Task<IResult> (string id, PublishedItems items, CancellationToken ct) =>
            NoContent(Guid.TryParse(id, out var guid) ? await items.WithdrawEventAsync(guid, ct) : new(ItemActionStatus.NotFound)));
        g.MapPost("/deals/{id}/withdraw", async Task<IResult> (string id, PublishedItems items, CancellationToken ct) =>
            NoContent(Guid.TryParse(id, out var guid) ? await items.WithdrawDealAsync(guid, ct) : new(ItemActionStatus.NotFound)));
        return app;
    }

    private static async Task<Results<Ok<PagedResult<AdminItemDto>>, ValidationProblem>> List(
        string? kind, string? status, string? search, int? page, int? pageSize, PublishedItems items, CancellationToken ct)
    {
        var (parsedStatus, parsedKind, filterErrors) = PublishedItems.ParseFilter(status, kind);
        var (p, size, pageErrors) = Paging.Parse(page, pageSize);
        if (filterErrors is not null || pageErrors is not null)
            return TypedResults.ValidationProblem((filterErrors ?? []).Concat(pageErrors ?? []).ToDictionary(e => e.Key, e => e.Value));
        return TypedResults.Ok(await items.ListAsync(parsedKind, parsedStatus, search, p, size, ct));
    }

    private static IResult NoContent(ItemActionResult r) => r.Status == ItemActionStatus.Done ? TypedResults.NoContent() : Failure(r);

    private static IResult Failure(ItemActionResult r) => r.Status switch
    {
        ItemActionStatus.NotFound => TypedResults.NotFound(),
        ItemActionStatus.Conflict => TypedResults.Conflict(),
        _ => TypedResults.ValidationProblem(r.Errors!.ToDictionary(e => e.Key, e => e.Value)),
    };
}
