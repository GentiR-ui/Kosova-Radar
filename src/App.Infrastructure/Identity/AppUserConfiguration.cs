using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Infrastructure.Identity;

internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> b)
    {
        b.Property(u => u.Email).HasMaxLength(256);
        // Identity only checks email uniqueness in code; the index makes a concurrent double submit fail safely.
        b.HasIndex(u => u.NormalizedEmail).HasDatabaseName("EmailIndex").IsUnique();
    }
}
