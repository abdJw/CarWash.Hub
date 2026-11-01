using CarWash.Hub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarWash.Hub.Persistence.Configurations;

/// <summary>Maps <see cref="User"/> to the <c>users</c> table.</summary>
/// <remarks>Layer: Persistence. The rowversion comes from the <c>IHasRowVersion</c> convention.</remarks>
public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<User> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // The role and the station travel together: a station user has exactly one station, a
        // Super Admin none. Held in the database, so no code path can create a station user who
        // would see every station.
        builder.ToTable("users", table =>
        {
            table.HasCheckConstraint("ck_users_role", "role IN (1, 2)");
            table.HasCheckConstraint(
                "ck_users_station_matches_role",
                "(role = 2 AND gas_station_id IS NOT NULL) OR (role = 1 AND gas_station_id IS NULL)");
        });

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();

        builder.Property(u => u.UserName).HasMaxLength(User.UserNameMaxLength).IsRequired();
        builder.Property(u => u.Email).HasMaxLength(User.EmailMaxLength).IsRequired();
        builder.Property(u => u.DisplayName).HasMaxLength(User.DisplayNameMaxLength).IsRequired();
        builder.Property(u => u.PasswordHash).HasMaxLength(256).IsUnicode(false).IsRequired();
        builder.Property(u => u.Role).HasConversion<byte>().IsRequired();
        builder.Property(u => u.IsActive).HasDefaultValue(true).IsRequired();

        builder.HasOne(u => u.GasStation)
            .WithMany()
            .HasForeignKey(u => u.GasStationId)
            .OnDelete(DeleteBehavior.Restrict);

        // Unique including deleted users, like every unique index here; case-insensitive through
        // the collation, so "Admin" and "admin" are one name.
        builder.HasIndex(u => u.UserName).IsUnique().HasDatabaseName("ix_users_user_name");
        builder.HasIndex(u => u.Email).IsUnique().HasDatabaseName("ix_users_email");
        builder.HasIndex(u => u.GasStationId).HasDatabaseName("ix_users_gas_station_id");
    }
}
