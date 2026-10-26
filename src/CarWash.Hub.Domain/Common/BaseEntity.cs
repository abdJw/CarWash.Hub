namespace CarWash.Hub.Domain.Common;

/// <summary>
/// Base type for every persisted entity, carrying only its identity.
/// </summary>
/// <typeparam name="TKey">The primary key type.</typeparam>
/// <remarks>Layer: Domain.</remarks>
public abstract class BaseEntity<TKey> : IEntity
    where TKey : IEquatable<TKey>
{
    /// <summary>Gets or sets the primary key.</summary>
    public TKey Id { get; set; } = default!;

    /// <inheritdoc />
    object IEntity.GetKeyValue() => Id!;
}

/// <summary>
/// Non-generic marker for entities, allowing cross-cutting components to work with an entity
/// without knowing its key type.
/// </summary>
/// <remarks>Layer: Domain.</remarks>
public interface IEntity
{
    /// <summary>Returns the primary key value boxed as <see cref="object"/>.</summary>
    /// <returns>The entity's key.</returns>
    object GetKeyValue();
}
