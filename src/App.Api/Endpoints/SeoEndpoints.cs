using App.Api.Http;
using App.Application.Public;

namespace App.Api.Endpoints;

public static class SeoEndpoints
{
    public static IEndpointRouteBuilder MapSeoEndpoints(this IEndpointRouteBuilder app)
    {
        // Crawler-facing pages are anonymous by design (REQ-090..092).
        app.MapGet("/events/{id}", (string id, HttpContext http, PublicItemQueries q, IConfiguration cfg, IWebHostEnvironment env) =>
            Page(id, env, http.RequestAborted, async (guid, shell) =>
                await q.GetEventAsync(guid, http.RequestAborted) is { } e ? SeoPages.EventPage(shell, e, BaseUrl(cfg)) : null))
            .AllowAnonymous().ExcludeFromDescription();
        app.MapGet("/deals/{id}", (string id, HttpContext http, PublicItemQueries q, IConfiguration cfg, IWebHostEnvironment env) =>
            Page(id, env, http.RequestAborted, async (guid, shell) =>
                await q.GetDealAsync(guid, http.RequestAborted) is { } d ? SeoPages.DealPage(shell, d, BaseUrl(cfg)) : null))
            .AllowAnonymous().ExcludeFromDescription();
        app.MapGet("/sitemap.xml", async (HttpContext http, SitemapQueries q, IConfiguration cfg, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "public, max-age=300";
            var (events, deals) = await q.ActiveIdsAsync(ct);
            return Results.Content(SeoPages.Sitemap(BaseUrl(cfg), events, deals), "application/xml");
        }).AllowAnonymous().ExcludeFromDescription();
        app.MapGet("/robots.txt", (IConfiguration cfg) => Results.Content(SeoPages.Robots(BaseUrl(cfg)), "text/plain"))
            .AllowAnonymous().ExcludeFromDescription();
        return app;
    }

    private static string BaseUrl(IConfiguration cfg) => (cfg["Site:BaseUrl"] ?? "").TrimEnd('/');

    /// <summary>Unknown, malformed or non-public ids get the plain shell with status 404 (the SPA shows its own not-found).</summary>
    private static async Task<IResult> Page(string id, IWebHostEnvironment env, CancellationToken ct,
        Func<Guid, string, Task<string?>> render)
    {
        var shell = await ReadShellAsync(env, ct);
        var html = Guid.TryParse(id, out var guid) ? await render(guid, shell) : null;
        return html is null
            ? Results.Content(SeoPages.NoIndex(shell), "text/html", statusCode: StatusCodes.Status404NotFound)
            : Results.Content(html, "text/html");
    }

    private static async Task<string> ReadShellAsync(IWebHostEnvironment env, CancellationToken ct)
    {
        var file = env.WebRootFileProvider.GetFileInfo("index.html");
        if (!file.Exists) return SeoPages.BuiltInShell;
        await using var stream = file.CreateReadStream();
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(ct);
    }
}
