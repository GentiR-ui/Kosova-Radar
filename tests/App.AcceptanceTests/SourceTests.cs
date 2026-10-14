using System.Net;
using System.Text.Json.Nodes;

namespace App.AcceptanceTests;

[Collection(AcceptanceCollection.Name)]
public class SourceTests(AppFactory app)
{
    private const string Unreachable = "https://kosovaradar-test.invalid/feed.json";

    [Fact, Trait("Req", "REQ-010")]
    public async Task Admin_creates_an_active_fixture_source_with_health_never()
    {
        var admin = await Radar.AdminAsync(app);
        var name = $"Burim {Radar.Token()}";
        var payload = Radar.Payload();
        var body = Radar.FixtureSource(payload, name, isActive: true, pollIntervalMinutes: 1440);

        var res = await admin.Post("/api/admin/sources", body).Expect(HttpStatusCode.Created);
        var dto = await Task.FromResult(res).Json(HttpStatusCode.Created);

        Assert.Equal("never", dto.Str("health"));
        Assert.Equal(name, dto.Str("name"));
        Assert.Equal("fixture", dto.Str("type"));
        Assert.True(dto.IsNull("baseUrl"));
        Assert.Equal("event", dto.Str("defaultKind"));
        Assert.True(dto.Bool("isActive"));
        Assert.False(dto.Bool("isUnreliable"));
        Assert.Equal(1440, dto.Int("pollIntervalMinutes"));
        Assert.True(dto.IsNull("lastSuccessAt"));
        Assert.True(dto.IsNull("lastErrorAt"));
        Assert.Equal(0, dto.Int("consecutiveFailures"));
        Assert.NotNull(res.Headers.Location);
        Assert.Contains(dto.Id().ToString(), res.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);

        var detail = await Radar.SourceAsync(admin, dto.Id());
        Assert.Equal(payload, detail.Str("fixturePayload"));
        Assert.Contains((await admin.Get("/api/admin/sources").Json()).Array(), s => s.Id() == dto.Id());
    }

    [Theory, Trait("Req", "REQ-010")]
    [InlineData("jsonFeed", Unreachable, 15)]
    [InlineData("rss", "http://kosovaradar-test.invalid/rss.xml", 1440)]
    public async Task Admin_creates_url_sources_of_each_type(string type, string url, int poll)
    {
        var admin = await Radar.AdminAsync(app);
        var body = Radar.UrlSource(type, url, isActive: true, pollIntervalMinutes: poll, defaultKind: "deal");

        var dto = await Radar.CreateSourceAsync(admin, body);

        Assert.Equal(type, dto.Str("type"));
        Assert.Equal(url, dto.Str("baseUrl"));
        Assert.Equal("deal", dto.Str("defaultKind"));
        Assert.Equal(poll, dto.Int("pollIntervalMinutes"));
        Assert.Equal("never", dto.Str("health"));

        // Not left active: the test only needs the creation response.
        body["isActive"] = false;
        await Radar.UpdateSourceAsync(admin, dto.Id(), body);
    }

    [Theory, Trait("Req", "REQ-010")]
    [InlineData("prishtinaEvents")]
    [InlineData("kapodalim")]
    [InlineData("gjirafaMall")]
    [InlineData("foleja")]
    public async Task Admin_creates_an_html_source_with_a_site_adapter(string adapter)
    {
        var admin = await Radar.AdminAsync(app);
        var body = Radar.UrlSource("html", "https://kosovaradar-test.invalid/events", isActive: true, pollIntervalMinutes: 180);
        body["adapter"] = adapter;

        var dto = await Radar.CreateSourceAsync(admin, body);

        Assert.Equal("html", dto.Str("type"));
        Assert.Equal(adapter, dto.Str("adapter"));
        Assert.Equal("https://kosovaradar-test.invalid/events", dto.Str("baseUrl"));
        Assert.Equal(180, dto.Int("pollIntervalMinutes"));
        Assert.Equal("never", dto.Str("health"));
        Assert.Equal(adapter, (await Radar.SourceAsync(admin, dto.Id())).Str("adapter"));

        // Not left active: the test only needs the creation response.
        body["isActive"] = false;
        await Radar.UpdateSourceAsync(admin, dto.Id(), body);
    }

    [Theory, Trait("Req", "REQ-010")]
    [InlineData(1)]
    [InlineData(1440)]
    public async Task Fixture_poll_interval_boundaries_are_accepted(int poll)
    {
        var admin = await Radar.AdminAsync(app);
        var dto = await Radar.CreateSourceAsync(admin, Radar.FixtureSource(Radar.Payload(), pollIntervalMinutes: poll));
        Assert.Equal(poll, dto.Int("pollIntervalMinutes"));
    }

    public static TheoryData<string, string[]> InvalidSources() => new()
    {
        { """{"name":"","type":"fixture","fixturePayload":"{\"items\":[]}","pollIntervalMinutes":60,"isActive":false,"isUnreliable":false}""", ["name"] },
        { """{"type":"fixture","fixturePayload":"{\"items\":[]}","pollIntervalMinutes":60,"isActive":false,"isUnreliable":false}""", ["name"] },
        { """{"name":"X","type":"ical","baseUrl":"https://example.com/","pollIntervalMinutes":60,"isActive":false,"isUnreliable":false}""", ["type"] },
        { """{"name":"X","type":"html","baseUrl":"https://example.com/events","pollIntervalMinutes":60,"isActive":false,"isUnreliable":false}""", ["adapter"] },
        { """{"name":"X","type":"html","baseUrl":"https://example.com/events","adapter":"facebookEvents","pollIntervalMinutes":60,"isActive":false,"isUnreliable":false}""", ["adapter"] },
        { """{"name":"X","type":"html","baseUrl":"/events","adapter":"kapodalim","pollIntervalMinutes":60,"isActive":false,"isUnreliable":false}""", ["baseUrl"] },
        { """{"name":"X","type":"html","baseUrl":"https://example.com/events","adapter":"prishtinaEvents","pollIntervalMinutes":14,"isActive":false,"isUnreliable":false}""", ["pollIntervalMinutes"] },
        { """{"name":"X","type":"fixture","fixturePayload":"{\"items\":[]}","pollIntervalMinutes":0,"isActive":false,"isUnreliable":false}""", ["pollIntervalMinutes"] },
        { """{"name":"X","type":"fixture","fixturePayload":"{\"items\":[]}","pollIntervalMinutes":1441,"isActive":false,"isUnreliable":false}""", ["pollIntervalMinutes"] },
        { """{"name":"X","type":"jsonFeed","baseUrl":"https://example.com/feed.json","pollIntervalMinutes":14,"isActive":false,"isUnreliable":false}""", ["pollIntervalMinutes"] },
        { """{"name":"X","type":"rss","baseUrl":"https://example.com/rss","pollIntervalMinutes":1441,"isActive":false,"isUnreliable":false}""", ["pollIntervalMinutes"] },
        { """{"name":"X","type":"jsonFeed","pollIntervalMinutes":60,"isActive":false,"isUnreliable":false}""", ["baseUrl"] },
        { """{"name":"X","type":"rss","baseUrl":"/relative/feed","pollIntervalMinutes":60,"isActive":false,"isUnreliable":false}""", ["baseUrl"] },
        { """{"name":"X","type":"jsonFeed","baseUrl":"ftp://example.com/feed.json","pollIntervalMinutes":60,"isActive":false,"isUnreliable":false}""", ["baseUrl"] },
        { """{"name":"X","type":"fixture","pollIntervalMinutes":60,"isActive":false,"isUnreliable":false}""", ["fixturePayload"] },
        { """{"name":"","type":"jsonFeed","baseUrl":"not a url","pollIntervalMinutes":5,"isActive":false,"isUnreliable":false}""", ["name", "baseUrl", "pollIntervalMinutes"] },
    };

    [Theory, Trait("Req", "REQ-011")]
    [MemberData(nameof(InvalidSources))]
    public async Task Invalid_source_on_create_gets_400_naming_each_field(string json, string[] fields)
    {
        var admin = await Radar.AdminAsync(app);
        var before = (await admin.Get("/api/admin/sources").Json()).Array().Count;

        await admin.Post("/api/admin/sources", JsonNode.Parse(json)).ExpectValidation(fields);

        Assert.Equal(before, (await admin.Get("/api/admin/sources").Json()).Array().Count);
    }

    [Theory, Trait("Req", "REQ-011")]
    [MemberData(nameof(InvalidSources))]
    public async Task Invalid_source_on_update_gets_400_and_the_source_is_unchanged(string json, string[] fields)
    {
        var admin = await Radar.AdminAsync(app);
        var payload = Radar.Payload();
        var source = await Radar.CreateSourceAsync(admin, Radar.FixtureSource(payload, pollIntervalMinutes: 60));

        await admin.Put($"/api/admin/sources/{source.Id()}", JsonNode.Parse(json)).ExpectValidation(fields);

        var after = await Radar.SourceAsync(admin, source.Id());
        Assert.Equal(source.Str("name"), after.Str("name"));
        Assert.Equal("fixture", after.Str("type"));
        Assert.Equal(60, after.Int("pollIntervalMinutes"));
        Assert.Equal(payload, after.Str("fixturePayload"));
    }

    [Fact, Trait("Req", "REQ-011")]
    public async Task Url_source_poll_interval_lower_boundary_is_15_minutes()
    {
        var admin = await Radar.AdminAsync(app);
        var ok = await Radar.CreateSourceAsync(admin, Radar.UrlSource("jsonFeed", Unreachable, pollIntervalMinutes: 15));
        Assert.Equal(15, ok.Int("pollIntervalMinutes"));
        await admin.Post("/api/admin/sources", Radar.UrlSource("rss", Unreachable, pollIntervalMinutes: 14))
            .ExpectValidation("pollIntervalMinutes");
    }

    [Fact, Trait("Req", "REQ-011")]
    public async Task Updating_an_unknown_source_gets_404()
    {
        var admin = await Radar.AdminAsync(app);
        await admin.Put($"/api/admin/sources/{Guid.NewGuid()}", Radar.FixtureSource(Radar.Payload())).Expect(HttpStatusCode.NotFound);
        await admin.Get($"/api/admin/sources/{Guid.NewGuid()}").Expect(HttpStatusCode.NotFound);
    }

    [Fact, Trait("Req", "REQ-013")]
    public async Task Inactive_source_reports_health_disabled()
    {
        var admin = await Radar.AdminAsync(app);
        var created = await Radar.CreateSourceAsync(admin, Radar.FixtureSource(Radar.Payload(), isActive: false));
        Assert.Equal("disabled", created.Str("health"));

        Assert.Equal("disabled", (await Radar.SourceAsync(admin, created.Id())).Str("health"));
        var listed = (await admin.Get("/api/admin/sources").Json()).Array().Single(s => s.Id() == created.Id());
        Assert.Equal("disabled", listed.Str("health"));
    }

    [Fact, Trait("Req", "REQ-013")]
    public async Task Deactivating_a_healthy_source_reports_disabled()
    {
        var admin = await Radar.AdminAsync(app);
        var body = Radar.FixtureSource(Radar.Payload(Radar.EventItem(Radar.Title(Radar.Token()))));
        var source = await Radar.CreateSourceAsync(admin, body);
        await Radar.FetchAsync(admin, source.Id());

        body["isActive"] = true;
        Assert.Equal("healthy", (await Radar.UpdateSourceAsync(admin, source.Id(), body)).Str("health"));
        body["isActive"] = false;
        Assert.Equal("disabled", (await Radar.UpdateSourceAsync(admin, source.Id(), body)).Str("health"));
        Assert.Equal("disabled", (await Radar.SourceAsync(admin, source.Id())).Str("health"));
    }

    [Fact, Trait("Req", "REQ-013")]
    public async Task Inactive_source_is_never_fetched_by_the_scheduler()
    {
        var admin = await Radar.AdminAsync(app);
        var source = await Radar.CreateSourceAsync(admin,
            Radar.FixtureSource(Radar.Payload(Radar.EventItem(Radar.Title(Radar.Token()))), isActive: false, pollIntervalMinutes: 1));

        await Task.Delay(TimeSpan.FromSeconds(6)); // three scheduler ticks in Testing

        var after = await Radar.SourceAsync(admin, source.Id());
        Assert.True(after.IsNull("lastFetchedAt"));
        Assert.Empty(after.GetProperty("recentRuns").Array());
        Assert.Equal("disabled", after.Str("health"));
        Assert.Equal(0, (await Radar.CandidatePageAsync(admin, source.Id())).Int("totalCount"));
    }

    [Fact, Trait("Req", "REQ-073")]
    public async Task Source_marked_unreliable_shows_isUnreliable_in_the_list()
    {
        var admin = await Radar.AdminAsync(app);
        var body = Radar.FixtureSource(Radar.Payload());
        var source = await Radar.CreateSourceAsync(admin, body);
        Assert.False((await admin.Get("/api/admin/sources").Json()).Array().Single(s => s.Id() == source.Id()).Bool("isUnreliable"));

        body["isUnreliable"] = true;
        var updated = await Radar.UpdateSourceAsync(admin, source.Id(), body);
        Assert.True(updated.Bool("isUnreliable"));

        var listed = (await admin.Get("/api/admin/sources").Json()).Array().Single(s => s.Id() == source.Id());
        Assert.True(listed.Bool("isUnreliable"));
    }
}
