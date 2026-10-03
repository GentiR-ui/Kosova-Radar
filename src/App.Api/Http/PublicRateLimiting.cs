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
                    .LogWarning("Public rate limit hit on {Path}", ctx.HttpContext.Request.Path.Value);
                return ValueTask.CompletedTask;
            };
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var path = context.Request.Path;
                var isAnonymousEndpoint = context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null;
                var isPublicApi = isAnonymousEndpoint && path.StartsWithSegments("/api") && !path.StartsWithSegments("/api/auth");
                if (perMinute <= 0 || !isPublicApi) return RateLimitPartition.GetNoLimiter("unlimited");

                return RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1) });
            });
        });
        return services;
    }
}
