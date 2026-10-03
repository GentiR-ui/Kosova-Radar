using System.Globalization;
using App.Infrastructure.Time;

namespace App.Api.Testing;

/// <summary>
/// Deterministic test hooks (ADR-014), active only when TestHooks:Enabled is true (Development/Testing):
/// the X-Test-Now request header overrides IClock for that request, and /api/_test/throw raises an unhandled error.
/// </summary>
public static class TestHooks
{
    public const string HeaderName = "X-Test-Now";

    public static bool IsEnabled(IConfiguration config) => config.GetValue<bool>("TestHooks:Enabled");

    private static readonly string[] IsoFormats = ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"];

    /// <summary>Fails startup if the hooks are switched on outside Development/Testing.</summary>
    public static void GuardEnvironment(IConfiguration config, IHostEnvironment env)
    {
        if (IsEnabled(config) && !env.IsDevelopment() && !env.IsEnvironment("Testing"))
            throw new InvalidOperationException(
                $"TestHooks:Enabled must only be true in Development or Testing (environment: {env.EnvironmentName}).");
    }

    public static IApplicationBuilder UseTestClock(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        if (!context.Request.Headers.TryGetValue(HeaderName, out var raw))
        {
            await next(context);
            return;
        }

        if (!DateTimeOffset.TryParseExact(raw.ToString(), IsoFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var now))
        {
            await Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [HeaderName] = ["Must be an ISO-8601 UTC date-time."],
            }).ExecuteAsync(context);
            return;
        }

        AppClock.SetOverride(now);
        try { await next(context); }
        finally { AppClock.SetOverride(null); }
    });

    public static IEndpointRouteBuilder MapTestHookEndpoints(this IEndpointRouteBuilder app)
    {
        // Anonymous on purpose: only mapped when the hooks are enabled (never in Production).
        app.MapGet("/api/_test/throw", IResult () => throw new InvalidOperationException("TEST-SECRET-EXCEPTION"))
            .AllowAnonymous()
            .ExcludeFromDescription();
        return app;
    }
}
