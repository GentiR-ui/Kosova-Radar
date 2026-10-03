using App.Domain.Reference;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Persistence;

/// <summary>Upserts cities and categories (design.md §4) on every start. Idempotent.</summary>
public sealed class ReferenceDataSeeder(AppDbContext db)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        var cities = await db.Cities.ToDictionaryAsync(c => c.Slug, ct);
        foreach (var (slug, name, aliases) in ReferenceData.Cities)
        {
            if (!cities.TryGetValue(slug, out var city)) db.Cities.Add(new City { Slug = slug, Name = name, Aliases = [.. aliases] });
            else { city.Name = name; city.Aliases = [.. aliases]; }
        }

        var categories = await db.Categories.ToDictionaryAsync(c => (c.Slug, c.Kind), ct);
        foreach (var (slug, name, kind) in ReferenceData.Categories)
        {
            if (!categories.TryGetValue((slug, kind), out var category)) db.Categories.Add(new Category { Slug = slug, Name = name, Kind = kind });
            else category.Name = name;
        }

        await db.SaveChangesAsync(ct);
    }
}
