namespace App.Domain.Reference;

/// <summary>A Kosovo municipality (seeded reference data).</summary>
public sealed class City
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Slug { get; set; }
    public required string Name { get; set; }
    public List<string> Aliases { get; set; } = [];
}
