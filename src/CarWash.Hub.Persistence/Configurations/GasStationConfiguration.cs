using CarWash.Hub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarWash.Hub.Persistence.Configurations;

/// <summary>Maps <see cref="GasStation"/> to the <c>gas_stations</c> table.</summary>
/// <remarks>Layer: Persistence. The rowversion comes from the <c>IHasRowVersion</c> convention.</remarks>
public sealed class GasStationConfiguration : IEntityTypeConfiguration<GasStation>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<GasStation> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("gas_stations");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.Code).HasMaxLength(32).IsRequired();
        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
        builder.Property(s => s.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(s => s.City).HasMaxLength(100);
        builder.Property(s => s.Address).HasMaxLength(500);
        builder.Property(s => s.IsActive).HasDefaultValue(true).IsRequired();

        // Unique including soft-deleted rows, like every unique index here: a retired station's
        // code is not silently reused by a new one.
        builder.HasIndex(s => s.Code).IsUnique().HasDatabaseName("ix_gas_stations_code");
    }
}
