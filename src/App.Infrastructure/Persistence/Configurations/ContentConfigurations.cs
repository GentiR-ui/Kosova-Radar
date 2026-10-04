using System.Text.Json;
using System.Text.Json.Serialization;
using App.Domain.Content;
using App.Domain.Reference;
using App.Domain.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Infrastructure.Persistence.Configurations;

internal static class ContentConfigurationExtensions
{
    /// <summary>Maps <c>Version</c> to PostgreSQL <c>xmin</c> as an optimistic-concurrency token.</summary>
    public static void UseXminVersion<T>(this EntityTypeBuilder<T> b) where T : class =>
        b.Property<uint>("Version").HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
}

internal static class JsonbExtensions
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Stores a value as jsonb (System.Text.Json, enums by name); changes are detected on the serialized form.</summary>
    public static PropertyBuilder<T> HasJsonbConversion<T>(this PropertyBuilder<T> b) where T : class
    {
        b.HasColumnType("jsonb").HasConversion(
            v => JsonSerializer.Serialize(v, Options),
            s => JsonSerializer.Deserialize<T>(s, Options)!,
            new ValueComparer<T>(
                (x, y) => JsonSerializer.Serialize(x, Options) == JsonSerializer.Serialize(y, Options),
                v => JsonSerializer.Serialize(v, Options).GetHashCode(),
                v => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, Options), Options)!));
        return b;
    }
}

internal sealed class CandidateConfiguration : IEntityTypeConfiguration<Candidate>
{
    public void Configure(EntityTypeBuilder<Candidate> b)
    {
        b.Property(c => c.Kind).HasConversion<string>().HasMaxLength(16);
        b.Property(c => c.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(c => c.Validation).HasConversion<string>().HasMaxLength(16);
        b.Property(c => c.DuplicateOfKind).HasConversion<string>().HasMaxLength(16);
        b.Property(c => c.ExternalId).HasMaxLength(200);
        b.Property(c => c.SourceUrl).HasMaxLength(2000);
        b.Property(c => c.Fingerprint).HasMaxLength(64);
        b.Property(c => c.ContentHash).HasMaxLength(64);
        b.Property(c => c.Evidence).HasMaxLength(65536);
        b.Property(c => c.RejectionReason).HasMaxLength(500);
        b.Property(c => c.AiModel).HasMaxLength(120);
        b.Property(c => c.AiRequestId).HasMaxLength(200);
        b.Property(c => c.Fields).HasJsonbConversion();
        b.Property(c => c.Issues).HasJsonbConversion();
        b.HasOne<Source>().WithMany().HasForeignKey(c => c.SourceId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Category>().WithMany().HasForeignKey(c => c.SuggestedCategoryId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(c => c.Fingerprint);
        b.HasIndex(c => new { c.Status, c.Validation });
        b.HasIndex(c => c.SourceId);
        b.UseXminVersion();
    }
}

internal sealed class VenueConfiguration : IEntityTypeConfiguration<Venue>
{
    public void Configure(EntityTypeBuilder<Venue> b)
    {
        b.Property(v => v.Name).HasMaxLength(300);
        b.Property(v => v.NormalizedName).HasMaxLength(300);
        b.HasIndex(v => new { v.NormalizedName, v.CityId }).IsUnique();
        b.HasOne<City>().WithMany().HasForeignKey(v => v.CityId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> b)
    {
        b.Property(e => e.Title).HasMaxLength(300);
        b.Property(e => e.Description).HasMaxLength(10_000);
        b.Property(e => e.PriceText).HasMaxLength(300);
        b.Property(e => e.PriceAmount).HasPrecision(18, 2);
        b.Property(e => e.Currency).HasMaxLength(3);
        b.Property(e => e.ImageUrl).HasMaxLength(2000);
        b.Property(e => e.SourceUrl).HasMaxLength(2000);
        b.Property(e => e.Fingerprint).HasMaxLength(64);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(16);
        b.Ignore(e => e.EffectiveEnd);
        b.HasIndex(e => e.StartsAt);
        b.HasIndex(e => e.CityId);
        b.HasOne<City>().WithMany().HasForeignKey(e => e.CityId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Category>().WithMany().HasForeignKey(e => e.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Venue>().WithMany().HasForeignKey(e => e.VenueId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Source>().WithMany().HasForeignKey(e => e.PrimarySourceId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => e.CategoryId);
        b.HasIndex(e => e.Status);
        b.HasIndex(e => e.PrimarySourceId);
        b.HasIndex(e => e.Fingerprint);
        b.UseXminVersion();
    }
}

internal sealed class DealConfiguration : IEntityTypeConfiguration<Deal>
{
    public void Configure(EntityTypeBuilder<Deal> b)
    {
        b.Property(d => d.Title).HasMaxLength(300);
        b.Property(d => d.Description).HasMaxLength(10_000);
        b.Property(d => d.MerchantName).HasMaxLength(300);
        b.Property(d => d.OldPrice).HasPrecision(18, 2);
        b.Property(d => d.NewPrice).HasPrecision(18, 2);
        b.Property(d => d.PriceText).HasMaxLength(300);
        b.Property(d => d.Currency).HasMaxLength(3);
        b.Property(d => d.ImageUrl).HasMaxLength(2000);
        b.Property(d => d.SourceUrl).HasMaxLength(2000);
        b.Property(d => d.Fingerprint).HasMaxLength(64);
        b.Property(d => d.Status).HasConversion<string>().HasMaxLength(16);
        b.HasIndex(d => d.ValidUntil);
        b.HasOne<City>().WithMany().HasForeignKey(d => d.CityId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Category>().WithMany().HasForeignKey(d => d.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Source>().WithMany().HasForeignKey(d => d.PrimarySourceId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(d => d.Status);
        b.HasIndex(d => d.PrimarySourceId);
        b.HasIndex(d => d.Fingerprint);
        b.UseXminVersion();
    }
}

internal sealed class ItemSourceConfiguration : IEntityTypeConfiguration<ItemSource>
{
    public void Configure(EntityTypeBuilder<ItemSource> b)
    {
        b.Property(i => i.ItemKind).HasConversion<string>().HasMaxLength(16);
        b.Property(i => i.SourceUrl).HasMaxLength(2000);
        b.Property(i => i.Fingerprint).HasMaxLength(64);
        b.HasIndex(i => new { i.ItemId, i.ItemKind });
        b.HasIndex(i => i.Fingerprint);
        b.HasOne<Source>().WithMany().HasForeignKey(i => i.SourceId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Candidate>().WithMany().HasForeignKey(i => i.CandidateId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class JobExecutionConfiguration : IEntityTypeConfiguration<JobExecution>
{
    public void Configure(EntityTypeBuilder<JobExecution> b)
    {
        b.Property(j => j.JobName).HasConversion<string>().HasMaxLength(32);
        b.Property(j => j.Trigger).HasConversion<string>().HasMaxLength(16);
        b.Property(j => j.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(j => j.ErrorCode).HasMaxLength(64);
        b.Property(j => j.Error).HasMaxLength(1000);
        b.Property(j => j.CorrelationId).HasMaxLength(64);
        b.HasIndex(j => new { j.SourceId, j.StartedAt });
        b.HasOne<Source>().WithMany().HasForeignKey(j => j.SourceId).OnDelete(DeleteBehavior.Restrict);
    }
}
