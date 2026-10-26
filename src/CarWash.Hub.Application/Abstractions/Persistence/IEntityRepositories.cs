using CarWash.Hub.Domain.Entities;

namespace CarWash.Hub.Application.Abstractions.Persistence;

/// <summary>
/// Data access for machines, adding the one operation generic CRUD cannot express safely.
/// </summary>
/// <remarks>Layer: Application.</remarks>
public interface IMachineRepository : IGenericRepository<Machine, Guid>
{
    /// <summary>
    /// Returns the machine with the given identifier, registering it first if it is new.
    /// </summary>
    /// <param name="code">The machine identifier, already validated and trimmed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The machine's key and its identifier as first registered.</returns>
    /// <remarks>
    /// <para>
    /// <b>Race-safe.</b> Two first messages from the same new machine can arrive together. Both
    /// may find no row, both try to insert, and the unique index on the code lets exactly one
    /// win; the loser discards its insert and reads the winner's row. Every caller therefore gets
    /// the same machine, and no request fails because of the race.
    /// </para>
    /// <para>
    /// Soft-deleted and inactive machines are found too: a message is never rejected because of
    /// the machine's administrative state - it is evidence of what the machine is doing.
    /// </para>
    /// <para>
    /// Commits the registration immediately, on its own, so call it before staging anything else
    /// in the same unit of work.
    /// </para>
    /// </remarks>
    Task<MachineRegistration> GetOrRegisterAsync(string code, CancellationToken cancellationToken = default);
}

/// <summary>A machine's key and its identifier as first registered.</summary>
/// <param name="Id">The machine's primary key.</param>
/// <param name="Code">
/// The identifier as first registered. Matching is case-insensitive, so this may differ in case
/// from the one in the current message; this is the canonical spelling.
/// </param>
/// <remarks>Layer: Application.</remarks>
public sealed record MachineRegistration(Guid Id, string Code);
