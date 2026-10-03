using App.Application.Abstractions;
using App.Domain.Reference;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Reference;

public sealed record CityDto(string Slug, string Name);

public sealed record CategoryDto(string Slug, string Name, ItemKind Kind);

public static class ReferenceQueries
{
    public static async Task<List<CityDto>> CitiesAsync(IAppDbContext db, CancellationToken ct) =>
        (await db.Cities.AsNoTracking().Select(c => new CityDto(c.Slug, c.Name)).ToListAsync(ct))
        .OrderBy(c => c.Slug, StringComparer.Ordinal).ToList(); // ordinal, independent of DB collation

    public static async Task<List<CategoryDto>> CategoriesAsync(IAppDbContext db, ItemKind? kind, CancellationToken ct) =>
        (await db.Categories.AsNoTracking()
            .Where(c => kind == null || c.Kind == kind)
            .Select(c => new CategoryDto(c.Slug, c.Name, c.Kind))
            .ToListAsync(ct))
        .OrderBy(c => c.Slug, StringComparer.Ordinal).ThenBy(c => c.Kind).ToList(); // by slug (design.md §5)
}
