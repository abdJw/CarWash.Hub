using System.Globalization;
using CarWash.Hub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarWash.Hub.Persistence.Configurations;

/// <summary>Maps <see cref="StationServicePrice"/> to the <c>station_service_prices</c> table.</summary>
/// <remarks>Layer: Persistence. Removing a price is the soft delete.</remarks>
public sealed class StationServicePriceConfiguration : IEntityTypeConfiguration<StationServicePrice>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<StationServicePrice> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("station_service_prices", table => table.HasCheckConstraint(
            "ck_station_service_prices_unit_price",
            string.Create(CultureInfo.InvariantCulture, $"unit_price BETWEEN 0 AND {StationServicePrice.MaxUnitPrice}")));

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.ServiceCode).HasMaxLength(ServiceDefinition.CodeMaxLength).IsUnicode(false).IsRequired();
        builder.Property(p => p.UnitPrice).HasPrecision(9, 3);
        builder.Property(p => p.Note).HasMaxLength(StationServicePrice.NoteMaxLength);

        builder.HasOne<GasStation>()
            .WithMany()
            .HasForeignKey(p => p.GasStationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ServiceDefinition>()
            .WithMany()
            .HasForeignKey(p => p.ServiceCode)
            .HasPrincipalKey(s => s.Code)
            .OnDelete(DeleteBehavior.Restrict);

        // One live price per station, service and day. Unlike the other unique indexes here this one
        // skips deleted rows: removing a mistaken price must not block entering the right one for the
        // same day.
        builder.HasIndex(p => new { p.GasStationId, p.ServiceCode, p.EffectiveFrom })
            .IsUnique()
            .HasFilter("is_deleted = 0")
            .HasDatabaseName("ix_station_service_prices_station_service_effective_from");
    }
}
