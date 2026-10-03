using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Application.Auth;
using App.Infrastructure.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using App.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace App.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public class UserAdminTests(AppFactory app)
{
    private const string BootstrapEmail = "admin@kosovaradar.local";
    private const string BootstrapPassword = "Radar-admin-dev-2026";

    private async Task<HttpClient> LoginAsync(string email, string password)
    {
        var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { email, password })).StatusCode);
        return client;
    }

    private static string NewEmail() => $"ua{Guid.NewGuid():N}@test.local";

    private async Task<List<JsonElement>> AuditAsync(HttpClient admin, Guid entityId)
    {
        var res = await admin.GetAsync($"/api/admin/audit?entityId={entityId}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var page = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        return page.GetProperty("items").EnumerateArray().Select(e => e.Clone()).ToList();
    }

    [Fact]
    public async Task Create_writes_one_audit_entry_without_the_password_and_deactivate_audits_before_and_after()
    {
        var admin = await LoginAsync(BootstrapEmail, BootstrapPassword);
        var email = NewEmail();
        const string password = "all-lowercase-password";

        var created = await admin.PostAsJsonAsync("/api/admin/users", new { email, password, role = "reviewer" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
        await LoginAsync(email, password); // length is the only password rule (ADR-022)

        var afterCreate = Assert.Single(await AuditAsync(admin, id));
        Assert.Equal("user.create", afterCreate.GetProperty("action").GetString());
        Assert.Equal("user", afterCreate.GetProperty("entityType").GetString());
        Assert.Equal(email, afterCreate.GetProperty("changes").GetProperty("after").GetProperty("email").GetString());
        Assert.DoesNotContain(password, afterCreate.GetRawText());

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/users/{id}/deactivate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/users/{id}/deactivate", null)).StatusCode); // idempotent

        var entries = await AuditAsync(admin, id);
        Assert.Equal(["user.deactivate", "user.create"], entries.Select(e => e.GetProperty("action").GetString()));
        var changes = entries[0].GetProperty("changes");
        Assert.True(changes.GetProperty("before").GetProperty("isActive").GetBoolean());
        Assert.False(changes.GetProperty("after").GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Duplicate_email_and_validation_failures_leave_no_second_user()
    {
        var admin = await LoginAsync(BootstrapEmail, BootstrapPassword);
        var email = NewEmail();
        await admin.PostAsJsonAsync("/api/admin/users", new { email, password = "Strong-pass-2026!", role = "admin" });

        var duplicate = await admin.PostAsJsonAsync("/api/admin/users", new { email = email.ToUpperInvariant(), password = "Another-pass-2026!", role = "reviewer" });
        var tooShort = await admin.PostAsJsonAsync("/api/admin/users", new { email = NewEmail(), password = "short", role = "reviewer" });

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);
        var badRole = await admin.PostAsJsonAsync("/api/admin/users", new { email = NewEmail(), password = "Strong-pass-2026!", role = "owner" });
        Assert.Equal(HttpStatusCode.BadRequest, badRole.StatusCode);
        var audit = await admin.GetStringAsync("/api/admin/audit?pageSize=100");
        Assert.DoesNotContain("short", audit);
        var users = JsonDocument.Parse(await admin.GetStringAsync("/api/admin/users")).RootElement.EnumerateArray().ToList();
        Assert.Single(users, u => string.Equals(u.GetProperty("email").GetString(), email, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, users.Count(u => u.GetProperty("role").GetString() == "admin" && string.Equals(u.GetProperty("email").GetString(), email, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task Concurrent_creates_of_the_same_email_give_one_user_and_the_rest_409()
    {
        var admin = await LoginAsync(BootstrapEmail, BootstrapPassword);
        var email = NewEmail();

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            admin.PostAsJsonAsync("/api/admin/users", new { email, password = "Strong-pass-2026!", role = "reviewer" })));

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.All(results.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
    }

    [Fact]
    public async Task Reviewer_cannot_use_the_user_endpoints_and_anonymous_gets_401()
    {
        var admin = await LoginAsync(BootstrapEmail, BootstrapPassword);
        var email = NewEmail();
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/admin/users", new { email, password = "Strong-pass-2026!", role = "reviewer" })).StatusCode);
        var reviewer = await LoginAsync(email, "Strong-pass-2026!");

        Assert.Equal(HttpStatusCode.Forbidden, (await reviewer.GetAsync("/api/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reviewer.PostAsJsonAsync("/api/admin/users", new { email = NewEmail(), password = "Strong-pass-2026!", role = "admin" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reviewer.PostAsync($"/api/admin/users/{Guid.NewGuid()}/deactivate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.CreateClient().GetAsync("/api/admin/users")).StatusCode);
    }

    [Fact]
    public async Task Failed_role_assignment_rolls_back_the_user_and_its_audit_entry()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var http = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var actor = await users.FindByEmailAsync(BootstrapEmail);
        http.HttpContext = new DefaultHttpContext
        {
            User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                [new(System.Security.Claims.ClaimTypes.NameIdentifier, actor!.Id.ToString()), new(System.Security.Claims.ClaimTypes.Email, actor.Email!)], "test")),
        };
        var email = NewEmail();

        await Assert.ThrowsAnyAsync<Exception>(() => scope.ServiceProvider.GetRequiredService<UserAdmin>()
            .CreateAsync(email, "Strong-pass-2026!", "NoSuchRole", CancellationToken.None));

        using var check = app.Services.CreateScope();
        Assert.Null(await check.ServiceProvider.GetRequiredService<UserManager<AppUser>>().FindByEmailAsync(email));
        var creates = await check.ServiceProvider.GetRequiredService<AppDbContext>().AuditLogs
            .Where(a => a.Action == "user.create").Select(a => a.Changes).ToListAsync();
        Assert.DoesNotContain(creates, c => c.Contains(email));
    }
}
