using CarWash.Hub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarWash.Hub.Persistence.Configurations;

/// <summary>Maps <see cref="ServiceUsage"/> to the <c>service_usages</c> table.</summary>
/// <remarks>Layer: Persistence.</remarks>
public sealed class ServiceUsageConfiguration : IEntityTypeConfiguration<ServiceUsage>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ServiceUsage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("service_usages");
        builder.HasKey(u => u.Id);

        // Append-only and read by range, like the messages it comes from.
        builder.Property(u => u.Id).UseIdentityColumn();

        builder.Property(u => u.ServiceCode).HasMaxLength(ServiceDefinition.CodeMaxLength).IsUnicode(false).IsRequired();
        builder.Property(u => u.OccurredAtSource).HasConversion<byte>().IsRequired();

        builder.HasOne(u => u.Machine)
            .WithMany()
            .HasForeignKey(u => u.MachineId)
            .OnDelete(DeleteBehavior.Restrict);

        // No navigations to the catalog or the message: the worker never loads them, and a
        // navigation to the soft-deletable catalog would put a join into every usage query.
        builder.HasOne<ServiceDefinition>()
            .WithMany()
            .HasForeignKey(u => u.ServiceCode)
            .HasPrincipalKey(s => s.Code)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<MachineMessage>()
            .WithMany()
            .HasForeignKey(u => u.MachineMessageId)
            .OnDelete(DeleteBehavior.Restrict);

        // The station at the time of use, for the money it earned; the machine may move later.
        builder.HasOne<GasStation>()
            .WithMany()
            .HasForeignKey(u => u.GasStationId)
            .OnDelete(DeleteBehavior.Restrict);

        // A message is never counted twice, however often it is processed.
        builder.HasIndex(u => new { u.MachineMessageId, u.ItemIndex })
            .IsUnique()
            .HasDatabaseName("ix_service_usages_machine_message_id_item_index");

        // "Uses of this service on this machine since the refill" - a range count - and the
        // duplicate check for retried messages.
        builder.HasIndex(u => new { u.MachineId, u.ServiceCode, u.OccurredAt })
            .HasDatabaseName("ix_service_usages_machine_id_service_code_occurred_at");
    }
}
