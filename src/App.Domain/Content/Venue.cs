namespace App.Domain.Content;

/// <summary>Found-or-created on publish by (<see cref="NormalizedName"/>, <see cref="CityId"/>).</summary>
public sealed class Venue
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string NormalizedName { get; set; }
    public Guid CityId { get; set; }
}
