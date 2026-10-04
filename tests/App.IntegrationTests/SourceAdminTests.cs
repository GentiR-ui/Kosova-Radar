using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace App.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public class SourceAdminTests(AppFactory app)
{
    private static async Task<HttpClient> LoginAdminAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@kosovaradar.local", password = "Radar-admin-dev-2026" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return client;
    }

    private static object Body(string name, string payload = "{\"items\":[]}", bool isUnreliable = false, string type = "fixture") => new
    {
        name,
        type,
        baseUrl = (string?)null,
        fixturePayload = payload,
        defaultKind = "event",
        pollIntervalMinutes = 60,
        isActive = false,
        isUnreliable,
    };

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage res, HttpStatusCode expected)
    {
        var text = await res.Content.ReadAsStringAsync();
        Assert.True(res.StatusCode == expected, $"Expected {(int)expected} but got {(int)res.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    [Fact]
    public async Task Create_and_update_are_audited_with_the_payload_length_but_not_the_payload()
    {
        var admin = await LoginAdminAsync(app);
        var name = $"Burim {Guid.NewGuid():N}";
        var secretPayload = "{\"items\":[],\"note\":\"PAYLOAD-" + Guid.NewGuid().ToString("N") + "\"}";
        var created = await JsonAsync(await admin.PostAsJsonAsync("/api/admin/sources", Body(name, secretPayload)), HttpStatusCode.Created);
        var id = created.GetProperty("id").GetGuid();

        await JsonAsync(await admin.PutAsJsonAsync($"/api/admin/sources/{id}", Body(name + " 2", secretPayload, isUnreliable: true)), HttpStatusCode.OK);

        var audit = await JsonAsync(await admin.GetAsync($"/api/admin/audit?entityId={id}"), HttpStatusCode.OK);
        var entries = audit.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["source.update", "source.create"], entries.Select(e => e.GetProperty("action").GetString()));
        var update = entries[0].GetProperty("changes");
        Assert.Equal(name, update.GetProperty("before").GetProperty("name").GetString());
        Assert.False(update.GetProperty("before").GetProperty("isUnreliable").GetBoolean());
        Assert.Equal(name + " 2", update.GetProperty("after").GetProperty("name").GetString());
        Assert.True(update.GetProperty("after").GetProperty("isUnreliable").GetBoolean());
        Assert.Equal(secretPayload.Length, update.GetProperty("after").GetProperty("fixturePayloadLength").GetInt32());
        Assert.DoesNotContain("PAYLOAD-", audit.GetRawText());
    }

    [Fact]
    public async Task Failed_validation_writes_no_audit_entry_and_changes_nothing()
    {
        var admin = await LoginAdminAsync(app);
        var created = await JsonAsync(await admin.PostAsJsonAsync("/api/admin/sources", Body($"Burim {Guid.NewGuid():N}")), HttpStatusCode.Created);
        var id = created.GetProperty("id").GetGuid();

        var bad = await admin.PutAsJsonAsync($"/api/admin/sources/{id}", Body("", "", type: "html"));

        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var audit = await JsonAsync(await admin.GetAsync($"/api/admin/audit?entityId={id}"), HttpStatusCode.OK);
        Assert.Single(audit.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Changing_the_type_replaces_the_location_and_leaves_run_bookkeeping_alone()
    {
        var admin = await LoginAdminAsync(app);
        var created = await JsonAsync(await admin.PostAsJsonAsync("/api/admin/sources", Body($"Burim {Guid.NewGuid():N}")), HttpStatusCode.Created);
        var id = created.GetProperty("id").GetGuid();

        var url = new
        {
            name = "Feed",
            type = "jsonFeed",
            baseUrl = "https://example.com/feed.json",
            fixturePayload = "ignored",
            defaultKind = "deal",
            pollIntervalMinutes = 30,
            isActive = false,
            isUnreliable = false,
        };
        await JsonAsync(await admin.PutAsJsonAsync($"/api/admin/sources/{id}", url), HttpStatusCode.OK);

        var detail = await JsonAsync(await admin.GetAsync($"/api/admin/sources/{id}"), HttpStatusCode.OK);
        Assert.Equal("jsonFeed", detail.GetProperty("type").GetString());
        Assert.Equal("https://example.com/feed.json", detail.GetProperty("baseUrl").GetString());
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("fixturePayload").ValueKind);
        Assert.Equal("deal", detail.GetProperty("defaultKind").GetString());
        Assert.Equal(0, detail.GetProperty("consecutiveFailures").GetInt32());
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("lastFetchedAt").ValueKind);
    }

    [Fact]
    public async Task Fixture_sources_are_refused_when_the_environment_does_not_allow_them()
    {
        await using var strict = app.WithWebHostBuilder(b => b.UseSetting("Ingestion:AllowFixtureSources", "false"));
        var admin = await LoginAdminAsync(strict);

        var res = await admin.PostAsJsonAsync("/api/admin/sources", Body($"Burim {Guid.NewGuid():N}"));

        var problem = await JsonAsync(res, HttpStatusCode.BadRequest);
        Assert.True(problem.GetProperty("errors").TryGetProperty("type", out _));
    }

    [Fact]
    public async Task List_shows_health_and_unreliable_flags_without_the_fixture_payload_and_unknown_ids_are_404()
    {
        var admin = await LoginAdminAsync(app);
        var name = $"Burim {Guid.NewGuid():N}";
        var active = new { name, type = "jsonFeed", baseUrl = "https://example.com/feed.json", fixturePayload = (string?)null, defaultKind = "event", pollIntervalMinutes = 60, isActive = true, isUnreliable = true };
        var created = await JsonAsync(await admin.PostAsJsonAsync("/api/admin/sources", active), HttpStatusCode.Created);
        var inactive = await JsonAsync(await admin.PostAsJsonAsync("/api/admin/sources", Body($"Burim {Guid.NewGuid():N}", "PAYLOAD-IN-LIST")), HttpStatusCode.Created);

        var list = await JsonAsync(await admin.GetAsync("/api/admin/sources"), HttpStatusCode.OK);

        var items = list.EnumerateArray().ToList();
        var listed = items.Single(i => i.GetProperty("id").GetGuid() == created.GetProperty("id").GetGuid());
        Assert.Equal("never", listed.GetProperty("health").GetString());
        Assert.True(listed.GetProperty("isUnreliable").GetBoolean());
        Assert.Equal("disabled", items.Single(i => i.GetProperty("id").GetGuid() == inactive.GetProperty("id").GetGuid()).GetProperty("health").GetString());
        Assert.DoesNotContain("PAYLOAD-IN-LIST", list.GetRawText());
        Assert.Equal(items.Select(i => i.GetProperty("name").GetString()).Order(StringComparer.OrdinalIgnoreCase), items.Select(i => i.GetProperty("name").GetString()));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/admin/sources/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync($"/api/admin/sources/{Guid.NewGuid()}", Body("X"))).StatusCode);
    }

    [Fact]
    public async Task Anonymous_callers_get_401_on_every_source_endpoint()
    {
        var anonymous = app.CreateClient();
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/admin/sources")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/admin/sources/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/admin/sources", Body("X"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync($"/api/admin/sources/{id}", Body("X"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync($"/api/admin/sources/{id}/fetch", null)).StatusCode);
    }
}
