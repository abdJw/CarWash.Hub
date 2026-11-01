using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Application.Common.Specifications;
using CarWash.Hub.Domain.Entities;
using CarWash.Hub.Domain.Enums;

namespace CarWash.Hub.Application.Features.Dashboard;

/// <summary>The machines a dashboard caller may see, or one of them.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed class DashboardMachinesSpecification : BaseSpecification<Machine>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="scope">The machines the caller may see.</param>
    /// <param name="machineId">One machine's key, or null for all of them.</param>
    public DashboardMachinesSpecification(MachineAccessScope scope, Guid? machineId = null)
    {
        ArgumentNullException.ThrowIfNull(scope);

        if (scope.IsRestricted)
        {
            AddCriteria(machine => machine.GasStationId == scope.GasStationId);
        }

        if (machineId is { } id)
        {
            AddCriteria(machine => machine.Id == id);
        }

        AddOrderBy(machine => machine.DisplayName ?? machine.Code);
        AddOrderBy(machine => machine.Id);
    }
}

/// <summary>Hourly usage rows inside the caller's scope, within a range of hours.</summary>
/// <remarks>
/// Layer: Application. Grouped in the database by the dashboard service, so a 30-day fleet chart
/// reads one row per hour and service, not one per machine. Served by
/// <c>ix_service_usage_hourly_hour_start</c>, or the clustered index for a single machine.
/// </remarks>
public sealed class ScopedHourlyUsageSpecification : BaseSpecification<ServiceUsageHourly>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="scope">The machines the caller may see.</param>
    /// <param name="fromHour">The first hour, inclusive.</param>
    /// <param name="toHour">The last hour, inclusive.</param>
    /// <param name="machineId">One machine's key, or null for every machine in scope.</param>
    public ScopedHourlyUsageSpecification(MachineAccessScope scope, DateTimeOffset fromHour, DateTimeOffset toHour, Guid? machineId = null)
        : base(row => row.HourStart >= fromHour && row.HourStart <= toHour)
    {
        ArgumentNullException.ThrowIfNull(scope);

        if (scope.IsRestricted)
        {
            AddCriteria(row => row.Machine.GasStationId == scope.GasStationId);
        }

        if (machineId is { } id)
        {
            AddCriteria(row => row.MachineId == id);
        }
    }
}

/// <summary>A machine's latest uses, newest first.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed class RecentUsesSpecification : BaseSpecification<ServiceUsage>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="machineId">The machine's key.</param>
    /// <param name="take">How many.</param>
    public RecentUsesSpecification(Guid machineId, int take)
        : base(usage => usage.MachineId == machineId)
    {
        AddOrderBy(usage => usage.OccurredAt, SortDirection.Descending);
        AddOrderBy(usage => usage.Id, SortDirection.Descending);
        ApplyPaging(0, take);
    }
}

/// <summary>The services the dashboard shows, in display order.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed class ActiveServicesSpecification : BaseSpecification<ServiceDefinition>
{
    /// <summary>Builds the specification.</summary>
    public ActiveServicesSpecification()
        : base(service => service.IsActive)
    {
        AddOrderBy(service => service.SortOrder);
        AddOrderBy(service => service.Code);
    }
}
