using CarWash.Hub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarWash.Hub.Persistence.Configurations;

/// <summary>Maps <see cref="ServiceUsageDaily"/> to the <c>service_usage_daily</c> table.</summary>
/// <remarks>Layer: Persistence.</remarks>
public sealed class ServiceUsageDailyConfiguration : IEntityTypeConfiguration<ServiceUsageDaily>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ServiceUsageDaily> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("service_usage_daily", table =>
            table.HasCheckConstraint("ck_service_usage_daily_usage_count", "usage_count >= 0"));

        // As for the hourly rollup: the surrogate key is for the repository, and the natural key -
        // read as "these days" - is clustered.
        builder.HasKey(d => d.Id).IsClustered(false);
        builder.Property(d => d.Id).UseIdentityColumn();

        builder.Property(d => d.ServiceCode).HasMaxLength(ServiceDefinition.CodeMaxLength).IsUnicode(false).IsRequired();

        // Foreign keys without navigations, so no soft-delete filter reaches this table: a deleted
        // machine or station still shows the money it earned.
        builder.HasOne<Machine>()
            .WithMany()
            .HasForeignKey(d => d.MachineId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<GasStation>()
            .WithMany()
            .HasForeignKey(d => d.GasStationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ServiceDefinition>()
            .WithMany()
            .HasForeignKey(d => d.ServiceCode)
            .HasPrincipalKey(s => s.Code)
            .OnDelete(DeleteBehavior.Restrict);

        // SQL Server treats nulls as equal here, so an unassigned machine also gets one row a day.
        builder.HasIndex(d => new { d.BusinessDate, d.GasStationId, d.MachineId, d.ServiceCode })
            .IsUnique()
            .IsClustered()
            .HasDatabaseName("ix_service_usage_daily_business_date_station_machine_service");
    }
}
