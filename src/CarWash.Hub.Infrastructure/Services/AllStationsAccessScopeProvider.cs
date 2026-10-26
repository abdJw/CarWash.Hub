using CarWash.Hub.Application.Abstractions.Services;

namespace CarWash.Hub.Infrastructure.Services;

/// <summary>
/// The Phase 1 access scope: every caller allowed to read sees every machine.
/// </summary>
/// <remarks>
/// Layer: Infrastructure.
/// <para>
/// In Phase 1 the only readers hold the <c>client-read-key</c> - the owner and the testers - so
/// there is nothing to restrict. Phase 2 replaces this registration with a provider that reads the
/// signed-in dashboard user and returns <see cref="MachineAccessScope.Station(Guid)"/> for a gas
/// station user; the services already filter by whatever scope they are given.
/// </para>
/// </remarks>
public sealed class AllStationsAccessScopeProvider : IMachineAccessScopeProvider
{
    /// <inheritdoc />
    public MachineAccessScope GetScope() => MachineAccessScope.AllStations;
}
