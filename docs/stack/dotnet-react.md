# Default stack conventions — ASP.NET Core 10 + EF Core/PostgreSQL + React/TypeScript

Read this before writing code. These are the house patterns; the reviewer checks against them.
Snippets are patterns to follow, not files to paste blindly — keep them compiling and tested.

## Layout (fixed names — Dockerfile, CI and verify.sh depend on them)

```
App.slnx
src/App.Domain           entities, value objects, ITenantOwned, domain rules (no framework refs)
src/App.Application      use cases, DTOs, validation, interfaces (IAppDbContext, ITenantContext, IClock)
src/App.Infrastructure   AppDbContext, Migrations/, Identity, external service adapters
src/App.Api              Program.cs, Endpoints/<Feature>Endpoints.cs, auth, middleware, SPA hosting
tests/App.UnitTests      fast tests: domain rules, use cases, architecture rules        (yours)
tests/App.IntegrationTests  real API + PostgreSQL via its own AppFactory                 (yours)
tests/App.AcceptanceTests   black-box HTTP tests per REQ + their AppFactory, by test-author (LOCKED)
web/src/features/<feature>/  pages, components, api hooks, *.test.tsx                   (yours)
web/e2e/acceptance       Playwright tests per REQ + playwright.config.ts + servers.ts   (LOCKED)
web/e2e/flows            extra Playwright flows / regression                             (yours)
```

## Program.cs order (platform baseline, task T001)

```csharp
builder.Services.AddProblemDetails();
builder.Services.AddValidation();                 // .NET 10 minimal-API validation (DataAnnotations on request records)
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();
builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
builder.Services.AddAuthorization();
builder.Services.AddIdentityCore<AppUser>(o => { o.Password.RequiredLength = 12; o.User.RequireUniqueEmail = true; })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddClaimsPrincipalFactory<AppClaimsFactory>();    // adds "tenant_id" and role claims
builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;  // tests use https://localhost (AppFactory)
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddSingleton(LoginRateLimiter.Create());   // PartitionedRateLimiter<string>, key = $"{ip}|{email}"
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();

var app = builder.Build();
app.UseExceptionHandler();          // problem details, no exception text outside Development
app.UseStatusCodePages();
app.UseSecurityHeaders();           // small middleware: nosniff, DENY, referrer policy (+ HSTS in Production)
app.UseDefaultFiles(); app.UseStaticFiles();
app.UseAuthentication(); app.UseAuthorization();
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing")) await app.MigrateDatabaseAsync();
app.MapHealthChecks("/health");
app.MapAuthEndpoints();             // /api/auth/*, /api/me
app.MapInvoiceEndpoints();          // one extension method per feature
app.MapFallback("/api/{**rest}", () => Results.NotFound());   // unknown API routes stay 404
app.MapFallbackToFile("index.html");                          // SPA routes
app.Run();
```

Behind a reverse proxy in production add `UseForwardedHeaders` (X-Forwarded-For/Proto) before everything else.

## Multi-tenancy (the RLS lesson: AI code forgets tenant isolation — we make it structural)

```csharp
public interface ITenantOwned { Guid TenantId { get; set; } }            // Domain

public sealed class AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenant)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options), IAppDbContext
{
    private Guid CurrentTenantId => tenant.TenantId;          // EF parameterizes context members per query

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        var apply = typeof(AppDbContext).GetMethod(nameof(ApplyTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;
        foreach (var type in b.Model.GetEntityTypes().Select(t => t.ClrType).Where(typeof(ITenantOwned).IsAssignableFrom))
            apply.MakeGenericMethod(type).Invoke(this, [b]);
    }

    private void ApplyTenantFilter<T>(ModelBuilder b) where T : class, ITenantOwned =>
        b.Entity<T>().HasQueryFilter(e => e.TenantId == CurrentTenantId).HasIndex(e => e.TenantId);

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        foreach (var e in ChangeTracker.Entries<ITenantOwned>())
        {
            if (e.State == EntityState.Added) e.Entity.TenantId = tenant.TenantId;
            else if (e.State == EntityState.Modified && e.Property(x => x.TenantId).IsModified)
                throw new InvalidOperationException("TenantId cannot change.");
        }
        return base.SaveChangesAsync(ct);
    }
}
```

- `AppUser` carries `TenantId` but is NOT `ITenantOwned` (login must find users before a tenant is known).
- Never call `IgnoreQueryFilters()` outside Infrastructure admin code; the reviewer flags it.
- Integration test (yours): every `ITenantOwned` entity in the EF model has a query filter.
- Acceptance (test-author): one cross-tenant test per tenant-scoped endpoint (REQ-P04).

## Endpoints

```csharp
public static class InvoiceEndpoints
{
    public static IEndpointRouteBuilder MapInvoiceEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/invoices").RequireAuthorization().WithTags("Invoices");
        g.MapGet("/", ListInvoices.Handle);
        g.MapGet("/{id:guid}", GetInvoice.Handle);
        g.MapPost("/", CreateInvoice.Handle);
        return app;
    }
}

public sealed record CreateInvoiceRequest([Required, MaxLength(200)] string Customer, [MinLength(1)] List<LineItemDto> LineItems, DateOnly DueDate);
```

- Thin endpoints: bind → call a use case in Application → map to `TypedResults`.
- Return `Results<Ok<T>, NotFound, ValidationProblem>`; never leak entities, always DTOs.
- Business-rule failures → `TypedResults.ValidationProblem(errors)` (400) or `Conflict` (409).
- Every endpoint has an explicit auth decision: `RequireAuthorization(...)` or `AllowAnonymous()` with a comment why.
- Pagination for lists: `?page=1&pageSize=50` (max 100). Never return unbounded lists.
- Log with message templates, never interpolate user input; never log secrets, tokens, passwords, full bodies.

## Data & migrations

- Fluent configuration in `Infrastructure/Persistence/Configurations/*Configuration.cs`; max lengths on all strings.
- `dotnet ef migrations add <Name> -p src/App.Infrastructure -s src/App.Api`; one migration per task that changes the model.
- Money: `decimal(18,2)` + currency code. Time: `DateTimeOffset` UTC via `IClock` (testable).

## Tests

```csharp
// tests/App.AcceptanceTests  (test-author only; black box over HTTP)
[Collection(AcceptanceCollection.Name)]
public class AuthTests(AppFactory app)
{
    [Fact, Trait("Req", "REQ-P02")]
    public async Task Unknown_email_and_wrong_password_get_the_same_401()
    {
        var client = app.CreateClient();
        var email = $"u{Guid.NewGuid():N}@test.local";            // unique data: tests share one database
        var res = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Wrong-password-123" });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
```

```ts
// web/e2e/acceptance  (test-author only; uses accessible names from design.md §6)
test('REQ-P10 new visitor registers and sees the home screen', async ({ page }) => {
  const email = `u${Date.now()}${Math.random().toString(36).slice(2, 8)}@test.local`
  await page.goto('/register')
  await page.getByLabel('Email').fill(email)
  await page.getByLabel('Password').fill('Correct-horse-battery-1')
  await page.getByLabel('Organization name').fill('Acme Ltd')
  await page.getByRole('button', { name: 'Create account' }).click()
  await expect(page.getByRole('heading', { name: 'Acme Ltd' })).toBeVisible()
})
```

- Unit tests: domain rules and use cases, no database. Integration tests: real PostgreSQL via `AppFactory`.
- Acceptance test attributes go on ONE line — `[Fact, Trait("Req", "REQ-001")]` — because `verify.sh` counts
  declarations per line and fails if fewer tests pass than are declared (skipped/excluded tests are caught).
- Run acceptance UI tests with `npm run e2e:acceptance` (locked config); `npm run e2e` runs your own flows.
- Acceptance tests are the definition of done. Unit/integration/regression tests are yours to add and change.
- When fixing a bug, first add a failing regression test (unit, integration or `web/e2e/flows`).

## Frontend

- `web/src/features/<feature>/` holds `XxxPage.tsx`, components, `api.ts` (TanStack Query hooks), tests.
- `web/src/lib/api.ts`: one `fetch` wrapper — `credentials: 'same-origin'`, JSON, throws a typed `ProblemDetails` error.
- Server state only through TanStack Query; local UI state with React state. No Redux.
- Every input has a `<label>`; buttons have text; show field errors from `problem.errors`; loading, empty and error states for every list.
- Mobile first: layouts must work at 360 px (Playwright `mobile` project runs the acceptance suite).
- `web/e2e/acceptance` is excluded from Prettier so locked files never change on format.

## Formatting & commands

- C#: `dotnet format App.slnx --exclude tests/App.AcceptanceTests` (never plain `dotnet format`).
- Web: Prettier runs automatically after each edit (hook); `npm run lint|typecheck|test|build|e2e` in `web/`.
- Database for local runs and E2E: `docker compose up -d --wait db`.
