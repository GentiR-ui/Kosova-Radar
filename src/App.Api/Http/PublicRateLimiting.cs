using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;

namespace App.Api.Http;

/// <summary>Fixed window per client IP on anonymous API endpoints (not /api/auth, which has its own limiter); RateLimit:PublicPerMinute = 0 disables it.</summary>
public static class PublicRateLimiting
{
    public static IServiceCollection AddPublicRateLimiting(this IServiceCollection services, IConfiguration config)
    {
        var perMinute = config.GetValue("RateLimit:PublicPerMinute", 300);
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = (ctx, _) =>
            {
                ctx.HttpContext.Response.Headers.RetryAfter = "60";
                ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("App.Api.RateLimit")
                    .LogWarning("Public rate limit hit on {Route}", // route pattern, never the raw (decoded) path
                        (ctx.HttpContext.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unknown");
                return ValueTask.CompletedTask;
            };
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var path = context.Request.Path;
                var isAnonymousEndpoint = context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null;
                var isPublicApi = isAnonymousEndpoint && path.StartsWithSegments("/api") && !path.StartsWithSegments("/api/auth");
                if (perMinute <= 0 || !isPublicApi) return RateLimitPartition.GetNoLimiter("unlimited");

                return RateLimitPartition.GetFixedWindowLimiter(
                    ClientIp.Key(context),
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1) });
            });
        });
        return services;
    }
}
