using CarWash.Hub.Application.Common.Specifications;
using CarWash.Hub.Domain.Entities;
using CarWash.Hub.Domain.Enums;

namespace CarWash.Hub.Application.Features.Processing;

// Every specification here bypasses the soft-delete filter: the worker processes the messages of a
// deleted machine like any other, and must find that machine's existing rows rather than insert
// duplicates beside rows the filter hid.

/// <summary>The oldest pending messages, by id.</summary>
/// <remarks>
/// Layer: Application. Served by the filtered index <c>ix_machine_messages_pending</c>, which holds
/// only pending rows, so the worker's queue stays a tiny index however long the history grows.
/// </remarks>
public sealed class PendingMessagesSpecification : BaseSpecification<MachineMessage>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="take">The batch size.</param>
    public PendingMessagesSpecification(int take)
        : base(message => message.ProcessingStatus == ProcessingStatus.Pending)
    {
        IncludeSoftDeleted();
        AddOrderBy(message => message.Id);
        ApplyPaging(0, take);
    }
}

/// <summary>Messages by id, tracked, to record their processing outcome.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed class MessagesByIdsSpecification : BaseSpecification<MachineMessage>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="ids">The message ids.</param>
    public MessagesByIdsSpecification(IReadOnlyCollection<long> ids)
        : base(message => ids.Contains(message.Id))
    {
        IncludeSoftDeleted();
        EnableTracking();
    }
}

/// <summary>Messages in one processing state - the status endpoint's counts.</summary>
/// <remarks>Layer: Application. Pending and failed rows each have a filtered index.</remarks>
public sealed class MessagesByStatusSpecification : BaseSpecification<MachineMessage>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="status">The state.</param>
    /// <param name="oldestFirst">Whether to order oldest first, for "the oldest pending message".</param>
    public MessagesByStatusSpecification(ProcessingStatus status, bool oldestFirst = false)
        : base(message => message.ProcessingStatus == status)
    {
        IncludeSoftDeleted();

        if (oldestFirst)
        {
            AddOrderBy(message => message.Id);
            ApplyPaging(0, 1);
        }
    }
}

/// <summary>Every service code in the catalog, including retired ones.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed class AllServiceDefinitionsSpecification : BaseSpecification<ServiceDefinition>
{
    /// <summary>Builds the specification.</summary>
    public AllServiceDefinitionsSpecification()
    {
        IncludeSoftDeleted();
    }
}

/// <summary>
/// The device-timed uses of some machines within a time window - the candidates a retried message
/// could duplicate.
/// </summary>
/// <remarks>Layer: Application. A seek on <c>(machine_id, service_code, occurred_at)</c> per machine.</remarks>
public sealed class DeviceTimedUsagesSpecification : BaseSpecification<ServiceUsage>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="machineIds">The machines.</param>
    /// <param name="from">The window start, inclusive.</param>
    /// <param name="to">The window end, inclusive.</param>
    public DeviceTimedUsagesSpecification(IReadOnlyCollection<Guid> machineIds, DateTimeOffset from, DateTimeOffset to)
        : base(usage => machineIds.Contains(usage.MachineId)
                        && usage.OccurredAtSource == OccurrenceTimeSource.Device
                        && usage.OccurredAt >= from
                        && usage.OccurredAt <= to)
    {
        IncludeSoftDeleted();
    }
}

/// <summary>The hourly rollup rows of some machines within a range of hours, tracked to be incremented.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed class HourlyUsageRowsSpecification : BaseSpecification<ServiceUsageHourly>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="machineIds">The machines.</param>
    /// <param name="fromHour">The first hour, inclusive.</param>
    /// <param name="toHour">The last hour, inclusive.</param>
    public HourlyUsageRowsSpecification(IReadOnlyCollection<Guid> machineIds, DateTimeOffset fromHour, DateTimeOffset toHour)
        : base(row => machineIds.Contains(row.MachineId) && row.HourStart >= fromHour && row.HourStart <= toHour)
    {
        IncludeSoftDeleted();
        EnableTracking();
    }
}

/// <summary>The daily rollup rows of some machines within a range of business days, tracked to be incremented.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed class DailyUsageRowsSpecification : BaseSpecification<ServiceUsageDaily>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="machineIds">The machines.</param>
    /// <param name="fromDate">The first day, inclusive.</param>
    /// <param name="toDate">The last day, inclusive.</param>
    public DailyUsageRowsSpecification(IReadOnlyCollection<Guid> machineIds, DateOnly fromDate, DateOnly toDate)
        : base(row => machineIds.Contains(row.MachineId) && row.BusinessDate >= fromDate && row.BusinessDate <= toDate)
    {
        EnableTracking();
    }
}

/// <summary>Some machines, including deleted ones - for the station each one stands at.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed class MachinesByIdsSpecification : BaseSpecification<Machine>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="machineIds">The machines.</param>
    public MachinesByIdsSpecification(IReadOnlyCollection<Guid> machineIds)
        : base(machine => machineIds.Contains(machine.Id))
    {
        IncludeSoftDeleted();
    }
}

/// <summary>The state rows of some machines, tracked to be updated.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed class MachineStatesSpecification : BaseSpecification<MachineState>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="machineIds">The machines.</param>
    public MachineStatesSpecification(IReadOnlyCollection<Guid> machineIds)
        : base(state => machineIds.Contains(state.Id))
    {
        IncludeSoftDeleted();
        EnableTracking();
    }
}
