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

        // varchar: the id is printable ASCII by construction (DeviceEventId.TryRead).
        builder.Property(m => m.DeviceEventId)
            .HasMaxLength(MachineMessage.DeviceEventIdMaxLength)
            .IsUnicode(false);

        // The ingest dedupe arbiter: a retried event races its original here, and exactly one row
        // survives. Filtered, so the earlier firmware's messages (no event id) are never compared.
        builder.HasIndex(m => new { m.MachineId, m.DeviceEventId })
            .IsUnique()
            .HasFilter("[device_event_id] IS NOT NULL")
            .HasDatabaseName("ix_machine_messages_device_event_id");

        // Serves "latest status", "history, newest first" and "last seen" - each one a seek on
        // machine_id followed by a short range read. The id breaks ties between messages received
        // in the same tick, so paging is stable.
        builder.HasIndex(m => new { m.MachineId, m.ReceivedAt, m.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("ix_machine_messages_machine_id_received_at");

        // tinyint, defaulting to Pending (0) - which is what makes every message stored before
        // processing existed part of the first backlog.
        builder.Property(m => m.ProcessingStatus).HasConversion<byte>().IsRequired();
        builder.Property(m => m.ProcessingError).HasMaxLength(MachineMessage.ProcessingErrorMaxLength);

        // The worker's queue, and the failures the status endpoint counts. Filtered, so each holds
        // only its own handful of rows however long the history grows; the pending one is read in id
        // order. The predicates must stay literal for SQL Server to match them to the queries.
        builder.HasIndex(m => m.Id, "ix_machine_messages_pending")
            .HasFilter("[processing_status] = 0")
            .HasDatabaseName("ix_machine_messages_pending");
        builder.HasIndex(m => m.Id, "ix_machine_messages_failed")
            .HasFilter("[processing_status] = 3")
            .HasDatabaseName("ix_machine_messages_failed");
    }
}
