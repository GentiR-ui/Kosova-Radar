using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace App.AcceptanceTests;

[Collection(AcceptanceCollection.Name)]
public class FetchTests(AppFactory app)
{
    private static readonly TimeSpan SchedulerWait = TimeSpan.FromSeconds(15);

    private static async Task<JsonElement> WaitForScheduledRunAsync(HttpClient admin, Guid sourceId) =>
        await Radar.PollAsync(
            () => Radar.SourceAsync(admin, sourceId),
            s => s.GetProperty("recentRuns").Array() is { Count: > 0 } runs
                 && runs[0].Str("trigger") == "schedule" && runs[0].Str("status") != "running",
            SchedulerWait, $"a finished scheduled run of source {sourceId}");

    // ---------- REQ-012 scheduling ----------

    [Fact, Trait("Req", "REQ-012")]
    public async Task Active_source_that_was_never_fetched_is_fetched_automatically()
    {
        var admin = await Radar.AdminAsync(app);
        var title = Radar.Title(Radar.Token());
        var source = await Radar.CreateSourceAsync(admin,
            Radar.FixtureSource(Radar.Payload(Radar.EventItem(title)), isActive: true, pollIntervalMinutes: 1440));

        var detail = await WaitForScheduledRunAsync(admin, source.Id());

        var run = detail.GetProperty("recentRuns").Array()[0];
        Assert.Equal("succeeded", run.Str("status"));
        Assert.Equal(1, run.Int("created"));
        Assert.False(detail.IsNull("lastFetchedAt"));
        var candidates = (await Radar.CandidatePageAsync(admin, source.Id())).Items();
        Assert.Equal(title, Assert.Single(candidates).Str("title"));

        // Not fetched again before its poll interval has passed.
        await Task.Delay(TimeSpan.FromSeconds(5));
        var later = await Radar.SourceAsync(admin, source.Id());
        Assert.Single(later.GetProperty("recentRuns").Array(), r => r.Str("trigger") == "schedule");
    }

    [Fact, Trait("Req", "REQ-012")]
    public async Task Active_source_whose_last_fetch_is_older_than_its_interval_is_fetched_again()
    {
        var admin = await Radar.AdminAsync(app);
        var payload = Radar.Payload(Radar.EventItem(Radar.Title(Radar.Token())));

        // Due: last fetched 2 h ago with a 60-minute interval.
        var dueBody = Radar.FixtureSource(payload, pollIntervalMinutes: 60);
        var due = await Radar.CreateSourceAsync(admin, dueBody);
        await Radar.FetchAsync(admin, due.Id(), Clock.Now.AddHours(-2));

        // Not due: last fetched 10 min ago with a 60-minute interval.
        var notDueBody = Radar.FixtureSource(Radar.Payload(Radar.EventItem(Radar.Title(Radar.Token()))), pollIntervalMinutes: 60);
        var notDue = await Radar.CreateSourceAsync(admin, notDueBody);
        await Radar.FetchAsync(admin, notDue.Id(), Clock.Now.AddMinutes(-10));

        dueBody["isActive"] = true;
        notDueBody["isActive"] = true;
        await Radar.UpdateSourceAsync(admin, due.Id(), dueBody);
        await Radar.UpdateSourceAsync(admin, notDue.Id(), notDueBody);

        var detail = await WaitForScheduledRunAsync(admin, due.Id());
        Assert.Equal("succeeded", detail.GetProperty("recentRuns").Array()[0].Str("status"));

        var notDueDetail = await Radar.SourceAsync(admin, notDue.Id());
        Assert.DoesNotContain(notDueDetail.GetProperty("recentRuns").Array(), r => r.Str("trigger") == "schedule");

        dueBody["isActive"] = false;
        notDueBody["isActive"] = false;
        await Radar.UpdateSourceAsync(admin, due.Id(), dueBody);
        await Radar.UpdateSourceAsync(admin, notDue.Id(), notDueBody);
    }

    // ---------- REQ-014 manual fetch ----------

    [Fact, Trait("Req", "REQ-014")]
    public async Task Fetch_now_processes_an_inactive_source_and_returns_the_run_summary()
    {
        var admin = await Radar.AdminAsync(app);
        var group = Radar.Token();
        var source = await Radar.CreateSourceAsync(admin, Radar.FixtureSource(Radar.Payload(
            Radar.EventItem(Radar.Title(group)),
            Radar.EventItem(Radar.Title(group, "Teatër")),
            Radar.DealItem(Radar.Title(group, "Ofertë")))));

        var run = await Radar.FetchAsync(admin, source.Id());

        Assert.True(Guid.TryParse(run.Str("id"), out _));
        Assert.Equal(source.Id(), run.Id("sourceId"));
        Assert.Equal("manual", run.Str("trigger"));
        Assert.Equal("succeeded", run.Str("status"));
        Assert.False(run.IsNull("startedAt"));
        Assert.False(run.IsNull("finishedAt"));
        Assert.Equal(3, run.Int("received"));
        Assert.Equal(3, run.Int("created"));
        Assert.Equal(0, run.Int("updated"));
        Assert.Equal(0, run.Int("unchanged"));
        Assert.Equal(0, run.Int("failed"));
        Assert.True(run.IsNull("errorCode"));

        Assert.Equal(3, (await Radar.CandidatePageAsync(admin, source.Id())).Int("totalCount"));
        var detail = await Radar.SourceAsync(admin, source.Id());
        Assert.Equal(run.Id(), detail.GetProperty("recentRuns").Array()[0].Id());
        Assert.False(detail.IsNull("lastFetchedAt"));
    }

    [Fact, Trait("Req", "REQ-014")]
    public async Task Fetch_now_works_for_an_active_source_and_ignores_the_poll_interval()
    {
        var admin = await Radar.AdminAsync(app);
        var body = Radar.FixtureSource(Radar.Payload(Radar.EventItem(Radar.Title(Radar.Token()))));
        var source = await Radar.CreateSourceAsync(admin, body);
        var first = await Radar.FetchAsync(admin, source.Id());
        body["isActive"] = true;
        await Radar.UpdateSourceAsync(admin, source.Id(), body);

        var second = await Radar.FetchAsync(admin, source.Id());

        Assert.NotEqual(first.Id(), second.Id());
        Assert.Equal("manual", second.Str("trigger"));
        Assert.Equal("succeeded", second.Str("status"));
        Assert.Equal(1, second.Int("received"));

        body["isActive"] = false;
        await Radar.UpdateSourceAsync(admin, source.Id(), body);
    }

    [Fact, Trait("Req", "REQ-014")]
    public async Task Fetch_now_of_an_unknown_source_gets_404()
    {
        var admin = await Radar.AdminAsync(app);
        await admin.Post($"/api/admin/sources/{Guid.NewGuid()}/fetch").Expect(HttpStatusCode.NotFound);
    }

    // ---------- REQ-015 unchanged ----------

    [Fact, Trait("Req", "REQ-015")]
    public async Task Refetching_unchanged_items_creates_no_new_candidates()
    {
        var admin = await Radar.AdminAsync(app);
        var group = Radar.Token();
        var ing = await Radar.IngestAsync(admin, [Radar.EventItem(Radar.Title(group)), Radar.DealItem(Radar.Title(group, "Ofertë"))]);
        var idsBefore = ing.Candidates.Select(c => c.Id()).OrderBy(x => x).ToList();

        var again = await Radar.FetchAsync(admin, ing.SourceId);

        Assert.Equal("succeeded", again.Str("status"));
        Assert.Equal(2, again.Int("received"));
        Assert.Equal(0, again.Int("created"));
        Assert.Equal(0, again.Int("updated"));
        Assert.Equal(2, again.Int("unchanged"));
        var after = (await Radar.CandidatePageAsync(admin, ing.SourceId)).Items().Select(c => c.Id()).OrderBy(x => x).ToList();
        Assert.Equal(idsBefore, after);
    }

    // ---------- REQ-016 failures ----------

    [Theory, Trait("Req", "REQ-016")]
    [InlineData("not json")]
    [InlineData("{\"foo\":[]}")]
    [InlineData("[1,2,3]")]
    public async Task Malformed_payload_fails_the_run_with_MalformedPayload(string payload)
    {
        var admin = await Radar.AdminAsync(app);
        var source = await Radar.CreateSourceAsync(admin, Radar.FixtureSource(payload));

        var run = await Radar.FetchAsync(admin, source.Id());

        Assert.Equal("failed", run.Str("status"));
        Assert.Equal("MalformedPayload", run.Str("errorCode"));
        Assert.False(run.IsNull("finishedAt"));
        var detail = await Radar.SourceAsync(admin, source.Id());
        Assert.False(detail.IsNull("lastErrorAt"));
        Assert.False(string.IsNullOrWhiteSpace(detail.Str("lastError")));
        Assert.Equal(1, detail.Int("consecutiveFailures"));
        Assert.True(detail.IsNull("lastSuccessAt"));
        Assert.Equal("failed", detail.GetProperty("recentRuns").Array()[0].Str("status"));
    }

    [Fact, Trait("Req", "REQ-016")]
    public async Task Unreachable_host_fails_the_run_with_Unreachable()
    {
        var admin = await Radar.AdminAsync(app);
        var source = await Radar.CreateSourceAsync(admin, Radar.UrlSource("jsonFeed", "https://kosovaradar-test.invalid/feed.json"));

        var run = await Radar.FetchAsync(admin, source.Id());

        Assert.Equal("failed", run.Str("status"));
        Assert.Equal("Unreachable", run.Str("errorCode"));
        var detail = await Radar.SourceAsync(admin, source.Id());
        Assert.Equal(1, detail.Int("consecutiveFailures"));
        Assert.False(detail.IsNull("lastErrorAt"));
    }

    [Fact, Trait("Req", "REQ-016")]
    public async Task Active_source_with_one_or_two_failures_is_degraded_and_from_the_third_failing()
    {
        var admin = await Radar.AdminAsync(app);
        var body = Radar.FixtureSource("not json", isActive: false, pollIntervalMinutes: 1440);
        var source = await Radar.CreateSourceAsync(admin, body);
        var id = source.Id();

        async Task<JsonElement> HealthAfterActivationAsync()
        {
            body["isActive"] = true;
            var dto = await Radar.UpdateSourceAsync(admin, id, body);
            var read = await Radar.SourceAsync(admin, id);
            body["isActive"] = false;
            await Radar.UpdateSourceAsync(admin, id, body);
            Assert.Equal(dto.Str("health"), read.Str("health"));
            return read;
        }

        await Radar.FetchAsync(admin, id);
        var one = await HealthAfterActivationAsync();
        Assert.Equal("degraded", one.Str("health"));
        Assert.Equal(1, one.Int("consecutiveFailures"));

        await Radar.FetchAsync(admin, id);
        var two = await HealthAfterActivationAsync();
        Assert.Equal("degraded", two.Str("health"));
        Assert.Equal(2, two.Int("consecutiveFailures"));

        await Radar.FetchAsync(admin, id);
        var three = await HealthAfterActivationAsync();
        Assert.Equal("failing", three.Str("health"));
        Assert.Equal(3, three.Int("consecutiveFailures"));

        var listed = (await admin.Get("/api/admin/sources").Json()).Array().Single(s => s.Id() == id);
        Assert.Equal(3, listed.Int("consecutiveFailures"));
        Assert.False(listed.IsNull("lastErrorAt"));
    }

    // ---------- REQ-017 fault isolation ----------

    [Fact, Trait("Req", "REQ-017")]
    public async Task One_failing_source_does_not_stop_other_due_sources_or_the_public_api()
    {
        var admin = await Radar.AdminAsync(app);
        var title = Radar.Title(Radar.Token());
        var badBody = Radar.FixtureSource("not json", isActive: true, pollIntervalMinutes: 1440);
        var goodBody = Radar.FixtureSource(Radar.Payload(Radar.EventItem(title)), isActive: true, pollIntervalMinutes: 1440);
        var bad = await Radar.CreateSourceAsync(admin, badBody);
        var good = await Radar.CreateSourceAsync(admin, goodBody);

        var badDetail = await WaitForScheduledRunAsync(admin, bad.Id());
        var goodDetail = await WaitForScheduledRunAsync(admin, good.Id());

        var badRun = badDetail.GetProperty("recentRuns").Array()[0];
        Assert.Equal("failed", badRun.Str("status"));
        Assert.Equal("MalformedPayload", badRun.Str("errorCode"));
        var goodRun = goodDetail.GetProperty("recentRuns").Array()[0];
        Assert.Equal("succeeded", goodRun.Str("status"));
        Assert.Equal(1, goodRun.Int("created"));
        Assert.Equal(title, Assert.Single((await Radar.CandidatePageAsync(admin, good.Id())).Items()).Str("title"));

        await app.CreateClient().Get("/api/events").Expect(HttpStatusCode.OK);
        await app.CreateClient().Get("/health").Expect(HttpStatusCode.OK);

        badBody["isActive"] = false;
        await Radar.UpdateSourceAsync(admin, bad.Id(), badBody);
    }

    [Fact, Trait("Req", "REQ-017")]
    public async Task Public_api_keeps_serving_while_a_failing_source_is_fetched()
    {
        var admin = await Radar.AdminAsync(app);
        var source = await Radar.CreateSourceAsync(admin, Radar.UrlSource("jsonFeed", "https://kosovaradar-test.invalid/feed.json"));
        var fetch = Radar.FetchAsync(admin, source.Id());

        await app.CreateClient().Get("/api/events").Expect(HttpStatusCode.OK);
        await app.CreateClient().Get("/api/deals").Expect(HttpStatusCode.OK);

        Assert.Equal("failed", (await fetch).Str("status"));
        await app.CreateClient().Get("/api/cities").Expect(HttpStatusCode.OK);
    }

    // ---------- REQ-018 per-item failures ----------

    [Fact, Trait("Req", "REQ-018")]
    public async Task Unparsable_items_are_counted_failed_and_the_others_are_processed()
    {
        var admin = await Radar.AdminAsync(app);
        var group = Radar.Token();
        var good1 = Radar.EventItem(Radar.Title(group));
        var good2 = Radar.DealItem(Radar.Title(group, "Ofertë"));
        var unknownKind = Radar.EventItem(Radar.Title(group, "Panair"));
        unknownKind["kind"] = "concert";
        var missingKind = Radar.EventItem(Radar.Title(group, "Festival"));
        missingKind.Remove("kind");
        var badDate = Radar.EventItem(Radar.Title(group, "Ekspozitë"));
        badDate["startsAt"] = "next friday";
        var wrongType = Radar.EventItem(Radar.Title(group, "Ndeshje"));
        wrongType["title"] = 42;
        var noIdentity = Radar.EventItem(Radar.Title(group, "Ligjëratë"));
        noIdentity.Remove("externalId");
        noIdentity.Remove("url");

        var payload = "{\"items\":[" + string.Join(",",
            good1.Raw(), unknownKind.Raw(), "\"just a string\"", missingKind.Raw(), badDate.Raw(), wrongType.Raw(),
            noIdentity.Raw(), good2.Raw()) + "]}";
        var source = await Radar.CreateSourceAsync(admin, Radar.FixtureSource(payload));

        var run = await Radar.FetchAsync(admin, source.Id());

        Assert.Equal("succeeded", run.Str("status"));
        Assert.Equal(8, run.Int("received"));
        Assert.Equal(2, run.Int("created"));
        Assert.Equal(6, run.Int("failed"));
        var titles = (await Radar.CandidatePageAsync(admin, source.Id())).Items().Select(c => c.Str("title")).ToHashSet();
        Assert.Equal(new HashSet<string?> { good1["title"]!.GetValue<string>(), good2["title"]!.GetValue<string>() }, titles);
    }

    // ---------- REQ-019 success bookkeeping ----------

    [Fact, Trait("Req", "REQ-019")]
    public async Task Successful_fetch_after_failures_resets_the_count_and_reports_healthy()
    {
        var admin = await Radar.AdminAsync(app);
        var body = Radar.FixtureSource("not json", isActive: false, pollIntervalMinutes: 1440);
        var source = await Radar.CreateSourceAsync(admin, body);
        await Radar.FetchAsync(admin, source.Id());
        await Radar.FetchAsync(admin, source.Id());
        Assert.Equal(2, (await Radar.SourceAsync(admin, source.Id())).Int("consecutiveFailures"));

        body["fixturePayload"] = Radar.Payload(Radar.EventItem(Radar.Title(Radar.Token())));
        await Radar.UpdateSourceAsync(admin, source.Id(), body);
        var run = await Radar.FetchAsync(admin, source.Id());
        Assert.Equal("succeeded", run.Str("status"));

        body["isActive"] = true;
        await Radar.UpdateSourceAsync(admin, source.Id(), body);
        var detail = await Radar.SourceAsync(admin, source.Id());
        Assert.Equal("healthy", detail.Str("health"));
        Assert.Equal(0, detail.Int("consecutiveFailures"));
        Assert.False(detail.IsNull("lastSuccessAt"));
        Assert.True(detail.Time("lastSuccessAt") >= detail.Time("lastErrorAt"));

        body["isActive"] = false;
        await Radar.UpdateSourceAsync(admin, source.Id(), body);
    }

    [Fact, Trait("Req", "REQ-019")]
    public async Task Successful_fetch_sets_lastSuccessAt_to_the_fetch_time()
    {
        var admin = await Radar.AdminAsync(app);
        var source = await Radar.CreateSourceAsync(admin, Radar.FixtureSource(Radar.Payload(Radar.EventItem(Radar.Title(Radar.Token())))));
        var at = Clock.Now.AddHours(-1);

        await Radar.FetchAsync(admin, source.Id(), at);

        var detail = await Radar.SourceAsync(admin, source.Id());
        Assert.Equal(at, detail.Time("lastSuccessAt"));
        Assert.Equal(at, detail.Time("lastFetchedAt"));
        Assert.Equal(0, detail.Int("consecutiveFailures"));
    }

    // ---------- REQ-020 SSRF guard ----------

    [Theory, Trait("Req", "REQ-020")]
    [InlineData("jsonFeed", "http://127.0.0.1/")]
    [InlineData("jsonFeed", "http://localhost/")]
    [InlineData("jsonFeed", "http://169.254.169.254/")]
    [InlineData("jsonFeed", "http://[::1]/")]
    [InlineData("jsonFeed", "http://10.0.0.1/")]
    [InlineData("rss", "http://127.0.0.1/rss.xml")]
    [InlineData("rss", "http://10.0.0.1/rss.xml")]
    public async Task Source_url_resolving_to_a_non_public_address_fails_with_BlockedAddress(string type, string url)
    {
        var admin = await Radar.AdminAsync(app);
        var source = await Radar.CreateSourceAsync(admin, Radar.UrlSource(type, url));

        var run = await Radar.FetchAsync(admin, source.Id());

        Assert.Equal("failed", run.Str("status"));
        Assert.Equal("BlockedAddress", run.Str("errorCode"));
        Assert.Equal(0, run.Int("created"));
        Assert.Equal(1, (await Radar.SourceAsync(admin, source.Id())).Int("consecutiveFailures"));
    }

    // ---------- REQ-021 synthetic ----------

    [Fact, Trait("Req", "REQ-021")]
    public async Task Fixture_candidates_and_published_items_are_synthetic()
    {
        var admin = await Radar.AdminAsync(app);
        var group = Radar.Token();
        var ing = await Radar.IngestAsync(admin, [Radar.EventItem(Radar.Title(group)), Radar.DealItem(Radar.Title(group, "Ofertë"))]);

        foreach (var summary in (await Radar.CandidatePageAsync(admin, ing.SourceId)).Items())
            Assert.True(summary.Bool("isSynthetic"));
        foreach (var candidate in ing.Candidates)
            Assert.True(candidate.Bool("isSynthetic"));

        var anon = app.CreateClient();
        foreach (var candidate in ing.Candidates)
        {
            var itemId = await Radar.ApproveAsync(admin, candidate.Id());
            var path = candidate.Str("kind") == "event" ? "events" : "deals";
            Assert.True((await anon.Get($"/api/{path}/{itemId}").Json()).Bool("isSynthetic"));
            var listed = (await Radar.PublicListAsync(anon, $"/api/{path}?search={group}")).Single(i => i.Id() == itemId);
            Assert.True(listed.Bool("isSynthetic"));
        }
    }
}
