using CarWash.Hub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarWash.Hub.Persistence.Configurations;

/// <summary>Maps <see cref="ServiceDefinition"/> to the <c>service_definitions</c> table.</summary>
/// <remarks>Layer: Persistence. The rowversion comes from the <c>IHasRowVersion</c> convention.</remarks>
public sealed class ServiceDefinitionConfiguration : IEntityTypeConfiguration<ServiceDefinition>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ServiceDefinition> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("service_definitions", table =>
        {
            table.HasCheckConstraint("ck_service_definitions_low_threshold_percent", "low_threshold_percent BETWEEN 1 AND 99");
            table.HasCheckConstraint("ck_service_definitions_default_capacity", "default_capacity IS NULL OR default_capacity > 0");
        });

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        // varchar: codes are ASCII by the parser's rule, and every table referencing one by code
        // uses the same type, so the foreign keys compare without conversion.
        builder.Property(s => s.Code).HasMaxLength(ServiceDefinition.CodeMaxLength).IsUnicode(false).IsRequired();
        builder.Property(s => s.Name).HasMaxLength(100).IsRequired();
        builder.Property(s => s.NameAr).HasMaxLength(100).IsRequired();
        builder.Property(s => s.Icon).HasMaxLength(64).IsUnicode(false);
        builder.Property(s => s.Color).HasMaxLength(16).IsUnicode(false);
        builder.Property(s => s.IsActive).HasDefaultValue(true).IsRequired();

        // An alternate key rather than a unique index: usage rows reference the catalog by code, the
        // value the machines send, not by an internal id the parser would have to look up. It also
        // makes the code immutable, which it must be.
        builder.HasAlternateKey(s => s.Code).HasName("ak_service_definitions_code");
    }
}
