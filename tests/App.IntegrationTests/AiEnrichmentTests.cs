using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Application.Ai;
using App.Domain.Content;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace App.IntegrationTests;

/// <summary>The AI step through the whole fetch endpoint, with a scripted provider (the Fake rules are covered by the acceptance tests).</summary>
[Collection(IntegrationCollection.Name)]
public sealed class AiEnrichmentTests(AppFactory app) : IDisposable
{
    private readonly List<IDisposable> _factories = [];

    private sealed class ScriptedProvider(Func<AiExtractionRequest, CancellationToken, Task<AiExtractionResult>> script, bool enabled = true) : IAiProvider
    {
        public int Calls;
        public string Name => "Scripted";
        public bool IsEnabled => enabled;

        public Task<AiExtractionResult> ExtractAsync(AiExtractionRequest request, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return script(request, ct);
        }
    }

    private static AiExtractionResult Answer(AiExtractedFields? fields = null, string? category = null, double confidence = 0.8) =>
        new("scripted-1", "req-1", confidence, fields ?? new AiExtractedFields { City = "Gjilan" }, category, InputTokens: 120, OutputTokens: 30);

    private static string Token() => Guid.NewGuid().ToString("N")[..8];

    private HttpClient CreateAdmin(ScriptedProvider provider, int dailyLimit = 100_000, int timeoutSeconds = 15)
    {
        var factory = app.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            services.RemoveAll<IAiProvider>();
            services.AddSingleton<IAiProvider>(provider);
            services.RemoveAll<AiOptions>();
            services.AddSingleton(new AiOptions { Provider = "Fake", DailyCallLimit = dailyLimit, TimeoutSeconds = timeoutSeconds });
        }));
        _factories.Add(factory);
        return factory.CreateClient();
    }

    private static async Task LoginAsync(HttpClient client)
    {
        var res = await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@kosovaradar.local", password = "Radar-admin-dev-2026" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    private static string Item(string id, string title, string? text, string extra = "")
    {
        var textJson = text is null ? "" : $",\"text\":{JsonSerializer.Serialize(text)}";
        return $"{{\"externalId\":\"{id}\",\"kind\":\"event\",\"title\":\"{title}\",\"url\":\"https://example.test/{id}\"{extra}{textJson}}}";
    }

    private static string Payload(params string[] items) => "{\"items\":[" + string.Join(",", items) + "]}";

    private static string Future(int days) => DateTimeOffset.UtcNow.AddDays(days).ToString("yyyy-MM-ddTHH:mm:ss+00:00");

    private static async Task<Guid> CreateSourceAsync(HttpClient admin, string payload)
    {
        var res = await admin.PostAsJsonAsync("/api/admin/sources", new
        {
            name = $"Burim {Token()}",
            type = "fixture",
            baseUrl = (string?)null,
            fixturePayload = payload,
            defaultKind = "event",
            pollIntervalMinutes = 1440,
            isActive = false,
            isUnreliable = false,
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task SetPayloadAsync(HttpClient admin, Guid id, string payload)
    {
        var res = await admin.PutAsJsonAsync($"/api/admin/sources/{id}", new
        {
            name = $"Burim {Token()}",
            type = "fixture",
            baseUrl = (string?)null,
            fixturePayload = payload,
            defaultKind = "event",
            pollIntervalMinutes = 1440,
            isActive = false,
            isUnreliable = false,
        });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    private static async Task FetchAsync(HttpClient admin, Guid id)
    {
        var res = await admin.PostAsync($"/api/admin/sources/{id}/fetch", null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    private async Task<(List<Candidate> Candidates, List<AiCall> Calls)> StoredAsync(Guid sourceId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var candidates = await db.Candidates.AsNoTracking().Where(c => c.SourceId == sourceId).OrderBy(c => c.Fields.Title).ToListAsync();
        var ids = candidates.Select(c => c.Id).ToList();
        var calls = await db.AiCalls.AsNoTracking().Where(a => ids.Contains(a.CandidateId)).OrderBy(a => a.StartedAt).ToListAsync();
        return (candidates, calls);
    }

    private async Task<int> CallsTodayAsync()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var dayStart = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().AiCalls.CountAsync(a => a.StartedAt >= dayStart);
    }

    private static async Task<JsonElement> FetchRunAsync(HttpClient admin, Guid id)
    {
        var res = await admin.PostAsync($"/api/admin/sources/{id}/fetch", null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task An_unchanged_candidate_with_AiUnavailable_is_retried_at_most_three_times_and_counted_unchanged()
    {
        var failing = true;
        var provider = new ScriptedProvider((_, _) => failing
            ? throw new HttpRequestException("429")
            : Task.FromResult(Answer(new AiExtractedFields { City = "Gjilan" })));
        var admin = CreateAdmin(provider);
        await LoginAsync(admin);
        var id = await CreateSourceAsync(admin, Payload(Item("a", $"Retry {Token()}", "Diçka në qytet", $",\"startsAt\":\"{Future(10)}\"")));

        await FetchAsync(admin, id); // call 1 fails
        var run2 = await FetchRunAsync(admin, id); // call 2 fails
        var run3 = await FetchRunAsync(admin, id); // call 3 fails
        var run4 = await FetchRunAsync(admin, id); // cap reached: no call

        Assert.Equal(3, provider.Calls);
        foreach (var run in new[] { run2, run3, run4 })
        {
            Assert.Equal(1, run.GetProperty("unchanged").GetInt32());
            Assert.Equal(0, run.GetProperty("updated").GetInt32());
        }
        var (candidates, calls) = await StoredAsync(id);
        Assert.Equal(3, calls.Count);
        Assert.Contains(Assert.Single(candidates).Issues, i => i.Code == IssueCodes.AiUnavailable);
    }

    [Fact]
    public async Task A_retry_that_succeeds_clears_AiUnavailable_and_later_fetches_make_no_call()
    {
        var failing = true;
        var provider = new ScriptedProvider((_, _) => failing
            ? throw new HttpRequestException("429")
            : Task.FromResult(Answer(new AiExtractedFields { City = "Gjilan" })));
        var admin = CreateAdmin(provider);
        await LoginAsync(admin);
        var id = await CreateSourceAsync(admin, Payload(Item("a", $"Retry {Token()}", "Diçka në qytet", $",\"startsAt\":\"{Future(10)}\"")));

        await FetchAsync(admin, id);
        failing = false;
        var run = await FetchRunAsync(admin, id);
        await FetchAsync(admin, id);

        Assert.Equal(2, provider.Calls);
        Assert.Equal(1, run.GetProperty("unchanged").GetInt32());
        var (candidates, _) = await StoredAsync(id);
        var c = Assert.Single(candidates);
        Assert.DoesNotContain(c.Issues, i => i.Code == IssueCodes.AiUnavailable);
        Assert.Equal("gjilan", c.Fields.CitySlug);
    }

    [Fact]
    public async Task The_retry_respects_the_daily_limit()
    {
        var provider = new ScriptedProvider((_, _) => throw new HttpRequestException("429"));
        var before = await CallsTodayAsync();
        var admin = CreateAdmin(provider, dailyLimit: before + 1);
        await LoginAsync(admin);
        var id = await CreateSourceAsync(admin, Payload(Item("a", $"Retry {Token()}", "Diçka në qytet", $",\"startsAt\":\"{Future(10)}\"")));

        await FetchAsync(admin, id);
        await FetchAsync(admin, id);

        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task A_successful_call_is_stored_and_the_candidate_records_the_model_request_and_confidence()
    {
        var provider = new ScriptedProvider((_, _) => Task.FromResult(Answer(new AiExtractedFields { City = "Gjilan" }, category: "muzike", confidence: 0.75)));
        var admin = CreateAdmin(provider);
        await LoginAsync(admin);
        var t = Token();
        var id = await CreateSourceAsync(admin, Payload(Item("a", $"Mbrëmje {t}", "Diçka në qytet", $",\"startsAt\":\"{Future(10)}\"")));

        await FetchAsync(admin, id);

        var (candidates, calls) = await StoredAsync(id);
        var c = Assert.Single(candidates);
        Assert.Equal("gjilan", c.Fields.CitySlug);
        Assert.Equal("scripted-1", c.AiModel);
        Assert.Equal("req-1", c.AiRequestId);
        Assert.Equal(0.75, c.AiConfidence);
        Assert.NotNull(c.AiProcessedAt);
        Assert.Equal(["cityText", "citySlug"], c.AiSuggestedFields);
        Assert.NotNull(c.SuggestedCategoryId);
        var call = Assert.Single(calls);
        Assert.Equal(c.Id, call.CandidateId);
        Assert.Equal(("Scripted", "scripted-1", "req-1", true, null), (call.Provider, call.Model, call.RequestId, call.Success, call.ErrorCode));
        Assert.Equal((0.75, 120, 30), (call.Confidence, call.InputTokens, call.OutputTokens));
    }

    [Fact]
    public async Task Items_without_text_and_unchanged_items_cost_no_ai_call()
    {
        var provider = new ScriptedProvider((_, _) => Task.FromResult(Answer()));
        var admin = CreateAdmin(provider);
        await LoginAsync(admin);
        var t = Token();
        var id = await CreateSourceAsync(admin, Payload(Item("a", $"Pa tekst {t}", null), Item("b", $"Me tekst {t}", "Në qytet")));

        await FetchAsync(admin, id);
        Assert.Equal(1, provider.Calls);
        await FetchAsync(admin, id); // nothing changed

        Assert.Equal(1, provider.Calls);
        Assert.Single((await StoredAsync(id)).Calls);
    }

    [Theory]
    [InlineData("provider", AiErrorCodes.ProviderError)]
    [InlineData("invalid-exception", AiErrorCodes.InvalidOutput)]
    [InlineData("invalid-answer", AiErrorCodes.InvalidOutput)]
    [InlineData("timeout", AiErrorCodes.Timeout)]
    public async Task A_failed_call_is_stored_and_the_candidate_needs_review_without_ai_data(string failure, string errorCode)
    {
        var provider = new ScriptedProvider(async (_, ct) =>
        {
            switch (failure)
            {
                case "provider": throw new HttpRequestException("503 from the provider");
                case "invalid-exception": throw new AiInvalidOutputException("not json");
                case "timeout": await Task.Delay(Timeout.Infinite, ct); break;
            }
            // Unusable answer: confidence outside 0..1, and a category the kind does not have.
            return Answer(new AiExtractedFields { City = "Gjilan" }, category: "teknologji", confidence: 0.9);
        });
        var admin = CreateAdmin(provider, timeoutSeconds: 1);
        await LoginAsync(admin);
        var id = await CreateSourceAsync(admin, Payload(Item("a", $"Mbrëmje {Token()}", "Në qytet", $",\"startsAt\":\"{Future(10)}\"")));

        await FetchAsync(admin, id);

        var (candidates, calls) = await StoredAsync(id);
        var c = Assert.Single(candidates);
        Assert.Contains(c.Issues, i => i.Code == IssueCodes.AiUnavailable);
        Assert.Equal(ValidationOutcome.NeedsReview, c.Validation);
        Assert.Null(c.Fields.CitySlug);
        Assert.Null(c.AiModel);
        Assert.Empty(c.AiSuggestedFields);
        Assert.Null(c.SuggestedCategoryId);
        var call = Assert.Single(calls);
        Assert.False(call.Success);
        Assert.Equal(errorCode, call.ErrorCode);
    }

    [Fact]
    public async Task The_daily_limit_stops_calls_and_flags_the_remaining_candidates()
    {
        var provider = new ScriptedProvider((_, _) => Task.FromResult(Answer()));
        var admin = CreateAdmin(provider, dailyLimit: await CallsTodayAsync() + 1);
        await LoginAsync(admin);
        var t = Token();
        var id = await CreateSourceAsync(admin, Payload(
            Item("a", $"Njëra {t}", "Në qytet"), Item("b", $"Dyta {t}", "Në qytet"), Item("c", $"Treta {t}", "Në qytet")));

        await FetchAsync(admin, id);

        var (candidates, calls) = await StoredAsync(id);
        Assert.Equal(1, provider.Calls);
        Assert.Single(calls);
        Assert.Single(candidates, c => c.AiModel is not null);
        var limited = candidates.Where(c => c.AiModel is null).ToList();
        Assert.Equal(2, limited.Count);
        Assert.All(limited, c =>
        {
            Assert.Contains(c.Issues, i => i.Code == IssueCodes.AiUnavailable);
            Assert.Equal(ValidationOutcome.NeedsReview, c.Validation);
            Assert.Null(c.Fields.CityText);
        });
    }

    [Fact]
    public async Task A_zero_limit_means_no_call_at_all()
    {
        var provider = new ScriptedProvider((_, _) => Task.FromResult(Answer()));
        var admin = CreateAdmin(provider, dailyLimit: 0);
        await LoginAsync(admin);
        var id = await CreateSourceAsync(admin, Payload(Item("a", $"Njëra {Token()}", "Në qytet")));

        await FetchAsync(admin, id);

        Assert.Equal(0, provider.Calls);
        Assert.Empty((await StoredAsync(id)).Calls);
    }

    [Fact]
    public async Task A_changed_item_runs_the_ai_again_and_loses_what_the_old_text_gave_it()
    {
        var provider = new ScriptedProvider((req, _) => req.Text.Contains("#ai-fail")
            ? throw new InvalidOperationException("down")
            : Task.FromResult(Answer(new AiExtractedFields { City = "Gjilan" }, category: "sport")));
        var admin = CreateAdmin(provider);
        await LoginAsync(admin);
        var t = Token();
        var id = await CreateSourceAsync(admin, Payload(Item("a", $"Ndeshje {t}", "Në qytet", $",\"startsAt\":\"{Future(10)}\"")));
        await FetchAsync(admin, id);
        var first = Assert.Single((await StoredAsync(id)).Candidates);
        Assert.Equal("gjilan", first.Fields.CitySlug);
        Assert.NotNull(first.SuggestedCategoryId);

        // The provider is down when the item changes: the candidate is updated in place, the old AI data goes, AiUnavailable appears.
        await SetPayloadAsync(admin, id, Payload(Item("a", $"Ndeshje {t}", "#ai-fail Në qytet", $",\"startsAt\":\"{Future(10)}\"")));
        await FetchAsync(admin, id);
        var (candidates, calls) = await StoredAsync(id);
        var second = Assert.Single(candidates);
        Assert.Equal(first.Id, second.Id);
        Assert.Null(second.Fields.CitySlug);
        Assert.Null(second.AiModel);
        Assert.Null(second.SuggestedCategoryId);
        Assert.Empty(second.AiSuggestedFields);
        Assert.Contains(second.Issues, i => i.Code == IssueCodes.AiUnavailable);
        Assert.Equal([true, false], calls.Select(c => c.Success));

        // The text disappears: no call, no AiUnavailable.
        await SetPayloadAsync(admin, id, Payload(Item("a", $"Ndeshje {t}", null, $",\"startsAt\":\"{Future(10)}\"")));
        await FetchAsync(admin, id);
        var third = Assert.Single((await StoredAsync(id)).Candidates);
        Assert.DoesNotContain(third.Issues, i => i.Code == IssueCodes.AiUnavailable);
        Assert.Equal(2, provider.Calls);
    }

    [Fact]
    public async Task Without_a_provider_a_gap_the_text_could_fill_is_flagged_but_a_complete_item_is_not()
    {
        var provider = new ScriptedProvider((_, _) => throw new InvalidOperationException("never called"), enabled: false);
        var admin = CreateAdmin(provider);
        await LoginAsync(admin);
        var t = Token();
        var id = await CreateSourceAsync(admin, Payload(
            Item("gap", $"Pa qytet {t}", "Në Gjilan", $",\"startsAt\":\"{Future(10)}\""),
            Item("full", $"Me qytet {t}", "Në Gjilan", $",\"startsAt\":\"{Future(10)}\",\"city\":\"Prizren\"")));

        await FetchAsync(admin, id);

        var (candidates, calls) = await StoredAsync(id);
        Assert.Equal(0, provider.Calls);
        Assert.Empty(calls);
        Assert.Contains(candidates.Single(c => c.Fields.Title!.StartsWith("Pa qytet")).Issues, i => i.Code == IssueCodes.AiUnavailable);
        var full = candidates.Single(c => c.Fields.Title!.StartsWith("Me qytet"));
        Assert.DoesNotContain(full.Issues, i => i.Code == IssueCodes.AiUnavailable);
        Assert.Equal(ValidationOutcome.Valid, full.Validation);
    }

    [Fact]
    public async Task Calls_made_before_a_run_crashes_stay_in_the_ledger()
    {
        Guid sourceId = default;
        var provider = new ScriptedProvider(async (_, _) =>
        {
            // Deleting the run row makes the run's final save fail (optimistic concurrency): the run crashes after its calls.
            await using var scope = app.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().JobExecutions
                .Where(j => j.SourceId == sourceId && j.Status == JobStatus.Running).ExecuteDeleteAsync();
            return Answer();
        });
        var admin = CreateAdmin(provider);
        await LoginAsync(admin);
        var t = Token();
        sourceId = await CreateSourceAsync(admin, Payload(
            Item("a", $"Një {t}", "Tekst", $",\"startsAt\":\"{Future(10)}\""),
            Item("b", $"Dy {t}", "Tekst", $",\"startsAt\":\"{Future(11)}\"")));
        var before = await CallsTodayAsync();

        var res = await admin.PostAsync($"/api/admin/sources/{sourceId}/fetch", null);

        Assert.False(res.IsSuccessStatusCode);
        Assert.Equal(2, provider.Calls);
        Assert.Equal(before + 2, await CallsTodayAsync());
        Assert.Empty((await StoredAsync(sourceId)).Candidates);
    }

    public void Dispose()
    {
        foreach (var f in _factories) f.Dispose();
    }
}
