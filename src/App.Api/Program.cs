using App.Api.Endpoints;
using App.Api.Testing;
using App.Application.Auth;
using App.Infrastructure;
using App.Infrastructure.Identity;
using App.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
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

if (testHooks) app.UseTestClock();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    app.MapOpenApi().AllowAnonymous(); // API description, Development/Testing only
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

await using (var scope = app.Services.CreateAsyncScope())
    await scope.ServiceProvider.GetRequiredService<BootstrapAdmin>().EnsureAsync();

app.MapAuthEndpoints();
if (testHooks) app.MapTestHookEndpoints();

// Unknown admin routes require a signed-in user first (401), then 404; other unknown API routes are 404.
app.MapFallback("/api/admin/{**rest}", () => Results.NotFound()).RequireAuthorization();
app.MapFallback("/api/{**rest}", () => Results.NotFound()).AllowAnonymous();

app.Run();

public partial class Program { }
