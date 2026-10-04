using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Domain.Content;
using App.Domain.Reference;
using App.Domain.Sources;
using App.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace App.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public class DashboardTests(AppFactory app)
{
    private async Task<HttpClient> LoginAdminAsync()
    {
        var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@kosovaradar.local", password = "Radar-admin-dev-2026" })).StatusCode);
        return client;
    }

    private static async Task<Dictionary<string, int>> GetAsync(HttpClient client)
    {
        var res = await client.GetAsync("/api/admin/dashboard");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        return json.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt32());
    }

    [Fact]
    public async Task Dashboard_needs_a_signed_in_user()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.CreateClient().GetAsync("/api/admin/dashboard")).StatusCode);
    }

    [Fact]
    public async Task Dashboard_counts_candidates_items_sources_and_ai_calls()
    {
        var admin = await LoginAdminAsync();
        var before = await GetAsync(admin);
        Assert.Equal(9, before.Count);

        var now = DateTimeOffset.UtcNow;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var city = db.Cities.First();
            Source NewSource(int failures, bool active) => new()
            {
                Name = $"Burim {Guid.NewGuid():N}"[..14],
                CreatedAt = now,
                IsActive = active,
                LastFetchedAt = now,
                ConsecutiveFailures = failures,
            };
            var healthy = NewSource(0, true);
            var degraded = NewSource(1, true);
            var failing = NewSource(Source.FailingThreshold, true);
            var disabled = NewSource(5, false);
            db.Sources.AddRange(healthy, degraded, failing, disabled);

            Candidate NewCandidate(CandidateStatus status, ValidationOutcome outcome) => new()
            {
                SourceId = healthy.Id,
                Kind = ItemKind.Event,
                SourceUrl = "https://example.test/x",
                Evidence = "{}",
                Fingerprint = Guid.NewGuid().ToString("N"),
                ContentHash = "h",
                FetchedAt = now,
                Status = status,
                Validation = outcome,
                Fields = new CandidateFields { Title = "T" },
            };
            db.Candidates.AddRange(
                NewCandidate(CandidateStatus.Pending, ValidationOutcome.NeedsReview),
                NewCandidate(CandidateStatus.Pending, ValidationOutcome.Valid),
                NewCandidate(CandidateStatus.Rejected, ValidationOutcome.NeedsReview));

            Event NewEvent(ItemStatus status, DateTimeOffset startsAt, DateTimeOffset updatedAt) => new()
            {
                Title = "E",
                CityId = city.Id,
                StartsAt = startsAt,
                Status = status,
                PrimarySourceId = healthy.Id,
                SourceUrl = "https://example.test/x",
                Fingerprint = Guid.NewGuid().ToString("N"),
                SearchText = "e",
                LastVerifiedAt = now,
                PublishedAt = now,
                CreatedAt = now,
                UpdatedAt = updatedAt,
            };
            db.Events.AddRange(
                NewEvent(ItemStatus.Published, now.AddDays(3), now),
                NewEvent(ItemStatus.Published, now.AddDays(-5), now.AddDays(-5)), // past its end, not yet flagged: not active
                NewEvent(ItemStatus.Withdrawn, now.AddDays(3), now),
                NewEvent(ItemStatus.Expired, now.AddDays(-2), now.AddDays(-1)),
                NewEvent(ItemStatus.Expired, now.AddDays(-20), now.AddDays(-15))); // expired too long ago

            db.Deals.AddRange(
                new Deal
                {
                    Title = "D",
                    Status = ItemStatus.Published,
                    ValidUntil = now.AddDays(2),
                    PrimarySourceId = healthy.Id,
                    SourceUrl = "https://example.test/x",
                    Fingerprint = Guid.NewGuid().ToString("N"),
                    SearchText = "d",
                    LastVerifiedAt = now,
                    PublishedAt = now,
                    CreatedAt = now,
                    UpdatedAt = now,
                },
                new Deal
                {
                    Title = "D2",
                    Status = ItemStatus.Expired,
                    ValidUntil = now.AddDays(-2),
                    PrimarySourceId = healthy.Id,
                    SourceUrl = "https://example.test/x",
                    Fingerprint = Guid.NewGuid().ToString("N"),
                    SearchText = "d",
                    LastVerifiedAt = now,
                    PublishedAt = now,
                    CreatedAt = now,
                    UpdatedAt = now.AddHours(-2),
                });

            AiCall Call(bool ok, DateTimeOffset at) => new() { CandidateId = Guid.NewGuid(), Provider = "Fake", Model = "m", Success = ok, StartedAt = at };
            db.AiCalls.AddRange(Call(true, now), Call(false, now), Call(false, now.AddDays(-2)));
            await db.SaveChangesAsync();
        }

        var after = await GetAsync(admin);
        int Delta(string key) => after[key] - before[key];
        Assert.Equal(2, Delta("pendingCandidates"));
        Assert.Equal(1, Delta("needsReviewCandidates"));
        Assert.Equal(1, Delta("activeEvents"));
        Assert.Equal(1, Delta("activeDeals"));
        Assert.Equal(2, Delta("expiredLast7Days"));
        Assert.Equal(1, Delta("failingSources"));
        Assert.Equal(1, Delta("degradedSources"));
        Assert.Equal(2, Delta("aiCallsToday"));
        Assert.Equal(1, Delta("aiErrorsToday"));
    }
}
