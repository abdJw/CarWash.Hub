using CarWash.Hub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarWash.Hub.Persistence.Configurations;

/// <summary>Maps <see cref="MachineMessage"/> to the <c>machine_messages</c> table.</summary>
/// <remarks>Layer: Persistence.</remarks>
public sealed class MachineMessageConfiguration : IEntityTypeConfiguration<MachineMessage>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MachineMessage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("machine_messages", table =>
            table.HasCheckConstraint("ck_machine_messages_payload_size_bytes", "payload_size_bytes >= 0"));

        builder.HasKey(m => m.Id);

        // The one table whose keys the database generates: append-only, always read newest-first per
        // machine, so a monotonic bigint identity is the right clustering.
        builder.Property(m => m.Id).UseIdentityColumn();

        builder.Property(m => m.ReceivedAt).IsRequired();

        // nvarchar(max): the message's data as text. Not constrained to ISJSON - a machine may send a
        // JSON string holding any text - so analysis queries guard with ISJSON(payload) = 1 themselves.
        builder.Property(m => m.Payload).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(m => m.PayloadSizeBytes).IsRequired();

        builder.Property(m => m.ClientKeyId).HasMaxLength(32).IsRequired();
        builder.Property(m => m.SourceIp).HasMaxLength(64);
        builder.Property(m => m.UserAgent).HasMaxLength(512);
        builder.Property(m => m.CorrelationId).HasMaxLength(64);

        // Serves "latest status", "history, newest first" and "last seen" - each one a seek on
        // machine_id followed by a short range read. The id breaks ties between messages received
        // in the same tick, so paging is stable.
        builder.HasIndex(m => new { m.MachineId, m.ReceivedAt, m.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("ix_machine_messages_machine_id_received_at");
    }
}
