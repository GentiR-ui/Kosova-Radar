using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Application.Abstractions;
using App.Application.Audit;
using App.Application.Ingestion;
using App.Domain.Content;
using App.Domain.Sources;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace App.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public class IngestionRunTests(AppFactory app)
{
    private static string Token() => Guid.NewGuid().ToString("N")[..8];

    private static string Item(string id, string title, string kind = "event", string? extra = null) =>
        $"{{\"externalId\":\"{id}\",\"kind\":\"{kind}\",\"title\":\"{title}\",\"url\":\"https://example.test/{id}\"{extra}}}";

    private static string Payload(params string[] items) => "{\"items\":[" + string.Join(",", items) + "]}";

    private async Task<HttpClient> LoginAdminAsync()
    {
        var client = app.CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@kosovaradar.local", password = "Radar-admin-dev-2026" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return client;
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage res, HttpStatusCode expected)
    {
        var text = await res.Content.ReadAsStringAsync();
        Assert.True(res.StatusCode == expected, $"Expected {(int)expected} but got {(int)res.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private async Task<Guid> CreateSourceAsync(HttpClient admin, string payload)
    {
        var created = await JsonAsync(await admin.PostAsJsonAsync("/api/admin/sources", new
        {
            name = $"Burim {Token()}",
            type = "fixture",
            baseUrl = (string?)null,
            fixturePayload = payload,
            defaultKind = "event",
            pollIntervalMinutes = 1440,
            isActive = false,
            isUnreliable = false,
        }), HttpStatusCode.Created);
        return created.GetProperty("id").GetGuid();
    }

    /// <summary>A scheduled run only happens for an active source (REQ-013); the helper sources are created inactive.</summary>
    private async Task ActivateAsync(Guid id)
    {
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Sources.Where(s => s.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, true));
    }

    private async Task SetPayloadAsync(HttpClient admin, Guid id, string payload) =>
        await JsonAsync(await admin.PutAsJsonAsync($"/api/admin/sources/{id}", new
        {
            name = $"Burim {Token()}",
            type = "fixture",
            baseUrl = (string?)null,
            fixturePayload = payload,
            defaultKind = "event",
            pollIntervalMinutes = 1440,
            isActive = false,
            isUnreliable = false,
        }), HttpStatusCode.OK);

    private static async Task<JsonElement> FetchAsync(HttpClient admin, Guid id) =>
        await JsonAsync(await admin.PostAsync($"/api/admin/sources/{id}/fetch", null), HttpStatusCode.OK);

    private async Task<List<Candidate>> CandidatesAsync(Guid sourceId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Candidates.AsNoTracking()
            .Where(c => c.SourceId == sourceId).OrderBy(c => c.Fields.Title).ToListAsync();
    }

    [Fact]
    public async Task A_run_creates_candidates_with_resolved_slugs_synthetic_flag_and_raw_evidence()
    {
        var admin = await LoginAdminAsync();
        var t = Token();
        var raw = Item("a", $"Koncert {t}", extra: ",\"city\":\"Pristina\",\"category\":\"Muzikë & koncerte\",\"startsAt\":\"2026-10-20T20:00:00+02:00\"");
        var id = await CreateSourceAsync(admin, Payload(
            raw,
            Item("b", $"Ofertë {t}", "deal", ",\"city\":\"Atlantis\",\"category\":\"muzike\""),
            Item("c", $"Tjetër {t}")));

        var run = await FetchAsync(admin, id);

        Assert.Equal("succeeded", run.GetProperty("status").GetString());
        Assert.Equal(3, run.GetProperty("created").GetInt32());
        var candidates = await CandidatesAsync(id);
        var concert = candidates.Single(c => c.Fields.Title == $"Koncert {t}");
        Assert.Equal("prishtine", concert.Fields.CitySlug);
        Assert.Equal("Pristina", concert.Fields.CityText);
        Assert.Equal("muzike", concert.Fields.CategorySlug);
        Assert.Equal(raw, concert.Evidence);
        Assert.Equal(CandidateStatus.Pending, concert.Status);
        Assert.All(candidates, c => Assert.True(c.IsSynthetic));
        Assert.All(candidates, c => Assert.Equal(c.Fields.Url, c.SourceUrl));

        // Unknown city stays text without a slug; "muzike" is not a deal category, so no slug either.
        var deal = candidates.Single(c => c.Kind == App.Domain.Reference.ItemKind.Deal);
        Assert.Equal("Atlantis", deal.Fields.CityText);
        Assert.Null(deal.Fields.CitySlug);
        Assert.Null(deal.Fields.CategorySlug);
        Assert.Null(candidates.Single(c => c.Fields.Title == $"Tjetër {t}").Fields.StartsAt);
    }

    [Fact]
    public async Task A_changed_item_is_updated_in_place_and_an_unchanged_one_is_not_touched()
    {
        var admin = await LoginAdminAsync();
        var t = Token();
        var id = await CreateSourceAsync(admin, Payload(Item("a", $"Para {t}"), Item("b", $"Fiks {t}")));
        await FetchAsync(admin, id);
        var before = await CandidatesAsync(id);

        await SetPayloadAsync(admin, id, Payload(Item("a", $"Pas {t}"), Item("b", $"Fiks {t}")));
        var run = await FetchAsync(admin, id);

        Assert.Equal(0, run.GetProperty("created").GetInt32());
        Assert.Equal(1, run.GetProperty("updated").GetInt32());
        Assert.Equal(1, run.GetProperty("unchanged").GetInt32());
        var after = await CandidatesAsync(id);
        Assert.Equal(before.Select(c => c.Id).Order(), after.Select(c => c.Id).Order());
        var changed = after.Single(c => c.Fields.Title == $"Pas {t}");
        var old = before.Single(c => c.Id == changed.Id);
        Assert.NotEqual(old.ContentHash, changed.ContentHash);
        Assert.Equal(old.Fingerprint, changed.Fingerprint);
        var same = after.Single(c => c.Fields.Title == $"Fiks {t}");
        Assert.Equal(before.Single(c => c.Id == same.Id).UpdatedAt, same.UpdatedAt);
    }

    [Fact]
    public async Task An_auto_rejected_candidate_is_revalidated_when_the_source_fixes_the_item()
    {
        var admin = await LoginAdminAsync();
        var t = Token();
        var id = await CreateSourceAsync(admin, Payload(Item("a", $"Pa titull {t}", extra: ",\"url\":\"ftp://example.test/a\"")));
        await FetchAsync(admin, id);
        var rejected = (await CandidatesAsync(id)).Single();
        Assert.Equal(CandidateStatus.Rejected, rejected.Status);
        Assert.Null(rejected.RejectedByUserId);
        Assert.Contains(rejected.Issues, i => i.Code == IssueCodes.InvalidUrl);

        await SetPayloadAsync(admin, id, Payload(Item("a", $"Pa titull {t}", extra: ",\"startsAt\":\"2999-01-01T10:00:00Z\",\"city\":\"Prishtina\"")));
        await FetchAsync(admin, id);

        var fixedUp = (await CandidatesAsync(id)).Single();
        Assert.Equal(rejected.Id, fixedUp.Id);
        Assert.Equal(CandidateStatus.Pending, fixedUp.Status);
        Assert.Equal(ValidationOutcome.Valid, fixedUp.Validation);
        Assert.Empty(fixedUp.Issues);
    }

    [Fact]
    public async Task The_same_item_twice_in_one_feed_makes_one_candidate()
    {
        var admin = await LoginAdminAsync();
        var t = Token();
        var id = await CreateSourceAsync(admin, Payload(Item("a", $"Dy herë {t}"), Item("a", $"Dy herë {t}"), Item("a", $"Ndryshe {t}")));

        var run = await FetchAsync(admin, id);

        Assert.Equal(3, run.GetProperty("received").GetInt32());
        Assert.Equal(1, run.GetProperty("created").GetInt32());
        Assert.Equal(1, run.GetProperty("unchanged").GetInt32());
        Assert.Equal(1, run.GetProperty("updated").GetInt32());
        Assert.Equal($"Ndryshe {t}", Assert.Single(await CandidatesAsync(id)).Fields.Title);
    }

    [Fact]
    public async Task Items_that_cannot_be_parsed_are_counted_and_the_rest_is_stored()
    {
        var admin = await LoginAdminAsync();
        var t = Token();
        var id = await CreateSourceAsync(admin, Payload(Item("a", $"Mirë {t}"), "\"text\"", "{\"kind\":\"event\",\"title\":\"pa identitet\"}"));

        var run = await FetchAsync(admin, id);

        Assert.Equal("succeeded", run.GetProperty("status").GetString());
        Assert.Equal(3, run.GetProperty("received").GetInt32());
        Assert.Equal(1, run.GetProperty("created").GetInt32());
        Assert.Equal(2, run.GetProperty("failed").GetInt32());
        Assert.Single(await CandidatesAsync(id));
    }

    [Fact]
    public async Task An_item_with_a_NUL_character_is_a_per_item_failure_not_a_failed_run()
    {
        var admin = await LoginAdminAsync();
        var t = Token();
        var id = await CreateSourceAsync(admin, Payload(Item("a", $"Mirë {t}"), Item("b", "x" + (char)92 + "u0000y")));

        var run = await FetchAsync(admin, id);

        Assert.Equal("succeeded", run.GetProperty("status").GetString());
        Assert.Equal(1, run.GetProperty("created").GetInt32());
        Assert.Equal(1, run.GetProperty("failed").GetInt32());
    }

    [Fact]
    public async Task A_reviewer_cannot_fetch_and_no_run_is_recorded()
    {
        var admin = await LoginAdminAsync();
        var id = await CreateSourceAsync(admin, Payload());
        var email = $"rev{Token()}@test.local";
        await JsonAsync(await admin.PostAsJsonAsync("/api/admin/users", new { email, password = "Reviewer-password-1", role = "reviewer" }), HttpStatusCode.Created);
        var reviewer = app.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await reviewer.PostAsJsonAsync("/api/auth/login", new { email, password = "Reviewer-password-1" })).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await reviewer.PostAsync($"/api/admin/sources/{id}/fetch", null)).StatusCode);

        var detail = await JsonAsync(await admin.GetAsync($"/api/admin/sources/{id}"), HttpStatusCode.OK);
        Assert.Empty(detail.GetProperty("recentRuns").EnumerateArray());
    }

    [Fact]
    public async Task A_failed_run_stores_no_candidates_and_records_the_failure_on_the_source()
    {
        var admin = await LoginAdminAsync();
        var id = await CreateSourceAsync(admin, "not json");

        var run = await FetchAsync(admin, id);

        Assert.Equal("failed", run.GetProperty("status").GetString());
        Assert.Equal("MalformedPayload", run.GetProperty("errorCode").GetString());
        Assert.Empty(await CandidatesAsync(id));
        var detail = await JsonAsync(await admin.GetAsync($"/api/admin/sources/{id}"), HttpStatusCode.OK);
        Assert.Equal(1, detail.GetProperty("consecutiveFailures").GetInt32());
        Assert.Equal(run.GetProperty("id").GetGuid(), detail.GetProperty("recentRuns")[0].GetProperty("id").GetGuid());
        Assert.DoesNotContain("not json", detail.GetProperty("lastError").GetString());
    }

    [Fact]
    public async Task A_manual_fetch_is_audited_with_the_counts_only()
    {
        var admin = await LoginAdminAsync();
        var t = Token();
        var id = await CreateSourceAsync(admin, Payload(Item("a", $"Auditim {t}")));
        var run = await FetchAsync(admin, id);

        var audit = await JsonAsync(await admin.GetAsync($"/api/admin/audit?entityId={id}"), HttpStatusCode.OK);

        var entry = audit.GetProperty("items").EnumerateArray().Single(e => e.GetProperty("action").GetString() == "source.fetch");
        var after = entry.GetProperty("changes").GetProperty("after");
        Assert.Equal(run.GetProperty("id").GetGuid(), after.GetProperty("runId").GetGuid());
        Assert.Equal(1, after.GetProperty("created").GetInt32());
        Assert.DoesNotContain(t, entry.GetRawText());
    }

    [Fact]
    public async Task Fetching_a_source_that_does_not_exist_is_404_and_a_run_in_progress_gives_409_after_the_wait()
    {
        var admin = await LoginAdminAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync($"/api/admin/sources/{Guid.NewGuid()}/fetch", null)).StatusCode);

        var id = await CreateSourceAsync(admin, Payload());
        await using var scope = app.Services.CreateAsyncScope();
        var locks = scope.ServiceProvider.GetRequiredService<SourceRunLock>();
        using var held = await locks.TryAcquireAsync(id, TimeSpan.Zero, CancellationToken.None);
        Assert.NotNull(held);

        var busy = await scope.ServiceProvider.GetRequiredService<IngestionService>()
            .RunAsync(id, JobTrigger.Manual, TimeSpan.FromMilliseconds(100), CancellationToken.None);

        Assert.Equal(RunStatus.Busy, busy.Status);
        await using var verify = app.Services.CreateAsyncScope();
        Assert.False(await verify.ServiceProvider.GetRequiredService<AppDbContext>().JobExecutions.AnyAsync(j => j.SourceId == id));
    }

    [Fact]
    public async Task A_scheduled_run_needs_no_user_and_writes_no_audit_entry()
    {
        var admin = await LoginAdminAsync();
        var id = await CreateSourceAsync(admin, Payload(Item("a", $"Planifikuar {Token()}")));
        await ActivateAsync(id);

        await using var scope = app.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IngestionService>()
            .RunAsync(id, JobTrigger.Schedule, TimeSpan.Zero, CancellationToken.None);

        Assert.Equal("schedule", result.Run!.Trigger);
        Assert.Equal(1, result.Run.Created);
        var audit = await JsonAsync(await admin.GetAsync($"/api/admin/audit?entityId={id}"), HttpStatusCode.OK);
        Assert.DoesNotContain(audit.GetProperty("items").EnumerateArray(), e => e.GetProperty("action").GetString() == "source.fetch");
    }

    [Fact]
    public async Task A_crashing_run_is_marked_failed_instead_of_staying_running_and_the_lock_is_released()
    {
        var admin = await LoginAdminAsync();
        var id = await CreateSourceAsync(admin, Payload());
        await ActivateAsync(id);
        await using var scope = app.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var locks = sp.GetRequiredService<SourceRunLock>();
        var service = new IngestionService(sp.GetRequiredService<IAppDbContext>(), sp.GetRequiredService<IClock>(), TimeProvider.System,
            sp.GetRequiredService<IAuditWriter>(), [new ThrowingAdapter()], locks, NullLogger<IngestionService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunAsync(id, JobTrigger.Schedule, TimeSpan.Zero, CancellationToken.None));

        await using var verify = app.Services.CreateAsyncScope();
        var run = await verify.ServiceProvider.GetRequiredService<AppDbContext>().JobExecutions.AsNoTracking().SingleAsync(j => j.SourceId == id);
        Assert.Equal(JobStatus.Failed, run.Status);
        Assert.Equal("Interrupted", run.ErrorCode);
        Assert.NotNull(run.FinishedAt);
        var source = await verify.ServiceProvider.GetRequiredService<AppDbContext>().Sources.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.Equal(1, source.ConsecutiveFailures); // a crash counts against the source's health
        using var again = await locks.TryAcquireAsync(id, TimeSpan.Zero, CancellationToken.None);
        Assert.NotNull(again);
    }

    [Fact]
    public async Task Runs_left_running_by_a_previous_process_are_marked_interrupted_at_startup()
    {
        var admin = await LoginAdminAsync();
        var id = await CreateSourceAsync(admin, Payload());
        Guid runId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var run = new JobExecution
            {
                JobName = JobName.SourceFetch,
                SourceId = id,
                Trigger = JobTrigger.Schedule,
                StartedAt = DateTimeOffset.UtcNow,
                Status = JobStatus.Running,
                CorrelationId = "x",
            };
            db.JobExecutions.Add(run);
            await db.SaveChangesAsync();
            runId = run.Id;
            await scope.ServiceProvider.GetRequiredService<IngestionService>().MarkInterruptedAsync(CancellationToken.None);
        }

        await using var verify = app.Services.CreateAsyncScope();
        var stored = await verify.ServiceProvider.GetRequiredService<AppDbContext>().JobExecutions.AsNoTracking().SingleAsync(j => j.Id == runId);
        Assert.Equal(JobStatus.Failed, stored.Status);
        Assert.Equal("Interrupted", stored.ErrorCode);
        Assert.NotNull(stored.FinishedAt);
    }

    private sealed class ThrowingAdapter : ISourceAdapter
    {
        public bool CanHandle(Source source) => true;

        public Task<FetchResult> FetchAsync(Source source, CancellationToken ct) =>
            throw new InvalidOperationException("boom");

        public Task<FeedParseResult> ParseAsync(Source source, FetchResult fetched, CancellationToken ct) =>
            throw new InvalidOperationException("boom");
    }
}
