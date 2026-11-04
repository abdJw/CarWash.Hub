using System.Globalization;
using CarWash.Hub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarWash.Hub.Persistence.Configurations;

/// <summary>Maps <see cref="StationServiceDuration"/> to the <c>station_service_durations</c> table.</summary>
/// <remarks>Layer: Persistence.</remarks>
public sealed class StationServiceDurationConfiguration : IEntityTypeConfiguration<StationServiceDuration>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<StationServiceDuration> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var range = string.Create(
            CultureInfo.InvariantCulture,
            $"BETWEEN {StationServiceDuration.MinSeconds} AND {StationServiceDuration.MaxSeconds}");

        builder.ToTable("station_service_durations", table =>
        {
            table.HasCheckConstraint("ck_station_service_durations_duration1", $"duration1_seconds {range}");
            table.HasCheckConstraint("ck_station_service_durations_duration2", $"duration2_seconds IS NULL OR duration2_seconds {range}");
            table.HasCheckConstraint("ck_station_service_durations_duration3", $"duration3_seconds IS NULL OR duration3_seconds {range}");

            // Either one position or three: the machine has no two-position selector.
            table.HasCheckConstraint(
                "ck_station_service_durations_positions",
                "(duration2_seconds IS NULL AND duration3_seconds IS NULL) OR (duration2_seconds IS NOT NULL AND duration3_seconds IS NOT NULL)");
        });

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.ServiceCode).HasMaxLength(ServiceDefinition.CodeMaxLength).IsUnicode(false).IsRequired();

        // Named explicitly: the snake_case convention keeps a digit glued to the next word
        // ("duration1seconds").
        builder.Property(d => d.Duration1Seconds).HasColumnName("duration1_seconds");
        builder.Property(d => d.Duration2Seconds).HasColumnName("duration2_seconds");
        builder.Property(d => d.Duration3Seconds).HasColumnName("duration3_seconds");

        builder.HasOne<GasStation>()
            .WithMany()
            .HasForeignKey(d => d.GasStationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ServiceDefinition>()
            .WithMany()
            .HasForeignKey(d => d.ServiceCode)
            .HasPrincipalKey(s => s.Code)
            .OnDelete(DeleteBehavior.Restrict);

        // One live row per station and service. Filtered like prices, so a removed row never blocks
        // setting the run times again.
        builder.HasIndex(d => new { d.GasStationId, d.ServiceCode })
            .IsUnique()
            .HasFilter("is_deleted = 0")
            .HasDatabaseName("ix_station_service_durations_station_service");
    }
}
