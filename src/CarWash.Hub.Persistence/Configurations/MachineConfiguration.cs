using CarWash.Hub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarWash.Hub.Persistence.Configurations;

/// <summary>Maps <see cref="Machine"/> to the <c>machines</c> table.</summary>
/// <remarks>Layer: Persistence. The rowversion comes from the <c>IHasRowVersion</c> convention.</remarks>
public sealed class MachineConfiguration : IEntityTypeConfiguration<Machine>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Machine> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("machines");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.Code).HasMaxLength(Machine.CodeMaxLength).IsRequired();
        builder.Property(m => m.DisplayName).HasMaxLength(200);
        builder.Property(m => m.IsActive).HasDefaultValue(true).IsRequired();

        // Optional until the owner assigns the machine in Phase 2. Restrict: a station with
        // machines cannot be deleted out from under them.
        builder.HasOne(m => m.GasStation)
            .WithMany(s => s.Machines)
            .HasForeignKey(m => m.GasStationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(m => m.Messages)
            .WithOne(message => message.Machine)
            .HasForeignKey(message => message.MachineId)
            .OnDelete(DeleteBehavior.Restrict);

        // Unique and unfiltered: a soft-deleted machine keeps its id reserved, and the
        // registration race in MachineRepository depends on this index to pick one winner.
        builder.HasIndex(m => m.Code).IsUnique().HasDatabaseName("ix_machines_code");
        builder.HasIndex(m => m.GasStationId).HasDatabaseName("ix_machines_gas_station_id");
    }
}
