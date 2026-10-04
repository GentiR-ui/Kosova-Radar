using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using App.Domain.Content;
using App.Domain.Sources;
using App.Domain.Text;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace App.IntegrationTests;

/// <summary>Runs only when <c>PERF_TESTS=1</c>: seeds 10k published items and checks p95 &lt; 300 ms (non-functional §6).</summary>
public sealed class PerfFactAttribute : FactAttribute
{
    public PerfFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("PERF_TESTS") != "1") Skip = "Set PERF_TESTS=1 to run the 10k-item latency test.";
    }
}

[Collection(IntegrationCollection.Name)]
public sealed class PerformanceTests(AppFactory app, ITestOutputHelper output)
{
    private const int PerKind = 5_000;
    private const int Requests = 60;
    private static readonly string[] Words =
        ["koncert", "festival", "teatër", "ofertë", "zbritje", "pazar", "muzikë", "kinema", "sport", "shfaqje", "panair", "film", "dasmë", "cirk"];

    [PerfFact]
    public async Task Public_endpoints_have_p95_under_300ms_with_10k_published_items()
    {
        var now = DateTimeOffset.UtcNow;
        var sourceId = Guid.NewGuid();
        try
        {
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var source = new Source { Name = "Perf " + Guid.NewGuid().ToString("N"), Type = SourceType.Fixture, PollIntervalMinutes = 60 };
            source.Id = sourceId;
            db.Sources.Add(source);
            var cities = await db.Cities.ToListAsync();
            var rng = new Random(42);
            string Title(int i) => $"{Words[rng.Next(Words.Length)]} {Words[rng.Next(Words.Length)]} {i}";
            db.Events.AddRange(Enumerable.Range(0, PerKind).Select(i =>
            {
                var title = Title(i);
                return new Event
                {
                    Title = title, CityId = cities[i % cities.Count].Id, StartsAt = now.AddHours(1 + i), Status = ItemStatus.Published,
                    PrimarySourceId = source.Id, SourceUrl = $"https://perf.test/e/{i}", Fingerprint = $"perf-e-{source.Id:N}-{i}",
                    SearchText = TextNormalizer.Fold(title), LastVerifiedAt = now, PublishedAt = now, CreatedAt = now, UpdatedAt = now,
                };
            }));
            db.Deals.AddRange(Enumerable.Range(0, PerKind).Select(i =>
            {
                var title = Title(i);
                return new Deal
                {
                    Title = title, CityId = cities[i % cities.Count].Id, ValidUntil = now.AddHours(1 + i), Status = ItemStatus.Published,
                    PrimarySourceId = source.Id, SourceUrl = $"https://perf.test/d/{i}", Fingerprint = $"perf-d-{source.Id:N}-{i}",
                    SearchText = TextNormalizer.Fold(title), LastVerifiedAt = now, PublishedAt = now, CreatedAt = now, UpdatedAt = now,
                };
            }));
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync("ANALYZE \"Events\"; ANALYZE \"Deals\";");
        }

            using var client = app.CreateClient();
            var eventId = (await client.GetFromJsonAsync<JsonElement>("/api/events?pageSize=1")).GetProperty("items")[0].GetProperty("id").GetString();
            var dealId = (await client.GetFromJsonAsync<JsonElement>("/api/deals?pageSize=1")).GetProperty("items")[0].GetProperty("id").GetString();
            var endpoints = new[]
            {
                "/api/events", "/api/events?page=50&pageSize=20", "/api/events?search=koncert", "/api/deals", "/api/deals?search=zbritje",
                $"/api/events/{eventId}", $"/api/deals/{dealId}", "/api/search?q=festival", "/api/search?q=teater+film", $"/events/{eventId}",
            };
            var failures = new List<string>();
            foreach (var url in endpoints)
            {
                (await client.GetAsync(url)).EnsureSuccessStatusCode(); // warm-up
                var times = new List<double>();
                for (var i = 0; i < Requests; i++)
                {
                    var sw = Stopwatch.StartNew();
                    using var response = await client.GetAsync(url);
                    await response.Content.ReadAsByteArrayAsync();
                    sw.Stop();
                    response.EnsureSuccessStatusCode();
                    times.Add(sw.Elapsed.TotalMilliseconds);
                }
                times.Sort();
                var p95 = times[(int)Math.Ceiling(times.Count * 0.95) - 1];
                output.WriteLine($"p95 {p95,7:F1} ms  p50 {times[times.Count / 2],7:F1} ms  {url}");
                if (p95 >= 300) failures.Add($"{url}: p95 {p95:F0} ms");
            }
            Assert.Empty(failures);
        }
        finally
        {
            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Events.Where(e => e.PrimarySourceId == sourceId).ExecuteDeleteAsync();
            await db.Deals.Where(d => d.PrimarySourceId == sourceId).ExecuteDeleteAsync();
            await db.Sources.Where(s => s.Id == sourceId).ExecuteDeleteAsync();
        }
    }
}
