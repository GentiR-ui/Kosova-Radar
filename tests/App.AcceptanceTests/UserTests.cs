using System.Net;

namespace App.AcceptanceTests;

[Collection(AcceptanceCollection.Name)]
public class UserTests(AppFactory app)
{
    [Theory, Trait("Req", "REQ-001")]
    [InlineData("reviewer")]
    [InlineData("admin")]
    public async Task Admin_creates_an_active_user_who_can_sign_in_with_that_role(string role)
    {
        var admin = await Radar.AdminAsync(app);
        var email = Radar.Email();

        var dto = await admin.Post("/api/admin/users", new { email, password = Radar.UserPassword, role }).Json(HttpStatusCode.Created);
        Assert.True(Guid.TryParse(dto.Str("id"), out _));
        Assert.Equal(email, dto.Str("email"), ignoreCase: true);
        Assert.Equal(role, dto.Str("role"));
        Assert.True(dto.Bool("isActive"));
        Assert.False(dto.IsNull("createdAt"));

        var listed = (await admin.Get("/api/admin/users").Json()).Array().Single(u => u.Id() == dto.Id());
        Assert.Equal(role, listed.Str("role"));
        Assert.True(listed.Bool("isActive"));

        var client = app.CreateClient();
        var me = await Radar.Login(client, email, Radar.UserPassword).Json();
        Assert.Equal(role, me.Str("role"));
        Assert.Equal(dto.Id(), me.Id("userId"));

        // The role is effective: both can use the review queue, only Admins can manage sources.
        await client.Get("/api/admin/candidates").Expect(HttpStatusCode.OK);
        await client.Get("/api/admin/sources").Expect(role == "admin" ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
    }

    [Fact, Trait("Req", "REQ-001")]
    public async Task Password_of_exactly_12_characters_is_accepted()
    {
        var admin = await Radar.AdminAsync(app);
        var user = await Radar.CreateUserAsync(admin, "reviewer", password: "Abcdef-1234!");
        await Radar.LoginAsync(app, user.Email, "Abcdef-1234!");
    }

    [Fact, Trait("Req", "REQ-001")]
    public async Task Password_shorter_than_12_characters_is_rejected_and_no_user_is_created()
    {
        var admin = await Radar.AdminAsync(app);
        var email = Radar.Email();
        await admin.Post("/api/admin/users", new { email, password = "Abcdef-123!", role = "reviewer" }).ExpectValidation("password");

        var users = (await admin.Get("/api/admin/users").Json()).Array();
        Assert.DoesNotContain(users, u => string.Equals(u.Str("email"), email, StringComparison.OrdinalIgnoreCase));
    }

    [Fact, Trait("Req", "REQ-001")]
    public async Task Unknown_role_or_invalid_email_is_rejected()
    {
        var admin = await Radar.AdminAsync(app);
        await admin.Post("/api/admin/users", new { email = Radar.Email(), password = Radar.UserPassword, role = "owner" })
            .ExpectValidation("role");
        await admin.Post("/api/admin/users", new { email = "no-at-sign", password = Radar.UserPassword, role = "reviewer" })
            .ExpectValidation("email");
    }

    [Fact, Trait("Req", "REQ-002")]
    public async Task Duplicate_email_gets_409_and_no_second_user()
    {
        var admin = await Radar.AdminAsync(app);
        var user = await Radar.CreateUserAsync(admin, "reviewer");

        await admin.Post("/api/admin/users", new { email = user.Email, password = "Other-pass-2026!", role = "admin" })
            .Expect(HttpStatusCode.Conflict);
        await admin.Post("/api/admin/users", new { email = user.Email.ToUpperInvariant(), password = "Other-pass-2026!", role = "admin" })
            .Expect(HttpStatusCode.Conflict);

        var users = (await admin.Get("/api/admin/users").Json()).Array();
        var matching = users.Where(u => string.Equals(u.Str("email"), user.Email, StringComparison.OrdinalIgnoreCase)).ToList();
        var only = Assert.Single(matching);
        Assert.Equal("reviewer", only.Str("role"));

        // The original credentials still work; the second password does not.
        await Radar.LoginAsync(app, user.Email, user.Password);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Radar.Login(app.CreateClient(), user.Email, "Other-pass-2026!")).StatusCode);
    }

    [Fact, Trait("Req", "REQ-002")]
    public async Task Bootstrap_admin_email_cannot_be_created_again()
    {
        var admin = await Radar.AdminAsync(app);
        await admin.Post("/api/admin/users", new { email = Radar.AdminEmail, password = Radar.UserPassword, role = "reviewer" })
            .Expect(HttpStatusCode.Conflict);
        var users = (await admin.Get("/api/admin/users").Json()).Array();
        Assert.Single(users, u => string.Equals(u.Str("email"), Radar.AdminEmail, StringComparison.OrdinalIgnoreCase));
    }

    [Fact, Trait("Req", "REQ-003")]
    public async Task Deactivated_user_cannot_log_in_and_existing_session_ends()
    {
        var admin = await Radar.AdminAsync(app);
        var (session, user) = await Radar.SignedInUserAsync(app, admin, "reviewer");
        await session.Get("/api/admin/candidates").Expect(HttpStatusCode.OK);

        await admin.Post($"/api/admin/users/{user.Id}/deactivate").Expect(HttpStatusCode.NoContent);

        await session.Get("/api/admin/candidates").Expect(HttpStatusCode.Unauthorized);

        var deactivatedLogin = await Radar.Login(app.CreateClient(), user.Email, user.Password);
        Assert.Equal(HttpStatusCode.Unauthorized, deactivatedLogin.StatusCode);
        var unknownLogin = await Radar.Login(app.CreateClient(), Radar.Email(), user.Password);
        var a = JsonExt.Parse(await deactivatedLogin.Content.ReadAsStringAsync());
        var b = JsonExt.Parse(await unknownLogin.Content.ReadAsStringAsync());
        Assert.Equal(b.Str("title"), a.Str("title"));
        Assert.Equal(b.Str("detail"), a.Str("detail"));

        var listed = (await admin.Get("/api/admin/users").Json()).Array().Single(u => u.Id() == user.Id);
        Assert.False(listed.Bool("isActive"));
    }

    [Fact, Trait("Req", "REQ-003")]
    public async Task Deactivated_admin_session_ends_on_its_next_admin_request()
    {
        var admin = await Radar.AdminAsync(app);
        var (session, user) = await Radar.SignedInUserAsync(app, admin, "admin");
        await session.Get("/api/admin/users").Expect(HttpStatusCode.OK);

        await admin.Post($"/api/admin/users/{user.Id}/deactivate").Expect(HttpStatusCode.NoContent);

        await session.Get("/api/admin/users").Expect(HttpStatusCode.Unauthorized);
    }

    [Fact, Trait("Req", "REQ-003")]
    public async Task Deactivating_an_unknown_user_gets_404()
    {
        var admin = await Radar.AdminAsync(app);
        await admin.Post($"/api/admin/users/{Guid.NewGuid()}/deactivate").Expect(HttpStatusCode.NotFound);
    }

    [Fact, Trait("Req", "REQ-004")]
    public async Task Admin_cannot_deactivate_their_own_account()
    {
        var bootstrap = await Radar.AdminAsync(app);
        var (self, user) = await Radar.SignedInUserAsync(app, bootstrap, "admin");

        await self.Post($"/api/admin/users/{user.Id}/deactivate").Expect(HttpStatusCode.BadRequest);

        await self.Get("/api/admin/users").Expect(HttpStatusCode.OK);
        var listed = (await bootstrap.Get("/api/admin/users").Json()).Array().Single(u => u.Id() == user.Id);
        Assert.True(listed.Bool("isActive"));
        await Radar.LoginAsync(app, user.Email, user.Password);
    }
}
