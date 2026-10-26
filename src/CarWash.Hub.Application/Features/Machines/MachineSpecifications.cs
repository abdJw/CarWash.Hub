using System.Linq.Expressions;
using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Application.Common.Specifications;
using CarWash.Hub.Domain.Entities;
using CarWash.Hub.Domain.Enums;

namespace CarWash.Hub.Application.Features.Machines;

/// <summary>Machines matching the list filters, inside the caller's access scope.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed class MachineListSpecification : BaseSpecification<Machine>
{
    // An allow-list: a client-supplied SortBy never reaches the database as text.
    private static readonly Dictionary<string, Expression<Func<Machine, object?>>> SortableFields =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["machineId"] = machine => machine.Code,
            ["firstSeenAt"] = machine => machine.CreatedAt,
            ["lastSeenAt"] = machine => machine.Messages.Max(message => (DateTimeOffset?)message.ReceivedAt),
        };

    /// <summary>Builds the specification.</summary>
    /// <param name="query">The filters, sort and page.</param>
    /// <param name="scope">The machines the caller may see.</param>
    public MachineListSpecification(MachineListQuery query, MachineAccessScope scope)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(scope);

        if (scope.IsRestricted)
        {
            AddCriteria(machine => machine.GasStationId == scope.GasStationId);
        }

        if (query.IsActive is { } isActive)
        {
            AddCriteria(machine => machine.IsActive == isActive);
        }

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var term = query.SearchTerm.Trim();
            AddCriteria(machine => machine.Code.Contains(term)
                                   || (machine.DisplayName != null && machine.DisplayName.Contains(term)));
        }

        var sort = query.SortBy is not null && SortableFields.TryGetValue(query.SortBy, out var selector)
            ? selector
            : SortableFields["machineId"];
        AddOrderBy(sort, query.SortDirection);

        // A unique tiebreaker, so paging is stable when the sort key repeats.
        AddOrderBy(machine => machine.Id);
        ApplyPaging(query);
    }
}

/// <summary>One machine by the identifier it sends, inside the caller's access scope.</summary>
/// <remarks>
/// Layer: Application. The comparison is case-insensitive through the database collation, so
/// <c>cw-01</c> finds <c>CW-01</c>.
/// </remarks>
public sealed class MachineByCodeSpecification : BaseSpecification<Machine>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="code">The machine identifier.</param>
    /// <param name="scope">The machines the caller may see.</param>
    public MachineByCodeSpecification(string code, MachineAccessScope scope)
        : base(machine => machine.Code == code)
    {
        ArgumentNullException.ThrowIfNull(scope);

        if (scope.IsRestricted)
        {
            AddCriteria(machine => machine.GasStationId == scope.GasStationId);
        }
    }
}

/// <summary>One machine's messages, newest first, optionally within a time window.</summary>
/// <remarks>
/// Layer: Application. Ordered by <c>received_at DESC, id DESC</c>, which is exactly the
/// <c>ix_machine_messages_machine_id_received_at</c> index, so a page is an index range read.
/// </remarks>
public sealed class MachineMessagesSpecification : BaseSpecification<MachineMessage>
{
    /// <summary>Builds the specification for a page of history.</summary>
    /// <param name="machineId">The machine's key.</param>
    /// <param name="query">The window and page.</param>
    public MachineMessagesSpecification(Guid machineId, MachineHistoryQuery query)
        : base(message => message.MachineId == machineId)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.From is { } from)
        {
            AddCriteria(message => message.ReceivedAt >= from);
        }

        if (query.To is { } to)
        {
            AddCriteria(message => message.ReceivedAt <= to);
        }

        AddOrderBy(message => message.ReceivedAt, SortDirection.Descending);
        AddOrderBy(message => message.Id, SortDirection.Descending);
        ApplyPaging(query);
    }

    /// <summary>Builds the specification for the single latest message.</summary>
    /// <param name="machineId">The machine's key.</param>
    /// <returns>The specification.</returns>
    public static MachineMessagesSpecification Latest(Guid machineId) =>
        new(machineId, new MachineHistoryQuery { PageNumber = 1, PageSize = 1 });
}

/// <summary>One stored message by its identifier, inside the caller's access scope.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed class MachineMessageByIdSpecification : BaseSpecification<MachineMessage>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="id">The message identifier.</param>
    /// <param name="scope">The machines the caller may see.</param>
    public MachineMessageByIdSpecification(long id, MachineAccessScope scope)
        : base(message => message.Id == id)
    {
        ArgumentNullException.ThrowIfNull(scope);

        if (scope.IsRestricted)
        {
            AddCriteria(message => message.Machine.GasStationId == scope.GasStationId);
        }

        AddInclude(message => message.Machine);
    }
}
