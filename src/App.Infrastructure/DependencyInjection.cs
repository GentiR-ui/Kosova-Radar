using App.Application.Dashboard;
using App.Application.Items;
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
        if (ingestion.HttpTimeoutSeconds < 1 || ingestion.HttpRetries < 0 || ingestion.PerHostDelaySeconds < 0 || ingestion.HtmlHorizonDays < 1)
            throw new InvalidOperationException(
                "Ingestion__HttpTimeoutSeconds and Ingestion__HtmlHorizonDays must be at least 1, Ingestion__HttpRetries and Ingestion__PerHostDelaySeconds at least 0.");
        services.AddSingleton(ingestion);
        services.AddAi(config);
        services.AddScoped<SourceAdmin>();
        services.AddScoped<DuplicateDetector>();
        services.AddScoped<CandidateDecisions>();
        services.AddScoped<CandidateApproval>();
        services.AddScoped<PublishedItems>();
        services.AddScoped<DashboardQueries>();
        services.AddScoped<PublicItemQueries>();
        services.AddScoped<SitemapQueries>();
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
        services.AddSingleton<IHtmlSiteParser, PrishtinaEventsParser>();
        services.AddSingleton<IHtmlSiteParser, KapodalimParser>();
        services.AddScoped<ISourceAdapter, HtmlSourceAdapter>();
        services.AddScoped<IngestionService>();
        services.AddScoped<SyntheticFixtureSeeder>();
        services.AddScoped<KnownSourcesSeeder>();
        services.AddSingleton<ScheduledRuns>();
        services.AddHostedService<SourceScheduler>();
        return services;
    }

    /// <summary>The AI provider chosen by <c>Ai__Provider</c> (ADR-007, ADR-034); no provider and no key just means no AI; an explicit <c>Gemini</c> or <c>Anthropic</c> without a key stops startup.</summary>
    private static void AddAi(this IServiceCollection services, IConfiguration config)
    {
        var ai = config.GetSection(AiOptions.Section).Get<AiOptions>() ?? new AiOptions();
        if (ai.DailyCallLimit < 0 || ai.TimeoutSeconds < 1 || ai.MinIntervalMs < 0)
            throw new InvalidOperationException("Ai__DailyCallLimit and Ai__MinIntervalMs must be at least 0 and Ai__TimeoutSeconds at least 1.");
        ai.ApiKey = ai.ApiKey?.Trim(); // a pasted secret often ends in a newline, which is not a valid header value
        var name = ai.EffectiveProvider.ToLowerInvariant();
        if (name is not ("none" or "fake" or "gemini" or "anthropic"))
            throw new InvalidOperationException("Ai__Provider must be None, Fake, Gemini or Anthropic.");
        var remote = name is "gemini" or "anthropic";
        if (remote && string.IsNullOrWhiteSpace(ai.ApiKey))
            throw new InvalidOperationException($"Ai__Provider={ai.EffectiveProvider} needs Ai__ApiKey.");
        if (remote && ai.ApiKey!.Any(c => c is < '!' or > '~'))
            throw new InvalidOperationException("Ai__ApiKey may only contain printable ASCII characters without spaces.");
        ai.Model = ai.Model.Trim();
        if (ai.Model.Length == 0)
            ai.Model = name switch { "gemini" => GeminiAiProvider.DefaultModel, "anthropic" => "claude-haiku-4-5-20251001", _ => "" };
        if (remote && string.IsNullOrWhiteSpace(ai.Model))
            throw new InvalidOperationException("Ai__Model must not be empty.");
        if (name == "gemini" && !GeminiAiProvider.IsValidModel(ai.Model))
            throw new InvalidOperationException("Ai__Model may only contain letters, digits, '.', '-' and '_'.");
        // A model of the other vendor is a leftover setting (an old .env): every call would fail, so stop at start-up.
        if ((name == "gemini" && ai.Model.StartsWith("claude", StringComparison.OrdinalIgnoreCase))
            || (name == "anthropic" && ai.Model.StartsWith("gemini", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Ai__Model '{ai.Model}' does not belong to the {ai.EffectiveProvider} provider.");
        // The wait for the gate counts against the call timeout (AiEnricher), so a longer interval would time out every second call.
        if (remote && ai.MinIntervalMs >= ai.TimeoutSeconds * 1000)
            throw new InvalidOperationException("Ai__MinIntervalMs must be smaller than Ai__TimeoutSeconds * 1000.");
        services.AddSingleton(ai);
        services.AddSingleton<IAiProvider>(sp =>
        {
            IAiProvider provider = name switch
            {
                "fake" => new FakeAiProvider(),
                "gemini" => new GeminiAiProvider(sp.GetRequiredService<IHttpClientFactory>(), ai),
                "anthropic" => new AnthropicAiProvider(sp.GetRequiredService<IHttpClientFactory>(), ai),
                _ => new NoAiProvider(),
            };
            return remote && ai.MinIntervalMs > 0 ? new ThrottledAiProvider(provider, TimeSpan.FromMilliseconds(ai.MinIntervalMs), TimeProvider.System) : provider;
        });
        // Fixed hosts, no redirects (the key header must not follow one), no proxy; one attempt only: a retry is a second paid call.
        foreach (var client in new[] { AnthropicAiProvider.ClientName, GeminiAiProvider.ClientName })
            services.AddHttpClient(client)
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false, UseCookies = false });
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
