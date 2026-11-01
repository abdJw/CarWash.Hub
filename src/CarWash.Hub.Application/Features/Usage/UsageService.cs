using CarWash.Hub.Application.Abstractions.Persistence;
using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Application.Common.Constants;
using CarWash.Hub.Application.Common.Specifications;
using CarWash.Hub.Application.Exceptions;
using CarWash.Hub.Application.Features.Machines;
using CarWash.Hub.Application.Features.Processing;
using CarWash.Hub.Domain.Entities;

namespace CarWash.Hub.Application.Features.Usage;

/// <summary>Reads how machines are used, from the parsed usage history.</summary>
/// <remarks>Layer: Application. Every read is limited to the caller's <see cref="MachineAccessScope"/>.</remarks>
public interface IUsageService
{
    /// <summary>Returns a machine's usage over a window.</summary>
    /// <param name="machineId">The identifier the machine sends; case-insensitive.</param>
    /// <param name="query">The window.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Totals per service and counts per hour.</returns>
    /// <exception cref="NotFoundException">No such machine in the caller's scope.</exception>
    Task<MachineUsageDto> GetMachineUsageAsync(string machineId, UsageQuery query, CancellationToken cancellationToken = default);
}

/// <summary>The default <see cref="IUsageService"/>.</summary>
/// <remarks>Layer: Application. Reads the hourly rollup, never the individual uses.</remarks>
/// <param name="unitOfWork">Data access.</param>
/// <param name="scopeProvider">The machines the caller may see.</param>
/// <param name="clock">The end of a window that names none.</param>
/// <param name="localizer">Names things in "not found" messages.</param>
public sealed class UsageService(
    IUnitOfWork unitOfWork,
    IMachineAccessScopeProvider scopeProvider,
    IDateTimeProvider clock,
    ILocalizationService localizer) : IUsageService
{
    /// <inheritdoc />
    public async Task<MachineUsageDto> GetMachineUsageAsync(
        string machineId,
        UsageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var machines = await unitOfWork.Machines
            .ListAsync(
                new MachineByCodeSpecification(machineId, scopeProvider.GetScope()),
                machine => new MachineRegistration(machine.Id, machine.Code),
                cancellationToken)
            .ConfigureAwait(false);

        var machine = machines.Count > 0
            ? machines[0]
            : throw NotFoundException.For(localizer.GetString(ResourceKeys.Fields.Machine), machineId);

        var (from, to) = query.Resolve(clock.UtcNow);

        var hourly = await unitOfWork.Repository<ServiceUsageHourly, long>()
            .ListAsync(
                new MachineHourlyUsageSpecification(machine.Id, OccurrenceTimeResolver.HourStart(from), to),
                row => new HourlyUsageDto(row.HourStart, row.ServiceCode, row.UsageCount),
                cancellationToken)
            .ConfigureAwait(false);

        var totals = hourly
            .GroupBy(row => row.Service, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ServiceUsageTotalDto(group.Key, group.Sum(row => row.Count)))
            .OrderByDescending(total => total.Count)
            .ThenBy(total => total.Service, StringComparer.Ordinal)
            .ToList();

        return new MachineUsageDto(machine.Code, from, to, totals, hourly);
    }
}

/// <summary>One machine's hourly usage rows within a range of hours, oldest first.</summary>
/// <remarks>Layer: Application. A range seek on the clustered <c>(machine_id, hour_start, service_code)</c> index.</remarks>
public sealed class MachineHourlyUsageSpecification : BaseSpecification<ServiceUsageHourly>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="machineId">The machine's key.</param>
    /// <param name="fromHour">The first hour, inclusive.</param>
    /// <param name="to">The window end; hours starting after it are excluded.</param>
    public MachineHourlyUsageSpecification(Guid machineId, DateTimeOffset fromHour, DateTimeOffset to)
        : base(row => row.MachineId == machineId && row.HourStart >= fromHour && row.HourStart <= to)
    {
        AddOrderBy(row => row.HourStart);
        AddOrderBy(row => row.ServiceCode);
    }
}
