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

        // Quiet hours are a pair: both set, or neither (open 24 hours). No database default - a null
        // pair is a real choice - so existing stations get theirs from the migration.
        builder.ToTable("gas_stations", table => table.HasCheckConstraint(
            "ck_gas_stations_quiet_hours",
            "(quiet_hours_start IS NULL AND quiet_hours_end IS NULL) OR (quiet_hours_start IS NOT NULL AND quiet_hours_end IS NOT NULL AND quiet_hours_start <> quiet_hours_end)"));
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
