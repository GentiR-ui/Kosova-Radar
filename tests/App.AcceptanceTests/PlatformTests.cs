using System.Net;
using System.Text.Json;

namespace App.AcceptanceTests;

[Collection(AcceptanceCollection.Name)]
public class PlatformTests(AppFactory app)
{
    [Fact, Trait("Req", "REQ-P01")]
    public async Task Bootstrap_admin_exists_and_can_sign_in_as_admin()
    {
        var client = app.CreateClient();
        var me = await Radar.Login(client, Radar.AdminEmail, Radar.AdminPassword).Json();
        Assert.Equal(Radar.AdminEmail, me.Str("email"));
        Assert.Equal("admin", me.Str("role"));
        Assert.True(Guid.TryParse(me.Str("userId"), out _));

        var again = await client.Get("/api/me").Json();
        Assert.Equal(Radar.AdminEmail, again.Str("email"));
        Assert.Equal("admin", again.Str("role"));

        var users = (await client.Get("/api/admin/users").Json()).Array();
        var bootstrap = Assert.Single(users, u => string.Equals(u.Str("email"), Radar.AdminEmail, StringComparison.OrdinalIgnoreCase));
        Assert.True(bootstrap.Bool("isActive"));
        Assert.Equal("admin", bootstrap.Str("role"));
    }

    [Fact, Trait("Req", "REQ-P02")]
    public async Task Wrong_password_and_unknown_email_get_the_same_generic_401()
    {
        var admin = await Radar.AdminAsync(app);
        var user = await Radar.CreateUserAsync(admin, "reviewer");

        var wrongPassword = await Radar.Login(app.CreateClient(), user.Email, "Wrong-password-123");
        var unknownEmail = await Radar.Login(app.CreateClient(), Radar.Email(), "Wrong-password-123");

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        var a = JsonExt.Parse(await wrongPassword.Content.ReadAsStringAsync());
        var b = JsonExt.Parse(await unknownEmail.Content.ReadAsStringAsync());
        Assert.Equal(a.Str("title"), b.Str("title"));
        Assert.Equal(a.Str("detail"), b.Str("detail"));
        Assert.False(wrongPassword.Headers.Contains("Set-Cookie"));
    }

    [Theory, Trait("Req", "REQ-P03")]
    [InlineData("GET", "/api/me")]
    [InlineData("GET", "/api/admin/dashboard")]
    [InlineData("GET", "/api/admin/sources")]
    [InlineData("GET", "/api/admin/sources/00000000-0000-0000-0000-000000000001")]
    [InlineData("POST", "/api/admin/sources")]
    [InlineData("PUT", "/api/admin/sources/00000000-0000-0000-0000-000000000001")]
    [InlineData("POST", "/api/admin/sources/00000000-0000-0000-0000-000000000001/fetch")]
    [InlineData("GET", "/api/admin/candidates")]
    [InlineData("GET", "/api/admin/candidates/00000000-0000-0000-0000-000000000001")]
    [InlineData("PUT", "/api/admin/candidates/00000000-0000-0000-0000-000000000001")]
    [InlineData("POST", "/api/admin/candidates/00000000-0000-0000-0000-000000000001/approve")]
    [InlineData("POST", "/api/admin/candidates/00000000-0000-0000-0000-000000000001/reject")]
    [InlineData("POST", "/api/admin/candidates/00000000-0000-0000-0000-000000000001/merge")]
    [InlineData("GET", "/api/admin/items")]
    [InlineData("GET", "/api/admin/events/00000000-0000-0000-0000-000000000001")]
    [InlineData("PUT", "/api/admin/events/00000000-0000-0000-0000-000000000001")]
    [InlineData("POST", "/api/admin/events/00000000-0000-0000-0000-000000000001/expire")]
    [InlineData("POST", "/api/admin/events/00000000-0000-0000-0000-000000000001/withdraw")]
    [InlineData("GET", "/api/admin/deals/00000000-0000-0000-0000-000000000001")]
    [InlineData("PUT", "/api/admin/deals/00000000-0000-0000-0000-000000000001")]
    [InlineData("POST", "/api/admin/deals/00000000-0000-0000-0000-000000000001/expire")]
    [InlineData("POST", "/api/admin/deals/00000000-0000-0000-0000-000000000001/withdraw")]
    [InlineData("GET", "/api/admin/audit")]
    [InlineData("GET", "/api/admin/users")]
    [InlineData("POST", "/api/admin/users")]
    [InlineData("POST", "/api/admin/users/00000000-0000-0000-0000-000000000001/deactivate")]
    public async Task Unauthenticated_requests_to_admin_endpoints_and_me_get_401(string method, string path)
    {
        var client = app.CreateClient();
        var body = method == "GET" ? null : new { };
        var res = await client.Send(new HttpMethod(method), path, body);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact, Trait("Req", "REQ-P05")]
    public async Task Invalid_login_body_gets_problem_details_naming_each_field()
    {
        var client = app.CreateClient();
        var res = await client.Post("/api/auth/login", new { email = "", password = "" });
        Assert.Equal("application/problem+json", res.Content.Headers.ContentType?.MediaType);
        await Task.FromResult(res).ExpectValidation("email", "password");

        await client.Post("/api/auth/login", new { email = Radar.Email() }).ExpectValidation("password");
    }

    [Fact, Trait("Req", "REQ-P05")]
    public async Task Invalid_query_gets_problem_details_naming_the_parameter()
    {
        var client = app.CreateClient();
        var res = await client.Get("/api/events?pageSize=0");
        Assert.Equal("application/problem+json", res.Content.Headers.ContentType?.MediaType);
        await Task.FromResult(res).ExpectValidation("pageSize");
    }

    [Fact, Trait("Req", "REQ-P05")]
    public async Task Invalid_admin_body_gets_problem_details_naming_each_field()
    {
        var admin = await Radar.AdminAsync(app);
        var res = await admin.Post("/api/admin/users", new { email = "not-an-email", password = "short", role = "superuser" });
        Assert.Equal("application/problem+json", res.Content.Headers.ContentType?.MediaType);
        await Task.FromResult(res).ExpectValidation("email", "password", "role");
    }

    [Fact, Trait("Req", "REQ-P06")]
    public async Task More_than_ten_failed_logins_for_one_email_are_throttled_with_429()
    {
        var admin = await Radar.AdminAsync(app);
        var user = await Radar.CreateUserAsync(admin, "reviewer");
        var client = app.CreateClient();

        for (var i = 1; i <= 11; i++)
        {
            var res = await Radar.Login(client, user.Email, "Wrong-password-123");
            Assert.True(res.StatusCode == HttpStatusCode.Unauthorized, $"attempt {i}: expected 401, got {(int)res.StatusCode}");
        }

        // 11 failures in the window: further attempts for that email are refused, even with the right password.
        var blocked = await Radar.Login(client, user.Email, user.Password);
        Assert.Equal((HttpStatusCode)429, blocked.StatusCode);

        // Another email from the same client is not affected.
        var other = await Radar.CreateUserAsync(admin, "reviewer");
        Assert.Equal(HttpStatusCode.OK, (await Radar.Login(app.CreateClient(), other.Email, other.Password)).StatusCode);
    }

    [Fact, Trait("Req", "REQ-P06")]
    public async Task Ten_failed_logins_are_not_yet_throttled()
    {
        var admin = await Radar.AdminAsync(app);
        var user = await Radar.CreateUserAsync(admin, "reviewer");
        var client = app.CreateClient();
        for (var i = 1; i <= 10; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await Radar.Login(client, user.Email, "Wrong-password-123")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await Radar.Login(client, user.Email, user.Password)).StatusCode);
    }

    [Fact, Trait("Req", "REQ-P06")]
    public async Task Throttled_email_can_log_in_again_after_the_minute_has_passed()
    {
        var admin = await Radar.AdminAsync(app);
        var user = await Radar.CreateUserAsync(admin, "reviewer");
        var client = app.CreateClient();
        for (var i = 1; i <= 11; i++) await Radar.Login(client, user.Email, "Wrong-password-123");
        Assert.Equal((HttpStatusCode)429, (await Radar.Login(client, user.Email, user.Password)).StatusCode);

        await Task.Delay(TimeSpan.FromSeconds(62));

        Assert.Equal(HttpStatusCode.OK, (await Radar.Login(client, user.Email, user.Password)).StatusCode);
    }

    [Fact, Trait("Req", "REQ-P07")]
    public async Task Health_endpoint_responds_200_when_database_is_reachable()
    {
        var res = await app.CreateClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Theory, Trait("Req", "REQ-P08")]
    [InlineData("/health")]
    [InlineData("/api/events")]
    [InlineData("/api/me")]
    [InlineData("/api/does-not-exist")]
    [InlineData("/robots.txt")]
    [InlineData("/sitemap.xml")]
    [InlineData("/")]
    public async Task Security_headers_are_sent_on_every_response(string path)
    {
        var res = await app.CreateClient().GetAsync(path);
        string Header(string name) =>
            res.Headers.TryGetValues(name, out var v) ? string.Join(",", v)
            : res.Content.Headers.TryGetValues(name, out var c) ? string.Join(",", c) : "";

        Assert.Equal("nosniff", Header("X-Content-Type-Options"));
        Assert.Equal("DENY", Header("X-Frame-Options"));
        Assert.Equal("strict-origin-when-cross-origin", Header("Referrer-Policy"));
        Assert.Equal(
            "default-src 'self'; img-src 'self' https: data:; font-src 'self' data:; object-src 'none'; frame-ancestors 'none'; base-uri 'self'",
            Header("Content-Security-Policy"));
    }

    [Fact, Trait("Req", "REQ-P08")]
    public async Task Security_headers_are_sent_on_authenticated_and_error_responses()
    {
        var admin = await Radar.AdminAsync(app);
        foreach (var res in new[] { await admin.Get("/api/admin/dashboard"), await admin.Post("/api/admin/users", new { email = "" }) })
        {
            Assert.Equal("nosniff", string.Join(",", res.Headers.GetValues("X-Content-Type-Options")));
            Assert.Equal("DENY", string.Join(",", res.Headers.GetValues("X-Frame-Options")));
            Assert.Equal("strict-origin-when-cross-origin", string.Join(",", res.Headers.GetValues("Referrer-Policy")));
            Assert.True(res.Headers.Contains("Content-Security-Policy"));
        }
    }

    [Fact, Trait("Req", "REQ-P09")]
    public async Task Unhandled_error_returns_500_problem_details_without_internals()
    {
        var res = await app.CreateClient().GetAsync("/api/_test/throw");
        Assert.Equal(HttpStatusCode.InternalServerError, res.StatusCode);
        Assert.Equal("application/problem+json", res.Content.Headers.ContentType?.MediaType);
        var text = await res.Content.ReadAsStringAsync();
        var json = JsonExt.Parse(text);
        Assert.Equal(JsonValueKind.Object, json.ValueKind);
        Assert.Equal(500, json.GetProperty("status").GetInt32());
        Assert.DoesNotContain("TEST-SECRET-EXCEPTION", text);
        Assert.DoesNotContain("Exception", text);
        Assert.DoesNotMatch(@"\bat [A-Za-z_][\w.]*\(", text);
        Assert.DoesNotContain(".cs:line", text);
    }

    [Fact, Trait("Req", "REQ-P11")]
    public async Task Logout_ends_the_session()
    {
        var admin = await Radar.AdminAsync(app);
        await admin.Get("/api/me").Expect(HttpStatusCode.OK);

        await admin.Post("/api/auth/logout").Expect(HttpStatusCode.NoContent);

        await admin.Get("/api/me").Expect(HttpStatusCode.Unauthorized);
        await admin.Get("/api/admin/dashboard").Expect(HttpStatusCode.Unauthorized);
        await admin.Get("/api/admin/candidates").Expect(HttpStatusCode.Unauthorized);
    }
}
