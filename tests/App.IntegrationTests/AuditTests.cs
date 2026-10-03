using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using App.Application.Audit;
using App.Application.Auth;
using App.Infrastructure.Identity;
using App.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using App.Application.Abstractions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace App.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public class AuditTests(AppFactory app)
{
    private const string Password = "Integration-pass-1";

    private async Task<(AppUser User, HttpClient Client)> SignedInAsync(string role)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var email = $"a{Guid.NewGuid():N}@test.local";
        var user = new AppUser { UserName = email, Email = email, CreatedAt = DateTimeOffset.UtcNow };
        Assert.True((await users.CreateAsync(user, Password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password })).StatusCode);
        return (user, client);
    }

    /// <summary>Writes entries the way a use case does: record, then save with the action's own SaveChanges.</summary>
    private async Task RecordAsync(AppUser actor, params (string Action, string Type, Guid Id, object? Before, object? After)[] entries)
    {
        await using var scope = app.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, actor.Id.ToString()), new Claim(ClaimTypes.Email, actor.Email!)], "test")),
        };
        var writer = scope.ServiceProvider.GetRequiredService<IAuditWriter>();
        foreach (var e in entries)
        {
            writer.Record(e.Action, e.Type, e.Id, e.Before, e.After);
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().SaveChangesAsync(); // one commit per audited action
            await Task.Delay(5); // distinct timestamps
        }
    }

    [Fact]
    public async Task Entries_are_written_and_read_back_newest_first_filtered_by_entity()
    {
        var (admin, _) = await SignedInAsync(Roles.Admin);
        var (_, reviewer) = await SignedInAsync(Roles.Reviewer);
        var entityId = Guid.NewGuid();
        await RecordAsync(admin,
            (AuditActions.SourceCreate, AuditEntityTypes.Source, entityId, null, new { name = "Burimi", isActive = true }),
            (AuditActions.SourceUpdate, AuditEntityTypes.Source, entityId, new { name = "Burimi" }, new { name = "Burimi i ri" }),
            (AuditActions.SourceUpdate, AuditEntityTypes.Source, Guid.NewGuid(), null, null));

        var res = await reviewer.GetAsync($"/api/admin/audit?entityId={entityId}");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var page = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(2, page.GetProperty("totalCount").GetInt32());
        var items = page.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["source.update", "source.create"], items.Select(i => i.GetProperty("action").GetString()));
        var update = items[0];
        Assert.Equal(admin.Id, update.GetProperty("userId").GetGuid());
        Assert.Equal(admin.Email, update.GetProperty("userEmail").GetString());
        Assert.Equal("source", update.GetProperty("entityType").GetString());
        Assert.Equal("Burimi", update.GetProperty("changes").GetProperty("before").GetProperty("name").GetString());
        Assert.Equal("Burimi i ri", update.GetProperty("changes").GetProperty("after").GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, items[1].GetProperty("changes").GetProperty("before").ValueKind);
        Assert.Equal(TimeSpan.Zero, update.GetProperty("at").GetDateTimeOffset().Offset);
    }

    [Fact]
    public async Task Password_fields_never_reach_the_log()
    {
        var (admin, adminClient) = await SignedInAsync(Roles.Admin);
        var entityId = Guid.NewGuid();
        await RecordAsync(admin, (AuditActions.UserCreate, AuditEntityTypes.User, entityId, null,
            new { email = "x@test.local", password = "Secret-pass-12345", passwordHash = "h", nested = new { newPassword = "n", apiKey = "k" } }));

        var raw = await adminClient.GetStringAsync($"/api/admin/audit?entityId={entityId}");

        Assert.Contains("x@test.local", raw);
        Assert.DoesNotContain("password", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Secret-pass", raw);
    }

    [Fact]
    public async Task Stored_changes_are_jsonb_with_before_and_after()
    {
        var (admin, _) = await SignedInAsync(Roles.Admin);
        var entityId = Guid.NewGuid();
        await RecordAsync(admin, (AuditActions.ItemEdit, AuditEntityTypes.Event, entityId, new { Title = "A" }, new { Title = "B" }));

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var json = await db.AuditLogs.AsNoTracking().Where(a => a.EntityId == entityId).Select(a => a.Changes).SingleAsync();
        var changes = JsonDocument.Parse(json).RootElement;
        Assert.Equal("A", changes.GetProperty("before").GetProperty("title").GetString()); // camelCase
        Assert.Equal("B", changes.GetProperty("after").GetProperty("title").GetString());
    }

    [Fact]
    public async Task Recording_without_a_signed_in_user_fails()
    {
        await using var scope = app.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext();
        var writer = scope.ServiceProvider.GetRequiredService<IAuditWriter>();

        Assert.Throws<InvalidOperationException>(() => writer.Record(AuditActions.ItemExpire, AuditEntityTypes.Deal, Guid.NewGuid()));
    }

    [Fact]
    public async Task Paging_is_validated_and_a_page_past_the_end_is_empty()
    {
        var (_, client) = await SignedInAsync(Roles.Reviewer);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/admin/audit?pageSize=101")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/admin/audit?page=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/admin/audit?entityId=nope")).StatusCode);
        var far = JsonDocument.Parse(await client.GetStringAsync("/api/admin/audit?page=100000&pageSize=5")).RootElement;
        Assert.Empty(far.GetProperty("items").EnumerateArray());
        Assert.Equal(100000, far.GetProperty("page").GetInt32());
        var huge = JsonDocument.Parse(await client.GetStringAsync("/api/admin/audit?page=2147483647&pageSize=100")).RootElement;
        Assert.Empty(huge.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Audit_endpoint_needs_a_signed_in_Admin_or_Reviewer()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.CreateClient().GetAsync("/api/admin/audit")).StatusCode);
        foreach (var role in Roles.All)
        {
            var (_, client) = await SignedInAsync(role);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/admin/audit")).StatusCode);
        }
    }

    [Fact]
    public async Task The_cookie_principal_of_a_real_request_carries_the_id_and_email_the_writer_needs()
    {
        var (user, _) = await SignedInAsync(Roles.Reviewer);
        using var host = app.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddTransient<IStartupFilter, WhoAmIFilter>()));
        var client = host.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = Password })).StatusCode);

        var who = JsonDocument.Parse(await client.GetStringAsync("/_test/whoami")).RootElement;

        Assert.Equal(user.Id, who.GetProperty("id").GetGuid());
        Assert.Equal(user.Email, who.GetProperty("email").GetString());
    }

    /// <summary>Authenticates with the real cookie scheme and resolves ICurrentUser, exactly as an audited endpoint would.</summary>
    private sealed class WhoAmIFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Map("/_test/whoami", branch => branch.Run(async ctx =>
            {
                ctx.User = (await ctx.AuthenticateAsync(IdentityConstants.ApplicationScheme)).Principal!;
                var (id, email) = ctx.RequestServices.GetRequiredService<ICurrentUser>().Require();
                await ctx.Response.WriteAsJsonAsync(new { id, email });
            }));
            next(app);
        };
    }

    [Fact]
    public async Task An_entry_is_only_stored_by_the_callers_SaveChanges_and_rolls_back_with_it()
    {
        var (admin, _) = await SignedInAsync(Roles.Admin);
        var unsaved = Guid.NewGuid();
        var rolledBack = Guid.NewGuid();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            Authenticate(scope, admin);
            scope.ServiceProvider.GetRequiredService<IAuditWriter>().Record(AuditActions.ItemWithdraw, AuditEntityTypes.Event, unsaved);
        } // disposed without SaveChanges
        await using (var scope = app.Services.CreateAsyncScope())
        {
            Authenticate(scope, admin);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync();
            scope.ServiceProvider.GetRequiredService<IAuditWriter>().Record(AuditActions.ItemWithdraw, AuditEntityTypes.Event, rolledBack);
            await db.SaveChangesAsync();
            await tx.RollbackAsync(); // the action failed after saving: no audit entry may remain
        }

        await using var check = app.Services.CreateAsyncScope();
        var checkDb = check.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await checkDb.AuditLogs.AnyAsync(a => a.EntityId == unsaved || a.EntityId == rolledBack));
    }

    [Fact]
    public async Task Stored_entries_cannot_be_modified_or_deleted()
    {
        var (admin, _) = await SignedInAsync(Roles.Admin);
        var entityId = Guid.NewGuid();
        await RecordAsync(admin, (AuditActions.UserCreate, AuditEntityTypes.User, entityId, null, null));

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entry = await db.AuditLogs.SingleAsync(a => a.EntityId == entityId);
        entry.Action = "tampered";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.AuditLogs.Remove(await db.AuditLogs.SingleAsync(a => a.EntityId == entityId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    private static void Authenticate(AsyncServiceScope scope, AppUser actor) =>
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, actor.Id.ToString()), new Claim(ClaimTypes.Email, actor.Email!)], "test")),
        };
}
