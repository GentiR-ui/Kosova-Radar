using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Application.Auth;
using App.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace App.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public class AuthTests(AppFactory app)
{
    private const string Password = "Integration-pass-1";

    private async Task<AppUser> SeedUserAsync(string role, bool isActive = true)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var email = $"i{Guid.NewGuid():N}@test.local";
        var user = new AppUser { UserName = email, Email = email, IsActive = isActive, CreatedAt = DateTimeOffset.UtcNow };
        Assert.True((await users.CreateAsync(user, Password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        return user;
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient c, string email, string password) =>
        c.PostAsJsonAsync("/api/auth/login", new { email, password });

    private static async Task<(string? Title, string? Detail)> ProblemAsync(HttpResponseMessage res)
    {
        var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        return (json.GetProperty("title").GetString(), json.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Wrong_password_unknown_email_and_inactive_user_get_the_same_401_without_a_cookie()
    {
        var active = await SeedUserAsync(Roles.Reviewer);
        var inactive = await SeedUserAsync(Roles.Reviewer, isActive: false);

        var responses = new[]
        {
            await LoginAsync(app.CreateClient(), active.Email!, "Wrong-password-123"),
            await LoginAsync(app.CreateClient(), $"x{Guid.NewGuid():N}@test.local", Password),
            await LoginAsync(app.CreateClient(), inactive.Email!, Password), // correct password, inactive user
        };

        var bodies = new List<(string?, string?)>();
        foreach (var res in responses)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
            Assert.False(res.Headers.Contains("Set-Cookie"));
            bodies.Add(await ProblemAsync(res));
        }
        Assert.Single(bodies.Distinct());
    }

    [Fact]
    public async Task Me_returns_the_role_in_wire_format()
    {
        var reviewer = await SeedUserAsync(Roles.Reviewer);
        var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, reviewer.Email!, Password)).StatusCode);

        var me = JsonDocument.Parse(await client.GetStringAsync("/api/me")).RootElement;
        Assert.Equal("reviewer", me.GetProperty("role").GetString());
        Assert.Equal(reviewer.Id, me.GetProperty("userId").GetGuid());
    }

    [Fact]
    public async Task Changing_the_security_stamp_ends_an_existing_session_on_the_next_request()
    {
        var user = await SeedUserAsync(Roles.Reviewer);
        var client = app.CreateClient();
        await LoginAsync(client, user.Email!, Password);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me")).StatusCode);

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            await users.UpdateSecurityStampAsync((await users.FindByIdAsync(user.Id.ToString()))!);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public async Task A_replayed_copy_of_the_cookie_is_rejected_after_logout()
    {
        var user = await SeedUserAsync(Roles.Admin);
        var client = app.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var login = await LoginAsync(client, user.Email!, Password);
        var cookie = string.Join("; ", login.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]));

        HttpRequestMessage WithCookie(HttpMethod m, string url)
        {
            var req = new HttpRequestMessage(m, url);
            req.Headers.Add("Cookie", cookie);
            return req;
        }

        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(WithCookie(HttpMethod.Get, "/api/me"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(WithCookie(HttpMethod.Post, "/api/auth/logout"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(WithCookie(HttpMethod.Get, "/api/me"))).StatusCode);
    }

    [Fact]
    public async Task Deactivated_user_loses_the_session_even_without_a_new_security_stamp()
    {
        var user = await SeedUserAsync(Roles.Reviewer);
        var client = app.CreateClient();
        await LoginAsync(client, user.Email!, Password);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me")).StatusCode);

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var stored = (await users.FindByIdAsync(user.Id.ToString()))!;
            var stamp = stored.SecurityStamp;
            stored.IsActive = false;
            Assert.True((await users.UpdateAsync(stored)).Succeeded);
            Assert.Equal(stamp, (await users.FindByIdAsync(user.Id.ToString()))!.SecurityStamp);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/anything")).StatusCode);
    }

    [Fact]
    public async Task A_user_created_inactive_stays_inactive()
    {
        var user = await SeedUserAsync(Roles.Reviewer, isActive: false);
        await using var scope = app.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        Assert.False((await users.FindByIdAsync(user.Id.ToString()))!.IsActive);
    }

    [Fact]
    public async Task Logging_out_one_session_keeps_the_users_other_sessions()
    {
        var user = await SeedUserAsync(Roles.Admin);
        var first = app.CreateClient();
        var second = app.CreateClient();
        await LoginAsync(first, user.Email!, Password);
        await LoginAsync(second, user.Email!, Password);

        Assert.Equal(HttpStatusCode.NoContent, (await first.PostAsync("/api/auth/logout", null)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await first.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public async Task Login_throttle_counts_failures_only_and_answers_429_after_eleven()
    {
        var user = await SeedUserAsync(Roles.Reviewer);
        var client = app.CreateClient();

        // Invalid bodies (400) and successful logins are not counted.
        for (var i = 0; i < 15; i++)
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = "" })).StatusCode);
        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, user.Email!, "Wrong-password-123")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, user.Email!, Password)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, user.Email!, "Wrong-password-123")).StatusCode);
        var blocked = await LoginAsync(client, user.Email!, Password);
        Assert.Equal((HttpStatusCode)429, blocked.StatusCode);
        Assert.Equal("application/problem+json", blocked.Content.Headers.ContentType?.MediaType);
    }
}
