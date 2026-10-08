using App.Domain.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Infrastructure.Persistence.Configurations;

internal sealed class SourceConfiguration : IEntityTypeConfiguration<Source>
{
    public void Configure(EntityTypeBuilder<Source> b)
    {
        b.Property(s => s.Name).HasMaxLength(120);
        b.Property(s => s.Type).HasConversion<string>().HasMaxLength(16);
        b.Property(s => s.DefaultKind).HasConversion<string>().HasMaxLength(16);
        b.Property(s => s.Adapter).HasMaxLength(32);
        b.Property(s => s.BaseUrl).HasMaxLength(2000);
        b.Property(s => s.LastError).HasMaxLength(1000);
        b.Ignore(s => s.Health);
        b.HasIndex(s => s.Name);
    }
}
