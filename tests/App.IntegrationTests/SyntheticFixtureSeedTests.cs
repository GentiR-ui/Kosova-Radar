using App.Domain.Content;
using App.Domain.Sources;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace App.IntegrationTests;

/// <summary>T040: the development fixture source is seeded and fetched once, and only when <c>Seed__SyntheticFixtures</c> is on.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class SyntheticFixtureSeedTests(AppFactory app)
{
    private static async Task<Source?> FindSourceAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Sources
            .AsNoTracking().SingleOrDefaultAsync(s => s.Name == SyntheticFixtureSeeder.SourceName);
    }

    [Fact]
    public async Task Seed_creates_an_inactive_source_with_24_candidates_and_the_expected_issues_once()
    {
        await using var factory = app.WithWebHostBuilder(b => b.UseSetting("Seed:SyntheticFixtures", "true"));
        using var _ = factory.CreateClient(); // starts the host, which seeds

        var source = await FindSourceAsync(factory.Services);
        Assert.NotNull(source);
        Assert.False(source.IsActive);
        Assert.NotNull(source.LastFetchedAt);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var candidates = await db.Candidates.AsNoTracking().Where(c => c.SourceId == source.Id).ToListAsync();
        Assert.Equal(24, candidates.Count);
        Assert.All(candidates, c => Assert.True(c.IsSynthetic));

        var codes = candidates.SelectMany(c => c.Issues.Select(i => i.Code)).ToHashSet();
        foreach (var code in new[]
                 {
                     IssueCodes.MissingStartsAt, IssueCodes.Expired, IssueCodes.InvalidUrl, IssueCodes.UnknownCity,
                     IssueCodes.InvalidDateRange, IssueCodes.InvalidImageUrl, IssueCodes.AiUnavailable,
                     IssueCodes.MissingPrice, IssueCodes.ConflictingData, IssueCodes.PriceInconsistent,
                 })
            Assert.Contains(code, codes);
        Assert.Contains(candidates, c => c.Validation == ValidationOutcome.Valid);

        // A second start does not fetch again.
        var runs = await db.JobExecutions.CountAsync(j => j.SourceId == source.Id);
        await scope.ServiceProvider.GetRequiredService<SyntheticFixtureSeeder>().SeedAsync();
        Assert.Equal(runs, await db.JobExecutions.CountAsync(j => j.SourceId == source.Id));
    }
}
