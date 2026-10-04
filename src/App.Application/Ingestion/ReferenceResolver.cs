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
    private readonly Dictionary<(ItemKind, string), Guid> _categoryIds = [];

    public ReferenceResolver(CityMatcher cities, IEnumerable<Category> categories)
    {
        _cities = cities;
        foreach (var c in categories)
        {
            _categories.TryAdd((c.Kind, TextNormalizer.Fold(c.Slug)), c.Slug);
            _categories.TryAdd((c.Kind, TextNormalizer.Fold(c.Name)), c.Slug);
            _categoryIds.TryAdd((c.Kind, c.Slug), c.Id);
        }
    }

    public static async Task<ReferenceResolver> LoadAsync(IAppDbContext db, CancellationToken ct) =>
        new(new CityMatcher(await db.Cities.AsNoTracking().ToListAsync(ct)),
            await db.Categories.AsNoTracking().ToListAsync(ct));

    public string? CitySlug(string? text) => _cities.Find(text)?.Slug;

    public IReadOnlyList<string> CategorySlugs(ItemKind kind) =>
        _categoryIds.Keys.Where(k => k.Item1 == kind).Select(k => k.Item2).Order().ToList();

    public Guid? CategoryId(ItemKind kind, string slug) =>
        _categoryIds.TryGetValue((kind, slug), out var id) ? id : null;

    public CandidateFields Resolve(ItemKind kind, CandidateFields fields) =>
        fields with
        {
            CitySlug = CitySlug(fields.CityText),
            CategorySlug = FindCategory(kind, fields.CategoryText),
        };

    // A category matches when its slug or its name equals the text after folding; only categories of the item's kind count.
    private string? FindCategory(ItemKind kind, string? text)
    {
        var folded = TextNormalizer.Fold(text);
        return folded.Length > 0 && _categories.TryGetValue((kind, folded), out var slug) ? slug : null;
    }
}
