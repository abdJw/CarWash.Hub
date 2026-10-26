using CarWash.Hub.Application.Abstractions.Persistence;
using CarWash.Hub.Domain.Entities;
using CarWash.Hub.Persistence.Context;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace CarWash.Hub.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IMachineRepository"/>.
/// </summary>
/// <remarks>
/// Layer: Persistence.
/// <para>
/// <b>Why insert-then-catch rather than check-then-insert.</b> A check followed by an insert has a
/// gap in which a concurrent request can insert the same machine; one of the two would then fail
/// with a duplicate-key error and the machine would lose a message. Here the unique index on
/// <c>machines.code</c> is the arbiter: whichever insert lands first wins, and the loser catches
/// the violation, forgets its own row and reads the winner's. Every caller gets the same machine.
/// </para>
/// <para>
/// <b>Cached.</b> A known machine's key never changes - machines are soft-deleted, never removed -
/// so the code-to-key mapping is cached for a while, and a fleet of known machines sending
/// heartbeats costs no registration lookup at all.
/// </para>
/// </remarks>
/// <param name="context">The database context.</param>
/// <param name="cache">The process-wide memory cache.</param>
public sealed class MachineRepository(ApplicationDbContext context, IMemoryCache cache)
    : GenericRepository<Machine, Guid>(context), IMachineRepository
{
    private const string CacheKeyPrefix = "machine-registration:";

    private static readonly MemoryCacheEntryOptions CacheEntryOptions = new()
    {
        // Size is set because a SizeLimit on the shared cache would otherwise make Set throw.
        Size = 1,
        SlidingExpiration = TimeSpan.FromMinutes(30),
    };

    /// <inheritdoc />
    public async Task<MachineRegistration> GetOrRegisterAsync(string code, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        // Upper-cased so "cw-01" and "CW-01" share an entry, as they share a row under the
        // case-insensitive collation. Identifiers are ASCII-only, so this matches the database.
        var cacheKey = CacheKeyPrefix + code.ToUpperInvariant();
        if (cache.TryGetValue(cacheKey, out MachineRegistration? cached) && cached is not null)
        {
            return cached;
        }

        var registration = await FindAsync(code, cancellationToken).ConfigureAwait(false)
                           ?? await RegisterAsync(code, cancellationToken).ConfigureAwait(false);

        cache.Set(cacheKey, registration, CacheEntryOptions);
        return registration;
    }

    private Task<MachineRegistration?> FindAsync(string code, CancellationToken cancellationToken) =>
        // IgnoreQueryFilters: a soft-deleted machine still owns its id, and its messages are still
        // evidence worth keeping.
        Set.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(machine => machine.Code == code)
            .Select(machine => new MachineRegistration(machine.Id, machine.Code))
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<MachineRegistration> RegisterAsync(string code, CancellationToken cancellationToken)
    {
        var machine = new Machine
        {
            Id = Guid.CreateVersion7(),
            Code = code,
            IsActive = true,
        };

        Set.Add(machine);

        try
        {
            // Saved on its own, immediately: the registration must be committed before the message
            // that references it, and the caller has staged nothing else yet.
            await Context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new MachineRegistration(machine.Id, machine.Code);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Lost the race to a concurrent first message from the same machine. Forget our row
            // and use the winner's.
            Context.Entry(machine).State = EntityState.Detached;

            return await FindAsync(code, cancellationToken).ConfigureAwait(false)
                   ?? throw new InvalidOperationException(
                       $"Machine '{code}' violated the unique index on registration but could not be read back.",
                       exception);
        }
    }
}
