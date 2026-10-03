using System.Net;
using App.Api.Endpoints;
using App.Api.Http;
using App.Api.Testing;
using App.Application.Auth;
using App.Infrastructure;
using App.Infrastructure.Identity;
using App.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
TestHooks.GuardEnvironment(builder.Configuration, builder.Environment);
var testHooks = TestHooks.IsEnabled(builder.Configuration);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddValidation();
builder.Services.AddHttpContextAccessor();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
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

var app = builder.Build();
var isDevOrTesting = app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing");
if (!isDevOrTesting && string.IsNullOrWhiteSpace(builder.Configuration["Proxy:KnownProxies"])
    && string.IsNullOrWhiteSpace(builder.Configuration["Proxy:KnownNetworks"]))
    app.Logger.LogWarning("No Proxy__KnownProxies/KnownNetworks configured: behind a reverse proxy every client shares one IP for rate limits and HSTS is not sent.");

app.UseForwardedHeaders();
app.UseSecurityHeaders();            // first, so every response (errors, 400s from hooks, 429s) carries them
app.UseExceptionHandler();           // problem details without exception text (REQ-P09)
app.UseStatusCodePages();
if (!isDevOrTesting) app.UseHsts();
if (testHooks) app.UseTestClock();
app.UseDefaultFiles();
app.UseStaticFiles();                // built SPA assets in wwwroot: public, served before auth
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (isDevOrTesting)
{
    app.MapOpenApi().AllowAnonymous(); // API description, Development/Testing only
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

await using (var scope = app.Services.CreateAsyncScope())
    await scope.ServiceProvider.GetRequiredService<BootstrapAdmin>().EnsureAsync();

app.MapHealthChecks("/health").AllowAnonymous(); // liveness for the host and the reverse proxy
app.MapAuthEndpoints();
if (testHooks) app.MapTestHookEndpoints();

// Unknown admin routes require a signed-in user first (401), then 404; other unknown API routes are 404.
app.MapFallback("/api/admin/{**rest}", () => Results.NotFound()).RequireAuthorization();
app.MapFallback("/api/{**rest}", () => Results.NotFound()).AllowAnonymous();
app.MapFallbackToFile("index.html").AllowAnonymous(); // SPA routes are public; the SPA guards /admin itself
app.MapFallback("{*path:file}", () => Results.NotFound()).AllowAnonymous(); // missing files: 404, not 401

app.Run();

public partial class Program { }
