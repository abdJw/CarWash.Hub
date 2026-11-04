using System.Globalization;
using CarWash.Hub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarWash.Hub.Persistence.Configurations;

/// <summary>Maps <see cref="MachineDeviceSettings"/> to the <c>machine_device_settings</c> table.</summary>
/// <remarks>
/// Layer: Persistence. One-to-one with <c>machines</c>, sharing its key, with no navigation - so no
/// soft-delete filter reaches it, and a machine that is deleted and restored keeps its revisions.
/// Not row-versioned: only the worker writes it.
/// </remarks>
public sealed class MachineDeviceSettingsConfiguration : IEntityTypeConfiguration<MachineDeviceSettings>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MachineDeviceSettings> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var max = MachineDeviceSettings.MaxRevision.ToString(CultureInfo.InvariantCulture);

        builder.ToTable("machine_device_settings", table =>
        {
            table.HasCheckConstraint("ck_machine_device_settings_desired_revision", $"desired_revision BETWEEN 0 AND {max}");
            table.HasCheckConstraint("ck_machine_device_settings_applied_revision", "applied_revision >= 0 AND applied_revision <= desired_revision");
            table.HasCheckConstraint("ck_machine_device_settings_last_ack_revision", $"last_ack_revision BETWEEN 0 AND {max}");
            table.HasCheckConstraint("ck_machine_device_settings_max_reported_revision", $"max_reported_revision BETWEEN 0 AND {max}");
        });

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        // varchar: the document is ASCII by construction, and the firmware's limit is in bytes.
        builder.Property(s => s.ServicesJson).HasMaxLength(MachineDeviceSettings.ServicesJsonMaxLength).IsUnicode(false);
        builder.Property(s => s.Issue).HasConversion<byte>();
        builder.Property(s => s.IssueService).HasMaxLength(ServiceDefinition.CodeMaxLength).IsUnicode(false);
        builder.Property(s => s.LastAckStatus).HasConversion<byte>();

        builder.HasOne<Machine>()
            .WithOne()
            .HasForeignKey<MachineDeviceSettings>(s => s.Id)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
