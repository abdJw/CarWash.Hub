using CarWash.Hub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarWash.Hub.Persistence.Configurations;

/// <summary>Maps <see cref="Alert"/> to the <c>alerts</c> table.</summary>
/// <remarks>Layer: Persistence. Not row-versioned: the worker and an acknowledgement write disjoint columns.</remarks>
public sealed class AlertConfiguration : IEntityTypeConfiguration<Alert>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Alert> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("alerts");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).UseIdentityColumn();
        builder.Property(a => a.ServiceCode).HasMaxLength(ServiceDefinition.CodeMaxLength).IsUnicode(false);
        builder.Property(a => a.Type).HasConversion<byte>().IsRequired();
        builder.Property(a => a.Severity).HasConversion<byte>().IsRequired();
        builder.Property(a => a.Resolution).HasConversion<byte>();

        builder.HasOne(a => a.Machine)
            .WithMany()
            .HasForeignKey(a => a.MachineId)
            .OnDelete(DeleteBehavior.Restrict);

        // One open alert per machine, service and type. The explicit filter replaces the
        // "IS NOT NULL" EF would add for the nullable service code, so a machine's no-activity alert
        // (no service) is covered too: a unique index treats NULLs as equal. A level alert replaced
        // by a more urgent one is resolved and the new one inserted in the same save - safe because
        // the type is part of the key.
        builder.HasIndex(a => new { a.MachineId, a.ServiceCode, a.Type })
            .IsUnique()
            .HasFilter("[resolved_at] IS NULL")
            .HasDatabaseName("ix_alerts_open");

        // "This machine's alerts, newest first" and "open alerts, newest first".
        builder.HasIndex(a => new { a.MachineId, a.Id })
            .IsDescending(false, true)
            .HasDatabaseName("ix_alerts_machine_id_id");
        builder.HasIndex(a => new { a.ResolvedAt, a.Id })
            .IsDescending(false, true)
            .HasDatabaseName("ix_alerts_resolved_at_id");
    }
}
