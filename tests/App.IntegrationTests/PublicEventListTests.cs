using App.Application.Abstractions;
using App.Application.Public;
using App.Domain.Content;
using App.Domain.Sources;
using App.Domain.Text;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace App.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public class PublicEventListTests(AppFactory app)
{
    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    private static async Task<List<Event>> SeedAsync(AppDbContext db, string token, params (DateTimeOffset Start, DateTimeOffset? End, ItemStatus Status)[] items)
    {
        var source = new Source { Name = $"S-{token}", Type = SourceType.Fixture, PollIntervalMinutes = 60 };
        var city = await db.Cities.SingleAsync(c => c.Slug == "prishtine");
        db.Sources.Add(source);
        var events = items.Select((x, i) => new Event
        {
            Title = $"Event {token} {i}",
            CityId = city.Id,
            StartsAt = x.Start,
            EndsAt = x.End,
            Status = x.Status,
            PrimarySourceId = source.Id,
            SourceUrl = $"https://example.test/{token}/{i}",
            Fingerprint = $"{token}-{i}",
            SearchText = TextNormalizer.Fold($"Event {token} {i}"),
            LastVerifiedAt = Now,
            PublishedAt = Now,
            CreatedAt = Now,
            UpdatedAt = Now,
        }).ToList();
        db.Events.AddRange(events);
        await db.SaveChangesAsync();
        return events;
    }

    [Fact]
    public async Task Date_filters_use_pristina_midnight_across_the_march_dst_change()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var token = "dst" + Guid.NewGuid().ToString("N")[..8];
        // 2027-03-28 is the DST change day: Pristina is UTC+1 until 02:00, UTC+2 afterwards.
        var events = await SeedAsync(db, token,
            (new(2027, 3, 27, 22, 59, 0, TimeSpan.Zero), null, ItemStatus.Published), // 27th 23:59 local
            (new(2027, 3, 27, 23, 0, 0, TimeSpan.Zero), null, ItemStatus.Published),  // 28th 00:00 local
            (new(2027, 3, 28, 21, 59, 0, TimeSpan.Zero), null, ItemStatus.Published), // 28th 23:59 local (UTC+2)
            (new(2027, 3, 28, 22, 0, 0, TimeSpan.Zero), null, ItemStatus.Published)); // 29th 00:00 local
        var queries = new PublicEventQueries(db, new FixedClock(Now));

        async Task<List<string>> Titles(string from, string to)
        {
            var (result, errors) = await queries.ListAsync(new(null, null, from, to, token, null, null), default);
            Assert.Null(errors);
            return result!.Items.Select(e => e.Title[^1..]).ToList();
        }

        Assert.Equal(["1", "2"], await Titles("2027-03-28", "2027-03-28"));
        Assert.Equal(["0"], await Titles("2027-03-27", "2027-03-27"));
        Assert.Equal(["3"], await Titles("2027-03-29", "2027-03-29"));
        Assert.Equal(4, events.Count);
    }

    [Fact]
    public async Task Sql_active_filter_matches_the_domain_rule()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var token = "act" + Guid.NewGuid().ToString("N")[..8];
        var events = await SeedAsync(db, token,
            (Now.AddHours(-5), null, ItemStatus.Published),                    // no end, started 5 h ago: active
            (Now.AddHours(-6), null, ItemStatus.Published),                    // exactly 6 h: ended
            (Now.AddHours(-10), Now.AddMinutes(1), ItemStatus.Published),      // explicit end in the future
            (Now.AddHours(-10), Now, ItemStatus.Published),                    // explicit end == now: ended
            (Now.AddHours(-1), Now.AddHours(-2), ItemStatus.Published),        // end before start: falls back to start + 6 h
            (Now.AddHours(1), null, ItemStatus.Withdrawn),
            (Now.AddHours(1), null, ItemStatus.Expired));

        var sqlIds = await db.Events.Where(e => e.Fingerprint.StartsWith(token)).Where(ActiveItems.Event(Now)).Select(e => e.Id).ToListAsync();
        var domainIds = events.Where(e => e.IsActive(Now)).Select(e => e.Id).ToList();
        Assert.Equal(domainIds.Order(), sqlIds.Order());
        Assert.Equal(3, sqlIds.Count);
    }

    [Fact]
    public async Task Expiration_runner_expires_only_published_events_and_deals_past_their_end()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var token = "exp" + Guid.NewGuid().ToString("N")[..8];
        var events = await SeedAsync(db, token,
            (Now.AddHours(-7), null, ItemStatus.Published),                  // 0: ended (start + 6 h)
            (Now.AddHours(-1), null, ItemStatus.Published),                  // 1: running
            (Now.AddHours(-7), null, ItemStatus.Withdrawn),                  // 2: withdrawn stays
            (Now.AddHours(-10), Now, ItemStatus.Published),                  // 3: explicit end == now: ended
            (Now.AddHours(-10), Now.AddMinutes(1), ItemStatus.Published),    // 4: explicit end in the future
            (Now.AddHours(-8), Now.AddHours(-9), ItemStatus.Published));     // 5: end before start, falls back to start + 6 h: ended
        var source = await db.Sources.SingleAsync(s => s.Name == $"S-{token}");
        Deal NewDeal(int i, DateTimeOffset? until, DateTimeOffset verified) => new()
        {
            Title = $"Deal {token} {i}",
            Status = ItemStatus.Published,
            ValidUntil = until,
            PrimarySourceId = source.Id,
            SourceUrl = $"https://example.test/{token}/d{i}",
            Fingerprint = $"{token}-d{i}",
            LastVerifiedAt = verified,
            PublishedAt = Now,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        db.Deals.AddRange(NewDeal(0, Now.AddMinutes(-1), Now), NewDeal(1, Now.AddMinutes(1), Now), NewDeal(2, null, Now.AddDays(-30)));
        await db.SaveChangesAsync();

        var expired = await new ExpirationRunner(db).RunAsync(Now, default);

        Assert.True(expired >= 4);
        await using var read = app.Services.CreateAsyncScope();
        var rdb = read.ServiceProvider.GetRequiredService<AppDbContext>();
        var eventStatus = await rdb.Events.AsNoTracking().Where(e => e.Fingerprint.StartsWith(token)).OrderBy(e => e.Title)
            .Select(e => e.Status).ToListAsync();
        Assert.Equal([ItemStatus.Expired, ItemStatus.Published, ItemStatus.Withdrawn, ItemStatus.Expired, ItemStatus.Published, ItemStatus.Expired], eventStatus);
        // Undated deals are never flipped by the job: public reads treat them as expired by the 7-day rule (ADR-024).
        var dealStatus = await rdb.Deals.AsNoTracking().Where(d => d.Fingerprint.StartsWith(token)).OrderBy(d => d.Title)
            .Select(d => d.Status).ToListAsync();
        Assert.Equal([ItemStatus.Expired, ItemStatus.Published, ItemStatus.Published], dealStatus);
        Assert.Equal(6, events.Count);
    }

    [Theory]
    [InlineData("page=2147483647&pageSize=100")]
    [InlineData("to=2200-12-31")]
    [InlineData("from=1900-01-01")]
    public async Task Extreme_but_valid_parameters_do_not_fail(string query)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var parts = query.Split('&').Select(p => p.Split('=')).ToDictionary(p => p[0], p => p[1]);
        var request = new EventListRequest(null, null, parts.GetValueOrDefault("from"), parts.GetValueOrDefault("to"), null,
            parts.GetValueOrDefault("page"), parts.GetValueOrDefault("pageSize"));
        var (result, errors) = await new PublicEventQueries(db, new FixedClock(Now)).ListAsync(request, default);
        Assert.Null(errors);
        Assert.NotNull(result);
    }

    [Theory]
    [InlineData("page", "abc")]
    [InlineData("pageSize", "1.5")]
    [InlineData("from", "9999-12-31")]
    [InlineData("to", "0001-01-01")]
    public async Task Malformed_parameters_are_reported_by_name(string field, string value)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var request = new EventListRequest(null, null, field == "from" ? value : null, field == "to" ? value : null, null,
            field == "page" ? value : null, field == "pageSize" ? value : null);
        var (result, errors) = await new PublicEventQueries(db, new FixedClock(Now)).ListAsync(request, default);
        Assert.Null(result);
        Assert.Contains(field, errors!.Keys);
    }
}
