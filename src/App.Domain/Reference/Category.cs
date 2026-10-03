namespace App.Domain.Reference;

/// <summary>An event or deal category (seeded reference data). Unique per (Slug, Kind).</summary>
public sealed class Category
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Slug { get; set; }
    public required string Name { get; set; }
    public ItemKind Kind { get; set; }
}
