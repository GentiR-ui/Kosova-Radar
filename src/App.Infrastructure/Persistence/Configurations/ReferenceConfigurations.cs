using App.Domain.Reference;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Infrastructure.Persistence.Configurations;

internal sealed class CityConfiguration : IEntityTypeConfiguration<City>
{
    public void Configure(EntityTypeBuilder<City> b)
    {
        b.Property(c => c.Slug).HasMaxLength(64);
        b.Property(c => c.Name).HasMaxLength(120);
        b.HasIndex(c => c.Slug).IsUnique();
    }
}

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> b)
    {
        b.Property(c => c.Slug).HasMaxLength(64);
        b.Property(c => c.Name).HasMaxLength(120);
        b.Property(c => c.Kind).HasConversion<string>().HasMaxLength(16);
        b.HasIndex(c => new { c.Slug, c.Kind }).IsUnique();
    }
}
