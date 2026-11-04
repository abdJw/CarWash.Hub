using CarWash.Hub.Application.Common.Specifications;
using CarWash.Hub.Domain.Entities;

namespace CarWash.Hub.Application.Features.Ingestion;

/// <summary>The stored message a machine sent with a given device event id - the original of a retry.</summary>
/// <remarks>
/// Layer: Application. A seek on the unique filtered index <c>ix_machine_messages_device_event_id</c>.
/// Bypasses the soft-delete filter, which reaches messages through their required machine: a deleted
/// machine's originals must still be found, or its retries would be stored twice.
/// </remarks>
public sealed class MessageByDeviceEventSpecification : BaseSpecification<MachineMessage>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="machineId">The machine.</param>
    /// <param name="deviceEventId">The device's event id.</param>
    public MessageByDeviceEventSpecification(Guid machineId, string deviceEventId)
        : base(message => message.MachineId == machineId && message.DeviceEventId == deviceEventId)
    {
        IncludeSoftDeleted();
        ApplyPaging(0, 1);
    }
}
