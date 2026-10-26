using CarWash.Hub.Application.Abstractions.Persistence;
using CarWash.Hub.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CarWash.Hub.Persistence.Seeding;

/// <summary>
/// Applies pending migrations at startup when configured to.
/// </summary>
/// <remarks>
/// Layer: Persistence. Idempotent: safe to run on every start. Phase 2 adds the seeding of the
/// first Super Admin here, as a reconciliation step that inserts only what is missing.
/// </remarks>
/// <param name="context">The database context.</param>
/// <param name="options">Startup configuration.</param>
/// <param name="logger">Logger.</param>
public sealed class DbInitializer(
    ApplicationDbContext context,
    IOptions<SeedOptions> options,
    ILogger<DbInitializer> logger) : IDbInitializer
{
    private readonly SeedOptions _options = options.Value;

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.ApplyMigrations)
        {
            return;
        }

        var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).ToList();
        if (pending.Count == 0)
        {
            return;
        }

        logger.LogInformation("Applying {Count} pending migration(s): {Migrations}", pending.Count, string.Join(", ", pending));
        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }
}
