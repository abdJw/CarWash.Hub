namespace CarWash.Hub.Domain.Common;

/// <summary>
/// Marks an entity as soft-deletable: rows are flagged rather than physically removed.
/// </summary>
/// <remarks>
/// Layer: Domain.
/// <para>
/// The DbContext applies a global query filter (<c>e =&gt; !e.IsDeleted</c>) to every entity type
/// implementing this interface, by convention. Implementing it is therefore the <em>only</em>
/// thing a new entity must do to opt into soft delete.
/// </para>
/// <para>
/// To read deleted rows deliberately, call <c>IncludeSoftDeleted()</c> on the specification
/// passed to the repository.
/// </para>
/// </remarks>
public interface ISoftDeletable
{
    /// <summary>Gets or sets a value indicating whether the row is soft-deleted.</summary>
    bool IsDeleted { get; set; }

    /// <summary>Gets or sets when the row was soft-deleted, in UTC.</summary>
    DateTimeOffset? DeletedAt { get; set; }

    /// <summary>Gets or sets the identifier of the user who soft-deleted the row.</summary>
    Guid? DeletedBy { get; set; }
}
