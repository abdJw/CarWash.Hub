namespace CarWash.Hub.Domain.Common;

/// <summary>
/// Base type for entities that are audit-stamped and soft-deletable.
/// </summary>
/// <typeparam name="TKey">The primary key type.</typeparam>
/// <remarks>
/// Layer: Domain.
/// <para>
/// Deriving from this type is all an entity needs to do to gain automatic created/modified
/// stamping and participation in the soft-delete query filter. Both are driven by the interfaces
/// below, discovered by convention in the Persistence layer.
/// </para>
/// </remarks>
public abstract class AuditableEntity<TKey> : BaseEntity<TKey>, IAuditableEntity, ISoftDeletable
    where TKey : IEquatable<TKey>
{
    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAt { get; set; }

    /// <inheritdoc />
    public Guid? ModifiedBy { get; set; }

    /// <inheritdoc />
    public bool IsDeleted { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? DeletedAt { get; set; }

    /// <inheritdoc />
    public Guid? DeletedBy { get; set; }
}
