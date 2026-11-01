using CarWash.Hub.Application.Features.Alerts;
using CarWash.Hub.Application.Features.Auth;
using CarWash.Hub.Application.Features.Catalog;
using CarWash.Hub.Application.Features.Dashboard;
using CarWash.Hub.Application.Features.Ingestion;
using CarWash.Hub.Application.Features.Levels;
using CarWash.Hub.Application.Features.Machines;
using CarWash.Hub.Application.Features.Processing;
using CarWash.Hub.Application.Features.Refills;
using CarWash.Hub.Application.Features.Stations;
using CarWash.Hub.Application.Features.Usage;
using CarWash.Hub.Application.Features.Users;
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
        services.AddScoped<IUsageService, UsageService>();
        services.AddScoped<IProcessingStatusService, ProcessingStatusService>();
        services.AddScoped<ISignInService, SignInService>();
        services.AddScoped<IFleetDashboardService, FleetDashboardService>();
        services.AddScoped<IRefillService, RefillService>();

        // Administration: Super Admins only, checked inside each service.
        services.AddScoped<IGasStationService, GasStationService>();
        services.AddScoped<IMachineAdminService, MachineAdminService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IServiceCatalogService, ServiceCatalogService>();

        // Message processing. The parser is pure and stateless; the processor is scoped because
        // the worker runs each batch in a scope of its own, with its own unit of work.
        services.AddSingleton<IMachineMessageParser, Esp32ServiceEventParser>();
        services.AddScoped<IMachineStateProcessor, MachineStateProcessor>();
        services.AddScoped<IServiceLevelUpdater, ServiceLevelUpdater>();
        services.AddScoped<IAlertEvaluator, AlertEvaluator>();
        services.AddScoped<IAlertService, AlertService>();

        // Validators discovered by assembly scan: adding one for a new request type requires no
        // registration here and no change to ValidationFilter.
        services.AddValidatorsFromAssembly(ApplicationAssemblyReference.Assembly, includeInternalTypes: false);

        return services;
    }
}
