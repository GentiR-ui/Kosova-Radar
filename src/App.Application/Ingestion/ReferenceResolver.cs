using App.Application.Abstractions;
using App.Domain.Content;
using App.Domain.Reference;
using App.Domain.Text;
using Microsoft.EntityFrameworkCore;

namespace App.Application.Ingestion;

/// <summary>
/// Resolves free-text city and category to reference slugs (design.md §6). Unresolved text stays as text with a
/// null slug: a city nobody knows must not become a guess. Build once per run: the folded lookups are computed in the constructor.
/// </summary>
public sealed class ReferenceResolver
{
    private readonly CityMatcher _cities;

    // Per kind: folded slug and folded name -> slug.
    private readonly Dictionary<(ItemKind, string), string> _categories = [];

    public ReferenceResolver(CityMatcher cities, IEnumerable<Category> categories)
    {
        _cities = cities;
        foreach (var c in categories)
        {
            _categories.TryAdd((c.Kind, TextNormalizer.Fold(c.Slug)), c.Slug);
            _categories.TryAdd((c.Kind, TextNormalizer.Fold(c.Name)), c.Slug);
        }
    }

    public static async Task<ReferenceResolver> LoadAsync(IAppDbContext db, CancellationToken ct) =>
        new(new CityMatcher(await db.Cities.AsNoTracking().ToListAsync(ct)),
            await db.Categories.AsNoTracking().ToListAsync(ct));

    public CandidateFields Resolve(ItemKind kind, CandidateFields fields) =>
        fields with
        {
            CitySlug = _cities.Find(fields.CityText)?.Slug,
            CategorySlug = FindCategory(kind, fields.CategoryText),
        };

    // A category matches when its slug or its name equals the text after folding; only categories of the item's kind count.
    private string? FindCategory(ItemKind kind, string? text)
    {
        var folded = TextNormalizer.Fold(text);
        return folded.Length > 0 && _categories.TryGetValue((kind, folded), out var slug) ? slug : null;
    }
}
