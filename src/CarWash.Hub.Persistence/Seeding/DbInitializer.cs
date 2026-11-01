using CarWash.Hub.Application.Abstractions.Persistence;
using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Domain.Entities;
using CarWash.Hub.Domain.Enums;
using CarWash.Hub.Persistence.Context;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CarWash.Hub.Persistence.Seeding;

/// <summary>
/// Applies pending migrations at startup when configured to, then inserts missing reference data.
/// </summary>
/// <remarks>
/// Layer: Persistence. Idempotent: safe to run on every start.
/// <para>
/// <b>Seeding never stops the application.</b> A database that is unreachable or behind on
/// migrations at startup is logged and skipped, not thrown: the readiness probe already reports an
/// unreachable database, and the seed runs again on the next start. A crash loop on shared hosting
/// would only make the outage longer.
/// </para>
/// </remarks>
/// <param name="context">The database context.</param>
/// <param name="options">Startup configuration.</param>
/// <param name="passwordHasher">Hashes the first Super Admin's password.</param>
/// <param name="logger">Logger.</param>
public sealed class DbInitializer(
    ApplicationDbContext context,
    IOptions<SeedOptions> options,
    IPasswordHasher passwordHasher,
    ILogger<DbInitializer> logger) : IDbInitializer
{
    // The services the machines are known to send today. Refill tracking is on for all three: the
    // owner refills soap and water tanks and empties the vacuum. Colours are left to the dashboard's
    // palette until the owner picks one.
    private static readonly ServiceDefinition[] SeedServices =
    [
        new() { Code = "soap", Name = "Soap", NameAr = "صابون", Icon = "soap", TracksRefill = true, SortOrder = 10 },
        new() { Code = "water", Name = "Water", NameAr = "ماء", Icon = "water", TracksRefill = true, SortOrder = 20 },
        new() { Code = "vacuum", Name = "Vacuum", NameAr = "مكنسة كهربائية", Icon = "vacuum", TracksRefill = true, SortOrder = 30 },
    ];

    private readonly SeedOptions _options = options.Value;

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_options.ApplyMigrations)
        {
            var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).ToList();
            if (pending.Count > 0)
            {
                logger.LogInformation("Applying {Count} pending migration(s): {Migrations}", pending.Count, string.Join(", ", pending));
                await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        if (_options.SeedData)
        {
            await SeedAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SeedAsync(CancellationToken cancellationToken)
    {
        try
        {
            var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).ToList();
            if (pending.Count > 0)
            {
                logger.LogWarning(
                    "Skipping reference data: {Count} migration(s) are not applied ({Migrations}). Apply them, then restart",
                    pending.Count, string.Join(", ", pending));
                return;
            }

            await SeedServicesAsync(cancellationToken).ConfigureAwait(false);
            await SeedAdminAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Reference data could not be seeded; it is retried on the next start");
        }
        finally
        {
            context.ChangeTracker.Clear();
        }
    }

    // The first Super Admin, so someone can sign in to a fresh installation. Only while no Super
    // Admin exists - deleted ones included - so it never recreates an account the owner removed.
    private async Task SeedAdminAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.AdminUserName) || string.IsNullOrEmpty(_options.AdminPassword))
        {
            return;
        }

        var exists = await context.Users
            .IgnoreQueryFilters()
            .AnyAsync(user => user.Role == UserRole.SuperAdmin, cancellationToken)
            .ConfigureAwait(false);

        if (exists)
        {
            return;
        }

        var userName = _options.AdminUserName.Trim();
        context.Users.Add(new User
        {
            Id = Guid.CreateVersion7(),
            UserName = userName,
            Email = string.IsNullOrWhiteSpace(_options.AdminEmail) ? $"{userName}@localhost" : _options.AdminEmail.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(_options.AdminDisplayName) ? userName : _options.AdminDisplayName.Trim(),
            PasswordHash = passwordHasher.Hash(_options.AdminPassword),
            Role = UserRole.SuperAdmin,
            IsActive = true,
            SecurityStamp = Guid.NewGuid(),
        });

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            logger.LogWarning("Created the first Super Admin '{UserName}'. Remove Seed:AdminPassword from the configuration now", userName);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            logger.LogInformation("The first Super Admin was created concurrently");
        }
    }

    private async Task SeedServicesAsync(CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters: a service the owner retired stays retired rather than being re-added.
        var existing = await context.ServiceDefinitions
            .IgnoreQueryFilters()
            .Select(service => service.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var missing = SeedServices
            .Where(seed => !existing.Contains(seed.Code, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (missing.Count == 0)
        {
            return;
        }

        foreach (var seed in missing)
        {
            context.ServiceDefinitions.Add(new ServiceDefinition
            {
                Id = Guid.CreateVersion7(),
                Code = seed.Code,
                Name = seed.Name,
                NameAr = seed.NameAr,
                Icon = seed.Icon,
                TracksRefill = seed.TracksRefill,
                SortOrder = seed.SortOrder,
                IsConfigured = true,
            });
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Seeded service(s) {Codes}", string.Join(", ", missing.Select(seed => seed.Code)));
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Another process - or the worker registering a code it just saw - got there first.
            logger.LogInformation("Service catalog was seeded concurrently");
        }
    }
}
