using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using App.Api;
using App.Api.Endpoints;
using App.Api.Http;
using App.Api.Testing;
using App.Application.Abstractions;
using App.Application.Auth;
using App.Application.Ingestion;
using App.Infrastructure;
using App.Infrastructure.Identity;
using App.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
TestHooks.GuardEnvironment(builder.Configuration, builder.Environment);
ProductionGuards.RejectDevBootstrapCredentials(builder.Configuration, builder.Environment);
ProductionGuards.RequireTrustedProxy(builder.Configuration, builder.Environment);
// Catch lifetime mistakes (e.g. a singleton consuming a scoped service) in tests too, not only in Development.
var validateServices = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing");
builder.Host.UseDefaultServiceProvider(o => { o.ValidateScopes = validateServices; o.ValidateOnBuild = validateServices; });
// The crawler User-Agent and the SEO URLs need the public address (design.md §2).
if (!validateServices && string.IsNullOrWhiteSpace(builder.Configuration["Site:BaseUrl"]))
    throw new InvalidOperationException("Site__BaseUrl is not set.");
if (!validateServices && string.Equals(builder.Configuration["Ai:Provider"]?.Trim(), "Fake", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Ai__Provider=Fake is for Development and Testing only.");
var testHooks = TestHooks.IsEnabled(builder.Configuration);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
// Enums travel as camelCase strings ("needsReview", "jsonFeed"); design.md §5. Issue codes and run error codes are
// PascalCase strings, so model them as strings (or give those enums their own converter), not as plain enums.
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false)));
builder.Services.AddValidation();
builder.Services.AddHttpContextAccessor();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddSingleton<LoginThrottle>();
builder.Services.AddPublicRateLimiting(builder.Configuration);
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();

// Behind the production reverse proxy: trust X-Forwarded-For/Proto only from the configured proxies/networks.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var ip in (builder.Configuration["Proxy:KnownProxies"] ?? "")
                 .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries))
        o.KnownProxies.Add(IPAddress.Parse(ip));
    foreach (var cidr in (builder.Configuration["Proxy:KnownNetworks"] ?? "")
                 .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries))
        o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(cidr));
});

builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    o.ExpireTimeSpan = TimeSpan.FromHours(8); // server-side session store: see AddInfrastructure (ADR-017)
    o.SlidingExpiration = true;
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
});
// Endpoints without an explicit decision require a signed-in user; anonymous ones opt out with a comment.
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy(Policies.Admin, p => p.RequireRole(Roles.Admin))
    .AddPolicy(Policies.Reviewer, p => p.RequireRole(Roles.Admin, Roles.Reviewer));

builder.Services.AddHsts(o => { o.MaxAge = TimeSpan.FromDays(365); o.IncludeSubDomains = true; });

builder.Services.AddResponseCompression();

var app = builder.Build();
var isDevOrTesting = app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing");

app.UseForwardedHeaders();
app.UseSecurityHeaders();            // first, so every response (errors, 400s from hooks, 429s) carries them
app.UseExceptionHandler();           // problem details without exception text (REQ-P09)
app.UseStatusCodePages();
if (!isDevOrTesting) app.UseHsts();
if (testHooks) app.UseTestClock();
app.UseResponseCompression();
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // Vite hashes /assets/* file names: cache for a year; index.html must always be revalidated.
        ctx.Context.Response.Headers.CacheControl = ctx.Context.Request.Path.StartsWithSegments("/assets")
            ? "public,max-age=31536000,immutable"
            : "no-cache";
    },
});                // built SPA assets in wwwroot: public, served before auth
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (isDevOrTesting)
{
    app.MapOpenApi().AllowAnonymous(); // API description, Development/Testing only
}

await using (var scope = app.Services.CreateAsyncScope())
{
    // Every environment migrates at start-up (single instance, see docs/RUNBOOK.md); a failure stops the host.
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<ReferenceDataSeeder>().SeedAsync();
    await scope.ServiceProvider.GetRequiredService<BootstrapAdmin>().EnsureAsync();
    await scope.ServiceProvider.GetRequiredService<IngestionService>().MarkInterruptedAsync(default);
    if (builder.Configuration.GetValue("Seed:KnownSources", true))
        await scope.ServiceProvider.GetRequiredService<KnownSourcesSeeder>().SeedAsync();
    if (builder.Configuration.GetValue<bool>("Seed:SyntheticFixtures"))
        await scope.ServiceProvider.GetRequiredService<SyntheticFixtureSeeder>().SeedAsync();
}

app.MapHealthChecks("/health").AllowAnonymous(); // liveness for the host and the reverse proxy
app.MapAuthEndpoints();
app.MapReferenceEndpoints();
app.MapAuditEndpoints();
app.MapDashboardEndpoints();
app.MapUserEndpoints();
app.MapSourceEndpoints();
app.MapCandidateEndpoints();
app.MapPublishedItemEndpoints();
app.MapPublicItemEndpoints();
app.MapSeoEndpoints();
if (testHooks) app.MapTestHookEndpoints();

// Unknown admin routes require a signed-in user first (401), then 404; other unknown API routes are 404.
app.MapFallback("/api/admin/{**rest}", () => Results.NotFound()).RequireAuthorization();
app.MapFallback("/api/{**rest}", () => Results.NotFound()).AllowAnonymous();
app.MapFallbackToFile("index.html").AllowAnonymous(); // SPA routes are public; the SPA guards /admin itself
app.MapFallback("{*path:file}", () => Results.NotFound()).AllowAnonymous(); // missing files: 404, not 401

app.Run();

public partial class Program { }
