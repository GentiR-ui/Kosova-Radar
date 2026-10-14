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
public class PublicDealListTests(AppFactory app)
{
    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    private static async Task<(List<Event> Events, List<Deal> Deals)> SeedAsync(
        AppDbContext db, string token, int events, params (DateTimeOffset? Until, DateTimeOffset Verified, ItemStatus Status)[] deals) =>
        await SeedAsync(db, token, events, autoPublish: false, deals);

    private static async Task<(List<Event> Events, List<Deal> Deals)> SeedAsync(
        AppDbContext db, string token, int events, bool autoPublish, params (DateTimeOffset? Until, DateTimeOffset Verified, ItemStatus Status)[] deals)
    {
        var source = new Source { Name = $"S-{token}", Type = SourceType.Fixture, PollIntervalMinutes = 60, AutoPublish = autoPublish };
        db.Sources.Add(source);
        var city = await db.Cities.SingleAsync(c => c.Slug == "prishtine");
        var eventRows = Enumerable.Range(0, events).Select(i => new Event
        {
            Title = $"Event {token} {i}",
            CityId = city.Id,
            StartsAt = Now.AddDays(1 + i),
            Status = ItemStatus.Published,
            PrimarySourceId = source.Id,
            SourceUrl = $"https://example.test/{token}/e{i}",
            Fingerprint = $"{token}-e{i}",
            SearchText = TextNormalizer.Fold($"Event {token} {i}"),
            LastVerifiedAt = Now,
            PublishedAt = Now,
            CreatedAt = Now,
            UpdatedAt = Now,
        }).ToList();
        var dealRows = deals.Select((x, i) => new Deal
        {
            Title = $"Deal {token} {i}",
            ValidUntil = x.Until,
            Status = x.Status,
            PrimarySourceId = source.Id,
            SourceUrl = $"https://example.test/{token}/d{i}",
            Fingerprint = $"{token}-d{i}",
            SearchText = TextNormalizer.Fold($"Deal {token} {i}"),
            LastVerifiedAt = x.Verified,
            DiscountPercent = autoPublish ? 50 : null,
            PublishedAt = Now,
            CreatedAt = Now,
            UpdatedAt = Now,
        }).ToList();
        db.Events.AddRange(eventRows);
        db.Deals.AddRange(dealRows);
        await db.SaveChangesAsync();
        return (eventRows, dealRows);
    }

    [Fact]
    public async Task Sql_filter_equals_the_domain_rule_for_deals()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var token = "dr" + Guid.NewGuid().ToString("N")[..8];
        var (_, deals) = await SeedAsync(db, token, 0,
            (Now.AddSeconds(1), Now.AddDays(-30), ItemStatus.Published),   // dated, verified long ago: still active
            (Now, Now, ItemStatus.Published),                              // ends exactly now: expired
            (null, Now.AddDays(-7).AddSeconds(1), ItemStatus.Published),   // undated, just inside 7 days
            (null, Now.AddDays(-7), ItemStatus.Published),                 // undated, exactly 7 days: excluded
            (Now.AddDays(1), Now, ItemStatus.Withdrawn),
            (Now.AddDays(1), Now, ItemStatus.Expired));

        var viaSql = await db.Deals.Where(d => d.SearchText.Contains(token)).Where(ActiveItems.Deal(Now)).Select(d => d.Id).ToListAsync();

        Assert.Equal(deals.Where(d => d.IsActive(Now)).Select(d => d.Id).Order(), viaSql.Order());
        Assert.Equal(2, viaSql.Count);
    }

    [Fact]
    public async Task Sql_filter_equals_the_domain_rule_for_listing_deals_of_an_auto_publish_source()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var token = "dl" + Guid.NewGuid().ToString("N")[..8];
        var (_, deals) = await SeedAsync(db, token, 0, autoPublish: true,
            (null, Now.AddHours(-6).AddSeconds(1), ItemStatus.Published),  // listed 5 h 59 min ago: active
            (null, Now.AddHours(-6), ItemStatus.Published),                // exactly 6 h: stale
            (null, Now.AddDays(-1), ItemStatus.Published),                 // not listed since yesterday
            (Now.AddDays(1), Now.AddDays(-3), ItemStatus.Published),       // dated: the listing window does not apply
            (null, Now, ItemStatus.Expired));                              // ended by a fetch (price below the minimum)

        var viaSql = await db.Deals.Where(d => d.SearchText.Contains(token)).Where(ActiveItems.Deal(Now)).Select(d => d.Id).ToListAsync();
        var listing = (Deal d) => d.ValidUntil is null;
        var expected = deals.Where(d => d.IsActive(Now, listing(d))).Select(d => d.Id);

        Assert.Equal(expected.Order(), viaSql.Order());
        Assert.Equal(2, viaSql.Count);

        var wider = await db.Deals.Where(d => d.SearchText.Contains(token))
            .Where(ActiveItems.Deal(Now, new DealOptions { ListingStaleHours = 48 })).CountAsync();
        Assert.Equal(4, wider); // the three listed rows and the dated one; the expired row stays out
    }

    [Fact]
    public async Task Deals_without_valid_until_come_last_and_dated_ones_ascending()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var token = "do" + Guid.NewGuid().ToString("N")[..8];
        var (_, deals) = await SeedAsync(db, token, 0,
            (null, Now, ItemStatus.Published), (Now.AddDays(9), Now, ItemStatus.Published), (Now.AddDays(2), Now, ItemStatus.Published));

        var (page, errors) = await new PublicDealQueries(db, new FixedClock(Now)).ListAsync(new(null, null, token, null, null), default);

        Assert.Null(errors);
        Assert.Equal(new[] { deals[2].Id, deals[1].Id, deals[0].Id }, page!.Items.Select(d => d.Id).ToArray());
    }

    [Fact]
    public async Task Search_pages_run_through_events_then_deals_across_the_boundary()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var token = "sp" + Guid.NewGuid().ToString("N")[..8];
        var (events, deals) = await SeedAsync(db, token, 3,
            (Now.AddDays(3), Now, ItemStatus.Published), (Now.AddDays(2), Now, ItemStatus.Published));
        var queries = new PublicSearchQueries(db, new FixedClock(Now));
        var expected = events.OrderBy(e => e.StartsAt).Select(e => e.Id).Concat(deals.OrderBy(d => d.ValidUntil).Select(d => d.Id)).ToArray();

        var p1 = (await queries.SearchAsync(new(token, "1", "2"), default)).Result!;
        var p2 = (await queries.SearchAsync(new(token, "2", "2"), default)).Result!;
        var p3 = (await queries.SearchAsync(new(token, "3", "2"), default)).Result!;
        var p4 = (await queries.SearchAsync(new(token, "4", "2"), default)).Result!;

        Assert.Equal(5, p1.TotalCount);
        Assert.Equal(expected, p1.Items.Concat(p2.Items).Concat(p3.Items).Select(i => i.Id).ToArray());
        Assert.Equal(new[] { "event", "event", "event", "deal", "deal" }, p1.Items.Concat(p2.Items).Concat(p3.Items).Select(i => i.Kind).ToArray());
        Assert.Empty(p4.Items);
    }

    [Fact]
    public async Task Search_rejects_a_query_without_letters_or_digits_and_ignores_inactive_items()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var token = "sx" + Guid.NewGuid().ToString("N")[..8];
        var (_, deals) = await SeedAsync(db, token, 0,
            (Now.AddDays(1), Now, ItemStatus.Published), (Now.AddDays(-1), Now, ItemStatus.Published),
            (null, Now.AddDays(-8), ItemStatus.Published), (Now.AddDays(1), Now, ItemStatus.Withdrawn));
        var queries = new PublicSearchQueries(db, new FixedClock(Now));

        var (none, errors) = await queries.SearchAsync(new("--", null, null), default);
        var (found, _) = await queries.SearchAsync(new(token.ToUpperInvariant(), null, null), default);

        Assert.Null(none);
        Assert.Contains("q", errors!.Keys);
        Assert.Equal(new[] { deals[0].Id }, found!.Items.Select(i => i.Id).ToArray());
    }

    [Fact]
    public async Task Deal_list_filters_by_city_and_category_and_reports_unknown_slugs_together()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var token = "dl" + Guid.NewGuid().ToString("N")[..8];
        var (_, deals) = await SeedAsync(db, token, 0,
            (Now.AddDays(1), Now, ItemStatus.Published), (Now.AddDays(2), Now.AddDays(-3), ItemStatus.Published));
        var city = await db.Cities.SingleAsync(c => c.Slug == "prizren");
        deals[0].CityId = city.Id;
        await db.SaveChangesAsync();
        var queries = new PublicDealQueries(db, new FixedClock(Now));

        var (byCity, _) = await queries.ListAsync(new("prizren", null, token, null, null), default);
        var (page2, _) = await queries.ListAsync(new(null, null, token, "2", "1"), default);
        var (beyond, _) = await queries.ListAsync(new(null, null, token, "3", "1"), default);
        var (none, errors) = await queries.ListAsync(new("atlantis", "nope", null, null, null), default);

        Assert.Equal(new[] { deals[0].Id }, byCity!.Items.Select(d => d.Id).ToArray());
        Assert.Equal("fresh", byCity.Items[0].Freshness);
        Assert.Equal("stale", page2!.Items.Single().Freshness);
        Assert.Equal(2, beyond!.TotalCount);
        Assert.Empty(beyond.Items);
        Assert.Null(none);
        Assert.Equal(["category", "city"], errors!.Keys.Order().ToArray());
    }

    [Fact]
    public async Task Search_trims_the_query_and_rejects_short_and_bad_paging_together()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queries = new PublicSearchQueries(db, new FixedClock(Now));

        var (result, errors) = await queries.SearchAsync(new(" a ", "abc", "0"), default);

        Assert.Null(result);
        Assert.Equal(["page", "pageSize", "q"], errors!.Keys.Order().ToArray());
    }
}
