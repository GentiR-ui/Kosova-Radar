using App.Domain.Text;

namespace App.Domain.Reference;

/// <summary>
/// Finds the city whose name, slug or alias equals a text after folding (design.md §4).
/// Build once per batch: the folded lookup is computed in the constructor.
/// </summary>
public sealed class CityMatcher
{
    private readonly Dictionary<string, City> _byFolded = new(StringComparer.Ordinal);

    public CityMatcher(IEnumerable<City> cities)
    {
        foreach (var city in cities)
            foreach (var text in city.Aliases.Prepend(city.Slug.Replace('-', ' ')).Prepend(city.Name))
                _byFolded.TryAdd(TextNormalizer.Fold(text), city);
    }

    public City? Find(string? text) => _byFolded.GetValueOrDefault(TextNormalizer.Fold(text));
}
