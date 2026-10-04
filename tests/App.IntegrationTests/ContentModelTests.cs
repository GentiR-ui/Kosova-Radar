using App.Domain.Content;
using App.Domain.Reference;
using App.Domain.Sources;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace App.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public class ContentModelTests(AppFactory app)
{
    private static Candidate NewCandidate(Guid sourceId) => new()
    {
        Kind = ItemKind.Deal,
        SourceId = sourceId,
        SourceUrl = "https://example.test/x",
        Fingerprint = "f",
        ContentHash = "h",
        Evidence = "{\"raw\":true}",
        Fields = new CandidateFields { Title = "Ofertë", OldPrice = 10.50m, StartsAt = DateTimeOffset.UnixEpoch },
        Issues = [new Issue("UnknownCity", IssueSeverity.Review, "cityText", "msg")],
        AiSuggestedFields = ["title"],
    };

    [Fact]
    public async Task Candidate_round_trips_jsonb_fields_issues_and_lists()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var source = new Source { Name = "S", Type = SourceType.Fixture, PollIntervalMinutes = 60 };
        var candidate = NewCandidate(source.Id);
        db.Sources.Add(source);
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();

        await using var read = app.Services.CreateAsyncScope();
        var loaded = await read.ServiceProvider.GetRequiredService<AppDbContext>()
            .Candidates.AsNoTracking().SingleAsync(c => c.Id == candidate.Id);
        Assert.Equal(candidate.Fields, loaded.Fields);
        Assert.Equal("UnknownCity", Assert.Single(loaded.Issues).Code);
        Assert.Equal(IssueSeverity.Review, loaded.Issues[0].Severity);
        Assert.Equal(["title"], loaded.AiSuggestedFields);
        Assert.Equal(ItemKind.Deal, loaded.Kind);
        Assert.NotEqual(0u, loaded.Version);
    }

    [Fact]
    public async Task Concurrent_candidate_edits_fail_instead_of_overwriting()
    {
        Guid id;
        await using (var seed = app.Services.CreateAsyncScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<AppDbContext>();
            var source = new Source { Name = "S2", Type = SourceType.Fixture, PollIntervalMinutes = 60 };
            var c = NewCandidate(source.Id);
            db.Sources.Add(source);
            db.Candidates.Add(c);
            await db.SaveChangesAsync();
            id = c.Id;
        }

        await using var a = app.Services.CreateAsyncScope();
        await using var b = app.Services.CreateAsyncScope();
        var dbA = a.ServiceProvider.GetRequiredService<AppDbContext>();
        var dbB = b.ServiceProvider.GetRequiredService<AppDbContext>();
        var first = await dbA.Candidates.SingleAsync(c => c.Id == id);
        var second = await dbB.Candidates.SingleAsync(c => c.Id == id);
        first.RejectionReason = "one";
        second.RejectionReason = "two";
        await dbA.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => dbB.SaveChangesAsync());
    }
}

[Collection(IntegrationCollection.Name)]
public class ContentItemModelTests(AppFactory app)
{
    private async Task<(Guid CityId, Guid SourceId)> SeedParentsAsync(AppDbContext db)
    {
        await app.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope().ServiceProvider
            .GetRequiredService<ReferenceDataSeeder>().SeedAsync();
        var city = await db.Cities.AsNoTracking().FirstAsync(c => c.Slug == "prishtine");
        var source = new Source { Name = "S-items", Type = SourceType.Fixture, PollIntervalMinutes = 60 };
        db.Sources.Add(source);
        await db.SaveChangesAsync();
        return (city.Id, source.Id);
    }

    [Fact]
    public async Task Event_and_deal_round_trip_with_status_money_and_concurrency_token()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (cityId, sourceId) = await SeedParentsAsync(db);
        var ev = new Event { Title = "E", CityId = cityId, PrimarySourceId = sourceId, SourceUrl = "https://e.test", Fingerprint = "f", PriceAmount = 12.345m, Status = ItemStatus.Withdrawn };
        var deal = new Deal { Title = "D", PrimarySourceId = sourceId, SourceUrl = "https://d.test", Fingerprint = "f", OldPrice = 10m, NewPrice = 7.5m };
        db.Events.Add(ev);
        db.Deals.Add(deal);
        await db.SaveChangesAsync();

        await using var a = app.Services.CreateAsyncScope();
        await using var b = app.Services.CreateAsyncScope();
        var dbA = a.ServiceProvider.GetRequiredService<AppDbContext>();
        var dbB = b.ServiceProvider.GetRequiredService<AppDbContext>();
        var evA = await dbA.Events.SingleAsync(e => e.Id == ev.Id);
        Assert.Equal(ItemStatus.Withdrawn, evA.Status);
        Assert.Equal(12.35m, evA.PriceAmount); // numeric(18,2) rounds
        var evB = await dbB.Events.SingleAsync(e => e.Id == ev.Id);
        var dealA = await dbA.Deals.SingleAsync(d => d.Id == deal.Id);
        var dealB = await dbB.Deals.SingleAsync(d => d.Id == deal.Id);
        evA.Title = "one"; evB.Title = "two";
        dealA.Title = "one"; dealB.Title = "two";
        await dbA.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => dbB.SaveChangesAsync());
    }

    [Fact]
    public async Task Venue_name_is_unique_per_city_and_references_must_exist()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (cityId, sourceId) = await SeedParentsAsync(db);
        var name = $"venue-{Guid.NewGuid():N}";
        db.Venues.Add(new Venue { Name = name, NormalizedName = name, CityId = cityId });
        await db.SaveChangesAsync();

        await using var second = app.Services.CreateAsyncScope();
        var db2 = second.ServiceProvider.GetRequiredService<AppDbContext>();
        db2.Venues.Add(new Venue { Name = name, NormalizedName = name, CityId = cityId });
        await Assert.ThrowsAsync<DbUpdateException>(() => db2.SaveChangesAsync());

        await using var third = app.Services.CreateAsyncScope();
        var db3 = third.ServiceProvider.GetRequiredService<AppDbContext>();
        db3.Events.Add(new Event { Title = "orphan", CityId = Guid.NewGuid(), PrimarySourceId = sourceId, SourceUrl = "https://e.test", Fingerprint = "f" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db3.SaveChangesAsync());
    }
}
