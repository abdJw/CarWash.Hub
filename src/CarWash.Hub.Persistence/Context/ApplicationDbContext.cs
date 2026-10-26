using System.Linq.Expressions;
using CarWash.Hub.Domain.Common;
using CarWash.Hub.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CarWash.Hub.Persistence.Context;

/// <summary>
/// The application's EF Core context.
/// </summary>
/// <remarks>
/// Layer: Persistence.
/// <para>
/// <see cref="OnModelCreating"/> contains no per-entity fluent configuration. Entity mapping
/// lives in <c>IEntityTypeConfiguration&lt;T&gt;</c> classes discovered by assembly scan, so adding
/// an entity - the Phase 2 users and status logs - never means editing this file.
/// </para>
/// <para>
/// What it does contain is <em>conventions</em> - the database collation, the two halves of the
/// soft-delete filter and the rowversion token - applied uniformly to every entity that qualifies.
/// </para>
/// </remarks>
/// <param name="options">The context options.</param>
public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    /// <summary>The database collation set by the initial migration.</summary>
    public const string DatabaseCollation = "Arabic_100_CI_AS_SC_UTF8";

    /// <summary>Gets the gas stations.</summary>
    public DbSet<GasStation> GasStations => Set<GasStation>();

    /// <summary>Gets the machines.</summary>
    public DbSet<Machine> Machines => Set<Machine>();

    /// <summary>Gets the raw machine messages.</summary>
    public DbSet<MachineMessage> MachineMessages => Set<MachineMessage>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);

        // A case-insensitive collation gives case-insensitive equality, uniqueness and LIKE on
        // every text column - a machine id sent as "cw-01" finds "CW-01" - without a second
        // "normalised" column to keep in step. The _SC_UTF8 variant sorts Arabic correctly
        // (station names) and handles supplementary characters. Requires SQL Server 2019+.
        modelBuilder.UseCollation(DatabaseCollation);

        // Every IEntityTypeConfiguration<T> in this assembly, applied automatically.
        modelBuilder.ApplyConfigurationsFromAssembly(PersistenceAssemblyReference.Assembly);

        // All three must run after ApplyConfigurationsFromAssembly: the entity types and their
        // relationships have to exist in the model before conventions can be attached.
        ApplySoftDeleteQueryFilters(modelBuilder);
        ApplyDependentSoftDeleteQueryFilters(modelBuilder);
        ApplyRowVersionConvention(modelBuilder);
    }

    /// <summary>
    /// Maps <see cref="IHasRowVersion.RowVersion"/> to a SQL Server <c>rowversion</c> column.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    /// <remarks>
    /// Implementing <see cref="IHasRowVersion"/> is the whole cost of optimistic concurrency for an
    /// aggregate. A save that lost the race throws <c>DbUpdateConcurrencyException</c>, which the
    /// unit of work turns into a 409.
    /// </remarks>
    private static void ApplyRowVersionConvention(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (entityType.IsOwned() || !typeof(IHasRowVersion).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            modelBuilder.Entity(entityType.ClrType)
                .Property(nameof(IHasRowVersion.RowVersion))
                .IsRowVersion();
        }
    }

    /// <summary>
    /// Adds <c>e =&gt; !e.IsDeleted</c> to every entity implementing <see cref="ISoftDeletable"/>.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    /// <remarks>
    /// Built as an expression tree, because the entity type is only known at runtime. Implementing
    /// <see cref="ISoftDeletable"/> is the entire cost of opting a new entity into soft delete.
    /// To read deleted rows, call <c>IncludeSoftDeleted()</c> on the specification.
    /// </remarks>
    private static void ApplySoftDeleteQueryFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (entityType.IsOwned() || !typeof(ISoftDeletable).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var isDeleted = Expression.Property(parameter, nameof(ISoftDeletable.IsDeleted));

            modelBuilder.Entity(entityType.ClrType)
                .HasQueryFilter(Expression.Lambda(Expression.Not(isDeleted), parameter));
        }
    }

    /// <summary>
    /// Filters out dependents whose required soft-deletable principal has been deleted.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    /// <remarks>
    /// <para>
    /// A message's machine is required and soft-deletable. Without this, the messages of a
    /// soft-deleted machine would still be listed, and EF Core would warn
    /// (<c>PossibleIncorrectRequiredNavigationWithQueryFilterInteraction</c>) that the required
    /// navigation and the filter disagree. Its remedy - matching filters on both ends - is what this
    /// applies, by convention, so a new child entity inherits it with no edit here.
    /// </para>
    /// <para>
    /// The generated filter only navigates <em>towards</em> principals whose own filter does not
    /// navigate anywhere, so there is no possibility of a filter cycle.
    /// </para>
    /// </remarks>
    private static void ApplyDependentSoftDeleteQueryFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            // Entities that are themselves soft-deletable already carry their own filter.
            if (entityType.IsOwned() || typeof(ISoftDeletable).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            var softDeletablePrincipals = entityType.GetForeignKeys()
                .Where(fk => fk.IsRequired
                             && fk.DependentToPrincipal is not null
                             && typeof(ISoftDeletable).IsAssignableFrom(fk.PrincipalEntityType.ClrType))
                .Select(fk => fk.DependentToPrincipal!.Name)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (softDeletablePrincipals.Count == 0)
            {
                continue;
            }

            var parameter = Expression.Parameter(entityType.ClrType, "e");
            Expression? body = null;

            foreach (var navigationName in softDeletablePrincipals)
            {
                var navigation = Expression.Property(parameter, navigationName);
                var isDeleted = Expression.Property(navigation, nameof(ISoftDeletable.IsDeleted));
                var notDeleted = Expression.Not(isDeleted);

                body = body is null ? notDeleted : Expression.AndAlso(body, notDeleted);
            }

            modelBuilder.Entity(entityType.ClrType)
                .HasQueryFilter(Expression.Lambda(body!, parameter));
        }
    }
}
