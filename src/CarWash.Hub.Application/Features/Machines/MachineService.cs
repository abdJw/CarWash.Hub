using System.Linq.Expressions;
using System.Text.Json;
using CarWash.Hub.Application.Abstractions.Persistence;
using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Application.Common.Constants;
using CarWash.Hub.Application.Common.Models;
using CarWash.Hub.Application.Exceptions;
using CarWash.Hub.Domain.Entities;

namespace CarWash.Hub.Application.Features.Machines;

/// <summary>Reads machines and their stored messages.</summary>
/// <remarks>
/// Layer: Application. For testing the ingestion in Phase 1, and the data source of the Phase 2
/// dashboard. Every read is limited to the caller's <see cref="MachineAccessScope"/>.
/// </remarks>
public interface IMachineService
{
    /// <summary>Lists machines.</summary>
    /// <param name="query">Filters, sort and page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A page of machines with their first- and last-seen times.</returns>
    Task<PagedResult<MachineDto>> GetPagedAsync(MachineListQuery query, CancellationToken cancellationToken = default);

    /// <summary>Returns one machine.</summary>
    /// <param name="machineId">The identifier the machine sends; case-insensitive.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The machine.</returns>
    /// <exception cref="NotFoundException">No such machine in the caller's scope.</exception>
    Task<MachineDto> GetByCodeAsync(string machineId, CancellationToken cancellationToken = default);

    /// <summary>Returns a machine's most recent message - its current status.</summary>
    /// <param name="machineId">The identifier the machine sends; case-insensitive.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The latest message.</returns>
    /// <exception cref="NotFoundException">No such machine, or it has no stored message.</exception>
    Task<MachineMessageDto> GetLatestMessageAsync(string machineId, CancellationToken cancellationToken = default);

    /// <summary>Returns a page of a machine's messages, newest first.</summary>
    /// <param name="machineId">The identifier the machine sends; case-insensitive.</param>
    /// <param name="query">The time window and page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A page of messages with their raw payloads.</returns>
    /// <exception cref="NotFoundException">No such machine in the caller's scope.</exception>
    Task<PagedResult<MachineMessageDto>> GetHistoryAsync(string machineId, MachineHistoryQuery query, CancellationToken cancellationToken = default);

    /// <summary>Returns one stored message.</summary>
    /// <param name="id">The message identifier, as returned when it was stored.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The message.</returns>
    /// <exception cref="NotFoundException">No such message in the caller's scope.</exception>
    Task<MachineMessageDto> GetMessageByIdAsync(long id, CancellationToken cancellationToken = default);
}

/// <summary>The default <see cref="IMachineService"/>.</summary>
/// <remarks>Layer: Application.</remarks>
/// <param name="unitOfWork">Data access.</param>
/// <param name="scopeProvider">The machines the caller may see.</param>
/// <param name="localizer">Names things in "not found" messages.</param>
public sealed class MachineService(
    IUnitOfWork unitOfWork,
    IMachineAccessScopeProvider scopeProvider,
    ILocalizationService localizer) : IMachineService
{
    // Projected in the database: last-seen is a MAX over the (machine_id, received_at) index - a
    // single seek per machine - rather than anything stored on the machine row.
    private static readonly Expression<Func<Machine, MachineDto>> ToMachineDto = machine => new MachineDto(
        machine.Id,
        machine.Code,
        machine.DisplayName,
        machine.GasStationId,
        machine.IsActive,
        machine.CreatedAt,
        machine.Messages.Max(message => (DateTimeOffset?)message.ReceivedAt));

    /// <inheritdoc />
    public async Task<PagedResult<MachineDto>> GetPagedAsync(MachineListQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await unitOfWork.Machines
            .PagedListAsync(
                new MachineListSpecification(query, scopeProvider.GetScope()),
                ToMachineDto,
                query.PageNumber,
                query.PageSize,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<MachineDto> GetByCodeAsync(string machineId, CancellationToken cancellationToken = default)
    {
        var machines = await unitOfWork.Machines
            .ListAsync(new MachineByCodeSpecification(machineId, scopeProvider.GetScope()), ToMachineDto, cancellationToken)
            .ConfigureAwait(false);

        return machines.Count > 0 ? machines[0] : throw MachineNotFound(machineId);
    }

    /// <inheritdoc />
    public async Task<MachineMessageDto> GetLatestMessageAsync(string machineId, CancellationToken cancellationToken = default)
    {
        var machine = await FindMachineAsync(machineId, cancellationToken).ConfigureAwait(false);

        var latest = await unitOfWork.MachineMessages
            .FirstOrDefaultAsync(MachineMessagesSpecification.Latest(machine.Id), cancellationToken)
            .ConfigureAwait(false)
            ?? throw NotFoundException.For(localizer.GetString(ResourceKeys.Fields.MachineMessage), machine.Code);

        return ToMessageDto(latest, machine.Code, includeJson: true);
    }

    /// <inheritdoc />
    public async Task<PagedResult<MachineMessageDto>> GetHistoryAsync(
        string machineId,
        MachineHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var machine = await FindMachineAsync(machineId, cancellationToken).ConfigureAwait(false);

        var page = await unitOfWork.MachineMessages
            .PagedListAsync(new MachineMessagesSpecification(machine.Id, query), query.PageNumber, query.PageSize, cancellationToken)
            .ConfigureAwait(false);

        // Raw payloads only: parsing up to a hundred documents per page for a convenience field
        // would cost more than the list is worth. The single-message endpoints carry PayloadJson.
        return new PagedResult<MachineMessageDto>(
            [.. page.Items.Select(message => ToMessageDto(message, machine.Code, includeJson: false))],
            page.PageNumber,
            page.PageSize,
            page.TotalCount);
    }

    /// <inheritdoc />
    public async Task<MachineMessageDto> GetMessageByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var message = await unitOfWork.MachineMessages
            .FirstOrDefaultAsync(new MachineMessageByIdSpecification(id, scopeProvider.GetScope()), cancellationToken)
            .ConfigureAwait(false)
            ?? throw NotFoundException.For(localizer.GetString(ResourceKeys.Fields.MachineMessage), id);

        return ToMessageDto(message, message.Machine.Code, includeJson: true);
    }

    private async Task<MachineRegistration> FindMachineAsync(string machineId, CancellationToken cancellationToken)
    {
        var machines = await unitOfWork.Machines
            .ListAsync(
                new MachineByCodeSpecification(machineId, scopeProvider.GetScope()),
                machine => new MachineRegistration(machine.Id, machine.Code),
                cancellationToken)
            .ConfigureAwait(false);

        return machines.Count > 0 ? machines[0] : throw MachineNotFound(machineId);
    }

    private NotFoundException MachineNotFound(string machineId) =>
        NotFoundException.For(localizer.GetString(ResourceKeys.Fields.Machine), machineId);

    private static MachineMessageDto ToMessageDto(MachineMessage message, string machineCode, bool includeJson) =>
        new(
            message.Id,
            machineCode,
            message.ReceivedAt,
            message.PayloadSizeBytes,
            message.ClientKeyId,
            message.SourceIp,
            message.UserAgent,
            message.CorrelationId,
            message.Payload,
            includeJson ? ParseJson(message.Payload) : null);

    // Data sent as a JSON string may hold any text, so "is it JSON" is decided by parsing. Only
    // objects and arrays count: a payload that is just a number or a word is shown as text alone.
    private static JsonElement? ParseJson(string payload)
    {
        var start = payload.AsSpan().TrimStart();
        if (start.IsEmpty || start[0] is not ('{' or '['))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);

            // Cloned: the element must outlive the document, which is disposed on return.
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
