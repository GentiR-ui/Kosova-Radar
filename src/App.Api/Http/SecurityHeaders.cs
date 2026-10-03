namespace App.Api.Http;

/// <summary>Headers sent on every response, including errors and early rejections (REQ-P08, design.md §3).</summary>
public static class SecurityHeaders
{
    public const string ContentSecurityPolicy =
        "default-src 'self'; img-src 'self' https: data:; font-src 'self' data:; object-src 'none'; frame-ancestors 'none'; base-uri 'self'";

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) => app.Use((context, next) =>
    {
        context.Response.OnStarting(() =>
        {
            var h = context.Response.Headers;
            h.XContentTypeOptions = "nosniff";
            h.XFrameOptions = "DENY";
            h["Referrer-Policy"] = "strict-origin-when-cross-origin";
            h.ContentSecurityPolicy = ContentSecurityPolicy;
            return Task.CompletedTask;
        });
        return next(context);
    });
}
