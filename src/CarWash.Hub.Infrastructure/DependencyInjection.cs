using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Application.Common.Options;
using CarWash.Hub.Application.Features.Alerts;
using CarWash.Hub.Application.Features.Auth;
using CarWash.Hub.Application.Features.Dashboard;
using CarWash.Hub.Application.Features.DeviceSettings;
using CarWash.Hub.Application.Features.Processing;
using CarWash.Hub.Infrastructure.Auth;
using CarWash.Hub.Infrastructure.Localization;
using CarWash.Hub.Infrastructure.Processing;
using CarWash.Hub.Infrastructure.Services;
using CarWash.Hub.Infrastructure.Workers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace CarWash.Hub.Infrastructure;

/// <summary>
/// Composition root for the Infrastructure layer.
/// </summary>
/// <remarks>Layer: Infrastructure.</remarks>
public static class DependencyInjection
{
    /// <summary>
    /// Registers Infrastructure-layer services: client-key authentication, localization, the
    /// current-user accessor, the clock, the machine access scope, password hashing, the business
    /// time zone, Data Protection and the background workers.
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

        // Scoped: all three describe the current request, or the current dashboard operation.
        services.AddScoped<CurrentPrincipalHolder>();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<IMachineAccessScopeProvider, UserMachineAccessScopeProvider>();

        // Singletons: stateless and thread-safe.
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddSingleton<IPasswordHasher, PasswordHasherAdapter>();
        services.AddSingleton<IBusinessTimeZone, BusinessTimeZone>();

        services.AddOptions<DashboardOptions>()
            .Bind(configuration.GetSection(DashboardOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<SignInOptions>()
            .Bind(configuration.GetSection(SignInOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ActivityOptions>()
            .Bind(configuration.GetSection(ActivityOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<AlertOptions>()
            .Bind(configuration.GetSection(AlertOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // No dashboard in this host until one registers itself; the worker publishes regardless.
        services.TryAddSingleton<IDashboardEventPublisher, NullDashboardEventPublisher>();

        AddDataProtection(services);

        services.AddClientKeyAuthentication(configuration);
        services.AddConfiguredLocalization();

        AddBackgroundProcessing(services, configuration);

        return services;
    }

    // The dashboard cookie and antiforgery tokens are encrypted with these keys. Kept in the
    // database: on shared IIS the default in-memory ring is lost on every application pool recycle,
    // which would sign everyone out several times a day. The application name keeps keys from being
    // shared with any other application on the same database by accident.
    private static void AddDataProtection(IServiceCollection services)
    {
        services.AddDataProtection().SetApplicationName("CarWash.Hub");

        services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(provider =>
            new ConfigureOptions<KeyManagementOptions>(options =>
                options.XmlRepository = new DatabaseXmlRepository(provider.GetRequiredService<IServiceScopeFactory>())));
    }

    private static void AddBackgroundProcessing(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ProcessingOptions>()
            .Bind(configuration.GetSection(ProcessingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<BackgroundWorkerOptions>()
            .Bind(configuration.GetSection(BackgroundWorkerOptions.SectionName));

        services.AddOptions<DeviceSettingsOptions>()
            .Bind(configuration.GetSection(DeviceSettingsOptions.SectionName))
            .Validate(options => options.ReconcileIntervalSeconds is >= 5 and <= 3600, "DeviceSettings:ReconcileIntervalSeconds must be from 5 to 3600.")
            .ValidateOnStart();

        // Singletons shared by ingestion (which signals) and the worker (which waits and reports).
        services.AddSingleton<ChannelProcessingTrigger>();
        services.AddSingleton<IMessageProcessingTrigger>(provider => provider.GetRequiredService<ChannelProcessingTrigger>());
        services.AddSingleton<IDeviceSettingsSignal, DeviceSettingsSignal>();
        services.AddSingleton<ProcessingMonitor>();
        services.AddSingleton<IProcessingMonitor>(provider => provider.GetRequiredService<ProcessingMonitor>());

        // Always registered; it checks BackgroundWorkers:Enabled when it starts, once the host's
        // configuration - a test host's overrides included - is complete.
        services.AddHostedService<MachineStateWorker>();
    }
}
