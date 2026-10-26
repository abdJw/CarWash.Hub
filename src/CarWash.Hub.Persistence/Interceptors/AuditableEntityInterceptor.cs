using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CarWash.Hub.Persistence.Interceptors;

/// <summary>
/// Stamps audit columns and converts deletions of soft-deletable entities into flag updates.
/// </summary>
/// <remarks>
/// Layer: Persistence.
/// <para>
/// An interceptor rather than a <c>SaveChanges</c> override, so the behaviour applies to
/// every context instance including the one <c>dotnet ef</c> builds at design time, and so
/// it can be unit-tested in isolation.
/// </para>
/// <para>
/// If an audit-log interceptor is added later (Phase 2), it must run <em>after</em> this one: the
/// soft-delete conversion below turns a <c>Deleted</c> entry into a <c>Modified</c> one, and an
/// audit trail needs to observe the converted state to record the change as a soft delete.
/// </para>
/// <para>
/// In Phase 1 there are no user accounts, so the <c>*By</c> columns stay null; the timestamps are
/// always set.
/// </para>
/// </remarks>
/// <param name="currentUser">Supplies the acting user's identifier.</param>
/// <param name="dateTime">Supplies the current UTC instant.</param>
public sealed class AuditableEntityInterceptor(
    ICurrentUserService currentUser,
    IDateTimeProvider dateTime) : SaveChangesInterceptor
{
    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        ApplyStamps(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        ApplyStamps(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void ApplyStamps(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = dateTime.UtcNow;
        var userId = currentUser.UserId;

        // Soft delete is handled first and in its own pass, because converting an entry from
        // Deleted to Modified changes which branch of the stamping switch it falls into.
        foreach (var entry in context.ChangeTracker.Entries<ISoftDeletable>())
        {
            if (entry.State != EntityState.Deleted)
            {
                continue;
            }

            // The row is flagged, not removed. Cascade-deleted children of this entity are
            // left alone deliberately: EF has already marked them Deleted, and join rows
            // are meant to go for real.
            entry.State = EntityState.Modified;
            entry.Entity.IsDeleted = true;
            entry.Entity.DeletedAt = now;
            entry.Entity.DeletedBy = userId;
        }

        foreach (var entry in context.ChangeTracker.Entries<IAuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.CreatedBy = userId;
                    break;

                case EntityState.Modified:
                    entry.Entity.ModifiedAt = now;
                    entry.Entity.ModifiedBy = userId;

                    // Without this, updating any property would also rewrite CreatedAt and
                    // CreatedBy - the values loaded into the tracked entity are sent back
                    // whole unless explicitly marked unmodified. Detached-entity updates in
                    // particular would otherwise silently destroy creation provenance.
                    entry.Property(nameof(IAuditableEntity.CreatedAt)).IsModified = false;
                    entry.Property(nameof(IAuditableEntity.CreatedBy)).IsModified = false;
                    break;

                case EntityState.Detached:
                case EntityState.Unchanged:
                case EntityState.Deleted:
                default:
                    // Deleted entries reaching here are hard deletes of entities that are
                    // auditable but not soft-deletable; there is nothing to stamp on a row
                    // that is about to disappear.
                    break;
            }
        }
    }
}
