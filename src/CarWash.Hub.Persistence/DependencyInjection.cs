using CarWash.Hub.Application.Abstractions.Persistence;
using CarWash.Hub.Persistence.Context;
using CarWash.Hub.Persistence.Interceptors;
using CarWash.Hub.Persistence.Repositories;
using CarWash.Hub.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CarWash.Hub.Persistence;

/// <summary>
/// Composition root for the Persistence layer.
/// </summary>
/// <remarks>
/// Layer: Persistence. This is the single registration point the database provider is chosen at,
/// and the only place the Api project is permitted to touch a Persistence type.
/// </remarks>
public static class DependencyInjection
{
    /// <summary>The configuration key holding the database connection string.</summary>
    public const string ConnectionStringName = "Default";

    /// <summary>
    /// Registers the <c>DbContext</c>, repositories, unit of work, interceptor and startup migration.
    /// </summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The same <paramref name="services"/> instance, for chaining.</returns>
    /// <exception cref="InvalidOperationException">The connection string is not configured.</exception>
    public static IServiceCollection AddPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Read at scope creation, not here. Configuration read during registration captures a
        // snapshot: a source added afterwards - a test host's overrides, a reloaded secret - would
        // be invisible to the DbContext while visible to everything using IOptions.
        static string ResolveConnectionString(IServiceProvider serviceProvider) =>
            serviceProvider.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is not configured. " +
                "Set ConnectionStrings:Default in appsettings or ConnectionStrings__Default in the environment.");

        services.AddOptions<SeedOptions>()
            .Bind(configuration.GetSection(SeedOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Resolved per scope so it can see the current caller.
        services.AddScoped<AuditableEntityInterceptor>();

        services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
        {
            // ----------------------------------------------------------------------------
            // THE PROVIDER SWAP POINT. Changing databases means changing this call and the
            // provider package in CarWash.Hub.Persistence.csproj - nothing else in the
            // solution refers to a provider-specific type.
            // ----------------------------------------------------------------------------
            options.UseSqlServer(ResolveConnectionString(serviceProvider), sql =>
            {
                sql.MigrationsAssembly(PersistenceAssemblyReference.Assembly.FullName);

                // Transient faults - a container still starting, a failover, a dropped connection -
                // are retried rather than surfacing as a 500. This makes plain user-initiated
                // transactions illegal; IUnitOfWork.ExecuteInTransactionAsync is the supported way.
                sql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null);

                // SQL Server 2019 is the floor. Pinning the compatibility level makes EF emit only
                // SQL that level 150 accepts, even when development runs on SQL Server 2022.
                sql.UseCompatibilityLevel(150);
            });

            // One predictable naming style for hand-written SQL - analysis queries over the raw
            // payloads in particular: MachineMessage.ReceivedAt maps to machine_messages.received_at.
            options.UseSnakeCaseNamingConvention();

            options.AddInterceptors(serviceProvider.GetRequiredService<AuditableEntityInterceptor>());
        });

        // The code-to-key mapping of known machines. AddMemoryCache is idempotent.
        services.AddMemoryCache();

        services.AddScoped<IMachineRepository, MachineRepository>();

        // Open generic registration: a new entity gets a working repository with no registration
        // of its own.
        services.AddScoped(typeof(IGenericRepository<,>), typeof(GenericRepository<,>));

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IDbInitializer, DbInitializer>();

        return services;
    }

    /// <summary>
    /// Runs startup migration through the <see cref="IDbInitializer"/> abstraction.
    /// </summary>
    /// <param name="serviceProvider">The root service provider.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the database is ready.</returns>
    /// <remarks>
    /// Called from <c>Program.cs</c>. Creates its own scope because initialisation runs before the
    /// first request, when no request scope exists.
    /// </remarks>
    public static async Task InitializeDatabaseAsync(
        this IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        await using var scope = serviceProvider.CreateAsyncScope();
        var initializer = scope.ServiceProvider.GetRequiredService<IDbInitializer>();
        await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
    }
}
