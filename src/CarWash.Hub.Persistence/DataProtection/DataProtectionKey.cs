using CarWash.Hub.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CarWash.Hub.Persistence.DataProtection;

/// <summary>One ASP.NET Core Data Protection key, as XML.</summary>
/// <remarks>
/// Layer: Persistence. Infrastructure plumbing rather than a business entity, so it lives here and
/// not in Domain. Rows are only ever added; Data Protection itself decides which keys are current.
/// </remarks>
public sealed class DataProtectionKey : BaseEntity<int>
{
    /// <summary>Gets or sets the key's name, as Data Protection gives it.</summary>
    public string? FriendlyName { get; set; }

    /// <summary>Gets or sets the key's XML.</summary>
    public string Xml { get; set; } = string.Empty;
}

/// <summary>Maps <see cref="DataProtectionKey"/> to the <c>data_protection_keys</c> table.</summary>
/// <remarks>Layer: Persistence.</remarks>
public sealed class DataProtectionKeyConfiguration : IEntityTypeConfiguration<DataProtectionKey>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<DataProtectionKey> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("data_protection_keys");
        builder.HasKey(k => k.Id);
        builder.Property(k => k.Id).UseIdentityColumn();
        builder.Property(k => k.FriendlyName).HasMaxLength(200);
        builder.Property(k => k.Xml).HasColumnType("nvarchar(max)").IsRequired();
    }
}
