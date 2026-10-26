using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Infrastructure.Auth;
using CarWash.Hub.Infrastructure.Localization;
using CarWash.Hub.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CarWash.Hub.Infrastructure;

/// <summary>
/// Composition root for the Infrastructure layer.
/// </summary>
/// <remarks>Layer: Infrastructure.</remarks>
public static class DependencyInjection
{
    /// <summary>
    /// Registers Infrastructure-layer services: client-key authentication, localization, the
    /// current-user accessor, the clock and the machine access scope.
    /// </summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <param name="configuration">Application configuration, used to bind strongly-typed options.</param>
    /// <returns>The same <paramref name="services"/> instance, for chaining.</returns>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddHttpContextAccessor();

        // Scoped: both describe the current request.
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<IMachineAccessScopeProvider, AllStationsAccessScopeProvider>();

        // Singleton: stateless and thread-safe.
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();

        services.AddClientKeyAuthentication(configuration);
        services.AddConfiguredLocalization();

        return services;
    }
}
