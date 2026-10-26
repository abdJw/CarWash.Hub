namespace CarWash.Hub.Domain.Common;

/// <summary>
/// Marks an entity whose creation and modification are stamped automatically.
/// </summary>
/// <remarks>
/// Layer: Domain.
/// <para>
/// <c>AuditableEntityInterceptor</c> in the Persistence layer fills these properties on
/// <c>SaveChanges</c> from <c>ICurrentUserService</c>. Nothing in application code should assign
/// them by hand. In Phase 1 there are no user accounts, so the <c>*By</c> columns stay null; the
/// Phase 2 dashboard's sign-in fills them.
/// </para>
/// </remarks>
public interface IAuditableEntity
{
    /// <summary>Gets or sets when the row was created, in UTC.</summary>
    DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets the identifier of the user who created the row, if any.</summary>
    Guid? CreatedBy { get; set; }

    /// <summary>Gets or sets when the row was last modified, in UTC.</summary>
    DateTimeOffset? ModifiedAt { get; set; }

    /// <summary>Gets or sets the identifier of the user who last modified the row, if any.</summary>
    Guid? ModifiedBy { get; set; }
}
