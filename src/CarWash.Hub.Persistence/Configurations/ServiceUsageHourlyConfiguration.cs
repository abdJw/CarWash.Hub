using CarWash.Hub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarWash.Hub.Persistence.Configurations;

/// <summary>Maps <see cref="ServiceUsageHourly"/> to the <c>service_usage_hourly</c> table.</summary>
/// <remarks>Layer: Persistence.</remarks>
public sealed class ServiceUsageHourlyConfiguration : IEntityTypeConfiguration<ServiceUsageHourly>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ServiceUsageHourly> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("service_usage_hourly", table =>
            table.HasCheckConstraint("ck_service_usage_hourly_usage_count", "usage_count >= 0"));

        // The surrogate key exists because the repository needs one; it is not the clustering. The
        // table is read as "this machine's hours in a range", so the natural key is clustered.
        builder.HasKey(h => h.Id).IsClustered(false);
        builder.Property(h => h.Id).UseIdentityColumn();

        builder.Property(h => h.ServiceCode).HasMaxLength(ServiceDefinition.CodeMaxLength).IsUnicode(false).IsRequired();

        builder.HasOne(h => h.Machine)
            .WithMany()
            .HasForeignKey(h => h.MachineId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ServiceDefinition>()
            .WithMany()
            .HasForeignKey(h => h.ServiceCode)
            .HasPrincipalKey(s => s.Code)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(h => new { h.MachineId, h.HourStart, h.ServiceCode })
            .IsUnique()
            .IsClustered()
            .HasDatabaseName("ix_service_usage_hourly_machine_id_hour_start_service_code");

        // Fleet-wide charts and KPIs: every machine's hours in a range, without touching the
        // clustered index.
        builder.HasIndex(h => h.HourStart)
            .IncludeProperties(h => new { h.MachineId, h.ServiceCode, h.UsageCount })
            .HasDatabaseName("ix_service_usage_hourly_hour_start");
    }
}
