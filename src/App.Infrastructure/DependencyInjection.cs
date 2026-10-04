using App.Application.Abstractions;
using App.Application.Ai;
using App.Application.Audit;
using App.Application.Candidates;
using App.Application.Ingestion;
using App.Application.Public;
using App.Application.Sources;
using App.Infrastructure.Ai;
using App.Infrastructure.Audit;
using App.Infrastructure.Identity;
using App.Infrastructure.Ingestion;
using App.Infrastructure.Persistence;
using App.Infrastructure.Time;
using System.Net;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.Retry;

namespace App.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<IClock, AppClock>();
        var connectionString = config.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings__Default is not set.");
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));
        services.AddDataProtection().SetApplicationName("KosovaRadar").PersistKeysToDbContext<AppDbContext>();

        services.AddIdentityCore<AppUser>(o =>
            {
                o.Password.RequiredLength = 12;
                o.User.RequireUniqueEmail = true;
                // REQ-001: the only password rule is the length; complexity rules are off (ADR-022).
                o.Password.RequireDigit = o.Password.RequireLowercase = o.Password.RequireUppercase = o.Password.RequireNonAlphanumeric = false;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager<AppSignInManager>();

        // Deactivation updates the security stamp; validating it on every request ends sessions at once (REQ-003).
        services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero);

        services.AddMemoryCache();
        services.AddSingleton<ITicketStore, MemoryTicketStore>();
        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
            .Configure<ITicketStore>((o, store) => o.SessionStore = store);

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<ReferenceDataSeeder>();
        services.AddScoped<BootstrapAdmin>();
        services.AddScoped<UserAdmin>();
        var ingestion = config.GetSection(IngestionOptions.Section).Get<IngestionOptions>() ?? new IngestionOptions();
        if (ingestion.HttpTimeoutSeconds < 1 || ingestion.HttpRetries < 0 || ingestion.PerHostDelaySeconds < 0)
            throw new InvalidOperationException(
                "Ingestion__HttpTimeoutSeconds must be at least 1, Ingestion__HttpRetries and Ingestion__PerHostDelaySeconds at least 0.");
        services.AddSingleton(ingestion);
        services.AddAi(config);
        services.AddScoped<SourceAdmin>();
        services.AddScoped<CandidateDecisions>();
        services.AddScoped<CandidateApproval>();
        services.AddScoped<PublicItemQueries>();
        services.AddScoped<PublicEventQueries>();
        services.AddScoped<PublicDealQueries>();
        services.AddScoped<PublicSearchQueries>();
        services.AddScoped<ExpirationRunner>();
        services.AddHostedService<ExpirationJob>();
        services.AddSingleton<SourceRunLock>();
        services.AddSingleton(sp => new HostThrottle(TimeSpan.FromSeconds(sp.GetRequiredService<IngestionOptions>().PerHostDelaySeconds), TimeProvider.System));
        services.AddSingleton<RobotsCache>();
        services.AddTransient<HostThrottleHandler>();
        services.AddTransient<RobotsChecker>();
        services.AddScoped<ISourceAdapter, FixtureSourceAdapter>();
        services.AddSourceHttpClient(config);
        services.AddScoped<ISourceAdapter, JsonFeedSourceAdapter>();
        services.AddScoped<ISourceAdapter, RssSourceAdapter>();
        services.AddScoped<IngestionService>();
        services.AddSingleton<ScheduledRuns>();
        services.AddHostedService<SourceScheduler>();
        return services;
    }

    /// <summary>The AI provider chosen by <c>Ai__Provider</c> (ADR-007); a missing key never stops the application, it just means no AI.</summary>
    private static void AddAi(this IServiceCollection services, IConfiguration config)
    {
        var ai = config.GetSection(AiOptions.Section).Get<AiOptions>() ?? new AiOptions();
        if (ai.DailyCallLimit < 0 || ai.TimeoutSeconds < 1)
            throw new InvalidOperationException("Ai__DailyCallLimit must be at least 0 and Ai__TimeoutSeconds at least 1.");
        IAiProvider provider = ai.EffectiveProvider.ToLowerInvariant() switch
        {
            "none" => new NoAiProvider(),
            "fake" => new FakeAiProvider(),
            "anthropic" => throw new InvalidOperationException("Ai__Provider=Anthropic is not available yet (T027)."),
            _ => throw new InvalidOperationException("Ai__Provider must be None, Fake or Anthropic."),
        };
        services.AddSingleton(ai);
        services.AddSingleton(provider);
        services.AddSingleton<IAiLedger, AiLedger>();
        services.AddScoped<AiEnricher>();
    }

    /// <summary>Named client <c>sources</c> (design.md §6): SSRF guard, no proxy, 5 redirects, retries.</summary>
    private static void AddSourceHttpClient(this IServiceCollection services, IConfiguration config)
    {
        var siteUrl = config["Site:BaseUrl"]?.TrimEnd('/');
        var client = services.AddHttpClient<SourceHttpFetcher>(SourceHttpFetcher.ClientName, http =>
            {
                http.Timeout = Timeout.InfiniteTimeSpan; // SourceHttpFetcher enforces one deadline for the whole fetch
                http.DefaultRequestHeaders.UserAgent.TryParseAdd($"KosovaRadarBot/1.0 (+{siteUrl ?? "http://localhost"}/bot)");
            })
            .ConfigurePrimaryHttpMessageHandler(SourceHttpFetcher.CreateHandler);
        client
            .AddResilienceHandler("sources-retry", (pipeline, context) =>
            {
                var options = context.ServiceProvider.GetRequiredService<IngestionOptions>();
                if (options.HttpRetries < 1) return; // Polly rejects 0 attempts; no strategy means no retries
                pipeline.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = options.HttpRetries,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = TimeSpan.FromSeconds(1),
                    ShouldHandle = args => ValueTask.FromResult(IsTransient(args.Outcome)),
                });
            });
        client.AddHttpMessageHandler<HostThrottleHandler>(); // added last = innermost: every attempt, retries included, is spaced
    }

    private static bool IsTransient(Outcome<HttpResponseMessage> outcome) =>
        outcome.Exception is { } e
            ? e is HttpRequestException { HttpRequestError: not (HttpRequestError.NameResolutionError or HttpRequestError.SecureConnectionError) }
              && !SourceHttpFetcher.IsBlocked(e) // a blocked address, an unknown name or a bad certificate never gets better
            : outcome.Result is { StatusCode: >= HttpStatusCode.InternalServerError or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests };
}
