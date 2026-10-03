using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace App.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public class ReferenceDataTests(AppFactory app)
{
    [Fact]
    public async Task Seeding_is_idempotent_and_keeps_aliases()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var seeder = scope.ServiceProvider.GetRequiredService<ReferenceDataSeeder>();
        await seeder.SeedAsync();
        await seeder.SeedAsync();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(38, await db.Cities.CountAsync());
        Assert.Equal(14, await db.Categories.CountAsync());
        var prishtine = await db.Cities.AsNoTracking().SingleAsync(c => c.Slug == "prishtine");
        Assert.Contains("Pristina", prishtine.Aliases);
    }

    [Fact]
    public async Task Unfiltered_categories_are_ordered_by_slug()
    {
        var json = System.Text.Json.JsonDocument.Parse(await app.CreateClient().GetStringAsync("/api/categories")).RootElement;
        var slugs = json.EnumerateArray().Select(c => c.GetProperty("slug").GetString()!).ToList();
        Assert.Equal(slugs.OrderBy(s => s, StringComparer.Ordinal), slugs);
        Assert.Equal(14, slugs.Count);
    }
}
