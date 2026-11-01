using CarWash.Hub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarWash.Hub.Persistence.Configurations;

/// <summary>Maps <see cref="Refill"/> to the <c>refills</c> table.</summary>
/// <remarks>Layer: Persistence. Voiding is the soft delete; the void reason sits beside it.</remarks>
public sealed class RefillConfiguration : IEntityTypeConfiguration<Refill>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Refill> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("refills");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Notes).HasMaxLength(Refill.NotesMaxLength);
        builder.Property(r => r.VoidReason).HasMaxLength(Refill.VoidReasonMaxLength);

        builder.HasOne(r => r.Machine)
            .WithMany()
            .HasForeignKey(r => r.MachineId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(r => r.Items)
            .WithOne(item => item.Refill)
            .HasForeignKey(item => item.RefillId)
            .OnDelete(DeleteBehavior.Cascade);

        // "This machine's refills, latest first" - the history list and the level rebuild.
        builder.HasIndex(r => new { r.MachineId, r.RefilledAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_refills_machine_id_refilled_at");
    }
}

/// <summary>Maps <see cref="RefillItem"/> to the <c>refill_items</c> table.</summary>
/// <remarks>Layer: Persistence.</remarks>
public sealed class RefillItemConfiguration : IEntityTypeConfiguration<RefillItem>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RefillItem> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("refill_items", table =>
            table.HasCheckConstraint("ck_refill_items_capacity", $"capacity BETWEEN 1 AND {RefillItem.MaxCapacity}"));

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.ServiceCode).HasMaxLength(ServiceDefinition.CodeMaxLength).IsUnicode(false).IsRequired();

        builder.HasOne<ServiceDefinition>()
            .WithMany()
            .HasForeignKey(i => i.ServiceCode)
            .HasPrincipalKey(s => s.Code)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(i => new { i.RefillId, i.ServiceCode })
            .IsUnique()
            .HasDatabaseName("ix_refill_items_refill_id_service_code");
    }
}

/// <summary>Maps <see cref="MachineServiceState"/> to the <c>machine_service_states</c> table.</summary>
/// <remarks>Layer: Persistence. Not row-versioned: only the worker writes it.</remarks>
public sealed class MachineServiceStateConfiguration : IEntityTypeConfiguration<MachineServiceState>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MachineServiceState> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("machine_service_states");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).UseIdentityColumn();
        builder.Property(s => s.ServiceCode).HasMaxLength(ServiceDefinition.CodeMaxLength).IsUnicode(false).IsRequired();
        builder.Property(s => s.Level).HasConversion<byte>().IsRequired();
        builder.Property(s => s.AverageDailyUses).HasPrecision(9, 2);
        builder.Ignore(s => s.Remaining);

        builder.HasOne(s => s.Machine)
            .WithMany()
            .HasForeignKey(s => s.MachineId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ServiceDefinition>()
            .WithMany()
            .HasForeignKey(s => s.ServiceCode)
            .HasPrincipalKey(d => d.Code)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => new { s.MachineId, s.ServiceCode })
            .IsUnique()
            .HasDatabaseName("ix_machine_service_states_machine_id_service_code");

        // "Which machines need a refill" across the fleet.
        builder.HasIndex(s => s.Level).HasDatabaseName("ix_machine_service_states_level");
    }
}

/// <summary>Maps <see cref="StateRebuildRequest"/> to the <c>state_rebuild_requests</c> table.</summary>
/// <remarks>Layer: Persistence.</remarks>
public sealed class StateRebuildRequestConfiguration : IEntityTypeConfiguration<StateRebuildRequest>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<StateRebuildRequest> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("state_rebuild_requests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).UseIdentityColumn();
        builder.Property(r => r.Reason).HasConversion<byte>().IsRequired();
    }
}
