using System.Net;

namespace App.AcceptanceTests;

/// <summary>REQ-P04: a signed-in Reviewer gets 403 on every Admin-only endpoint (sources, users) and nothing changes.</summary>
[Collection(AcceptanceCollection.Name)]
public class RoleTests(AppFactory app)
{
    private async Task<(HttpClient Admin, HttpClient Reviewer)> ClientsAsync()
    {
        var admin = await Radar.AdminAsync(app);
        var (reviewer, _) = await Radar.SignedInUserAsync(app, admin, "reviewer");
        return (admin, reviewer);
    }

    [Fact, Trait("Req", "REQ-P04")]
    public async Task Reviewer_cannot_list_sources()
    {
        var (_, reviewer) = await ClientsAsync();
        await reviewer.Get("/api/admin/sources").Expect(HttpStatusCode.Forbidden);
    }

    [Fact, Trait("Req", "REQ-P04")]
    public async Task Reviewer_cannot_read_a_source()
    {
        var (admin, reviewer) = await ClientsAsync();
        var source = await Radar.CreateSourceAsync(admin, Radar.FixtureSource(Radar.Payload()));
        await reviewer.Get($"/api/admin/sources/{source.Id()}").Expect(HttpStatusCode.Forbidden);
    }

    [Fact, Trait("Req", "REQ-P04")]
    public async Task Reviewer_cannot_create_a_source_and_nothing_is_created()
    {
        var (admin, reviewer) = await ClientsAsync();
        var name = $"Burim {Radar.Token()}";
        await reviewer.Post("/api/admin/sources", Radar.FixtureSource(Radar.Payload(), name)).Expect(HttpStatusCode.Forbidden);

        var sources = (await admin.Get("/api/admin/sources").Json()).Array();
        Assert.DoesNotContain(sources, s => s.Str("name") == name);
    }

    [Fact, Trait("Req", "REQ-P04")]
    public async Task Reviewer_cannot_update_a_source_and_it_stays_unchanged()
    {
        var (admin, reviewer) = await ClientsAsync();
        var body = Radar.FixtureSource(Radar.Payload());
        var source = await Radar.CreateSourceAsync(admin, body);
        var originalName = source.Str("name");

        var changed = Radar.FixtureSource(Radar.Payload(), $"Ndryshuar {Radar.Token()}", isUnreliable: true, pollIntervalMinutes: 5);
        await reviewer.Put($"/api/admin/sources/{source.Id()}", changed).Expect(HttpStatusCode.Forbidden);

        var after = await Radar.SourceAsync(admin, source.Id());
        Assert.Equal(originalName, after.Str("name"));
        Assert.False(after.Bool("isUnreliable"));
        Assert.Equal(1440, after.Int("pollIntervalMinutes"));
    }

    [Fact, Trait("Req", "REQ-P04")]
    public async Task Reviewer_cannot_fetch_a_source_and_no_run_happens()
    {
        var (admin, reviewer) = await ClientsAsync();
        var source = await Radar.CreateSourceAsync(admin,
            Radar.FixtureSource(Radar.Payload(Radar.EventItem(Radar.Title(Radar.Token())))));

        await reviewer.Post($"/api/admin/sources/{source.Id()}/fetch").Expect(HttpStatusCode.Forbidden);

        var after = await Radar.SourceAsync(admin, source.Id());
        Assert.True(after.IsNull("lastFetchedAt"));
        Assert.Empty(after.GetProperty("recentRuns").Array());
        Assert.Equal(0, (await Radar.CandidatePageAsync(admin, source.Id())).Int("totalCount"));
    }

    [Fact, Trait("Req", "REQ-P04")]
    public async Task Reviewer_cannot_list_users()
    {
        var (_, reviewer) = await ClientsAsync();
        await reviewer.Get("/api/admin/users").Expect(HttpStatusCode.Forbidden);
    }

    [Fact, Trait("Req", "REQ-P04")]
    public async Task Reviewer_cannot_create_a_user_and_nothing_is_created()
    {
        var (admin, reviewer) = await ClientsAsync();
        var email = Radar.Email();
        await reviewer.Post("/api/admin/users", new { email, password = Radar.UserPassword, role = "admin" })
            .Expect(HttpStatusCode.Forbidden);

        var users = (await admin.Get("/api/admin/users").Json()).Array();
        Assert.DoesNotContain(users, u => string.Equals(u.Str("email"), email, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Radar.Login(app.CreateClient(), email, Radar.UserPassword)).StatusCode);
    }

    [Fact, Trait("Req", "REQ-P04")]
    public async Task Reviewer_cannot_deactivate_a_user_and_the_user_stays_active()
    {
        var (admin, reviewer) = await ClientsAsync();
        var victim = await Radar.CreateUserAsync(admin, "reviewer");

        await reviewer.Post($"/api/admin/users/{victim.Id}/deactivate").Expect(HttpStatusCode.Forbidden);

        var users = (await admin.Get("/api/admin/users").Json()).Array();
        Assert.True(users.Single(u => u.Id() == victim.Id).Bool("isActive"));
        Assert.Equal(HttpStatusCode.OK, (await Radar.Login(app.CreateClient(), victim.Email, victim.Password)).StatusCode);
    }
}
