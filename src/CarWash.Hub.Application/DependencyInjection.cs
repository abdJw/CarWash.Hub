using CarWash.Hub.Application.Features.Ingestion;
using CarWash.Hub.Application.Features.Machines;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace CarWash.Hub.Application;

/// <summary>
/// Composition root for the Application layer.
/// </summary>
/// <remarks>
/// Layer: Application. One registration extension per layer keeps <c>Program.cs</c> short and
/// keeps each layer's wiring next to the code it wires.
/// </remarks>
public static class DependencyInjection
{
    /// <summary>
    /// Registers Application-layer services: feature services and validators.
    /// </summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <returns>The same <paramref name="services"/> instance, for chaining.</returns>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Feature services. Scoped: each uses the request's unit of work.
        services.AddScoped<IIngestionService, IngestionService>();
        services.AddScoped<IMachineService, MachineService>();

        // Validators discovered by assembly scan: adding one for a new request type requires no
        // registration here and no change to ValidationFilter.
        services.AddValidatorsFromAssembly(ApplicationAssemblyReference.Assembly, includeInternalTypes: false);

        return services;
    }
}
