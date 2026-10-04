using App.Application.Common;
using App.Application.Public;
using Microsoft.AspNetCore.Http.HttpResults;

namespace App.Api.Endpoints;

public static class PublicItemEndpoints
{
    public static IEndpointRouteBuilder MapPublicItemEndpoints(this IEndpointRouteBuilder app)
    {
        // Published content is anonymous by design (REQ-085). A malformed id is a 404, so the id is bound as a string.
        app.MapGet("/api/events", ListEvents).AllowAnonymous().WithTags("Public");
        app.MapGet("/api/events/{id}", GetEvent).AllowAnonymous().WithTags("Public");
        app.MapGet("/api/deals", ListDeals).AllowAnonymous().WithTags("Public");
        app.MapGet("/api/search", Search).AllowAnonymous().WithTags("Public");
        app.MapGet("/api/deals/{id}", GetDeal).AllowAnonymous().WithTags("Public");
        return app;
    }

    private static async Task<Results<Ok<PagedResult<EventSummaryDto>>, ValidationProblem>> ListEvents(
        [AsParameters] EventListRequest request, PublicEventQueries queries, CancellationToken ct)
    {
        var (result, errors) = await queries.ListAsync(request, ct);
        return errors is not null ? TypedResults.ValidationProblem(errors) : TypedResults.Ok(result!);
    }

    private static async Task<Results<Ok<PagedResult<DealSummaryDto>>, ValidationProblem>> ListDeals(
        [AsParameters] DealListRequest request, PublicDealQueries queries, CancellationToken ct)
    {
        var (result, errors) = await queries.ListAsync(request, ct);
        return errors is not null ? TypedResults.ValidationProblem(errors) : TypedResults.Ok(result!);
    }

    private static async Task<Results<Ok<PagedResult<SearchResultDto>>, ValidationProblem>> Search(
        [AsParameters] SearchRequest request, PublicSearchQueries queries, CancellationToken ct)
    {
        var (result, errors) = await queries.SearchAsync(request, ct);
        return errors is not null ? TypedResults.ValidationProblem(errors) : TypedResults.Ok(result!);
    }

    private static async Task<Results<Ok<EventDetailDto>, NotFound>> GetEvent(string id, PublicItemQueries queries, CancellationToken ct) =>
        Guid.TryParse(id, out var guid) && await queries.GetEventAsync(guid, ct) is { } e ? TypedResults.Ok(e) : TypedResults.NotFound();

    private static async Task<Results<Ok<DealDetailDto>, NotFound>> GetDeal(string id, PublicItemQueries queries, CancellationToken ct) =>
        Guid.TryParse(id, out var guid) && await queries.GetDealAsync(guid, ct) is { } d ? TypedResults.Ok(d) : TypedResults.NotFound();
}
