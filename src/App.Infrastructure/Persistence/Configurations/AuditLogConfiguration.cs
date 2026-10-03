using App.Domain.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Infrastructure.Persistence.Configurations;

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.Property(a => a.UserEmail).HasMaxLength(256);
        b.Property(a => a.Action).HasMaxLength(64);
        b.Property(a => a.EntityType).HasMaxLength(32);
        b.Property(a => a.Changes).HasColumnType("jsonb");
        b.HasIndex(a => new { a.EntityId, a.At });
    }
}
