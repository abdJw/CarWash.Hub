using CarWash.Hub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarWash.Hub.Persistence.Configurations;

/// <summary>Maps <see cref="MachineState"/> to the <c>machine_states</c> table.</summary>
/// <remarks>
/// Layer: Persistence. One-to-one with <c>machines</c>, sharing its key. Deliberately not
/// row-versioned: the worker writes it on every batch, and nothing else edits it.
/// </remarks>
public sealed class MachineStateConfiguration : IEntityTypeConfiguration<MachineState>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MachineState> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("machine_states");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.LastServiceCode).HasMaxLength(ServiceDefinition.CodeMaxLength).IsUnicode(false);

        builder.HasOne(s => s.Machine)
            .WithOne(m => m.State)
            .HasForeignKey<MachineState>(s => s.Id)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
