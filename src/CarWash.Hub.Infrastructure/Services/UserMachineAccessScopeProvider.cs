using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Domain.Enums;

namespace CarWash.Hub.Infrastructure.Services;

/// <summary>
/// The machines the current caller may see: a station user sees their station's, everyone else all.
/// </summary>
/// <remarks>
/// Layer: Infrastructure.
/// <para>
/// <b>Who sees everything.</b> A Super Admin, a holder of the <c>client-read-key</c>, and code
/// running with no caller at all - the processing worker, startup. Only a station user is
/// restricted, and every service filters its reads by the scope returned here, so isolation holds
/// even for a URL typed by hand: another station's machine is simply not found.
/// </para>
/// <para>
/// <b>Fails closed.</b> A station user whose session somehow lacks a station is given a scope that
/// matches no station, rather than all of them.
/// </para>
/// </remarks>
/// <param name="currentUser">The caller.</param>
public sealed class UserMachineAccessScopeProvider(ICurrentUserService currentUser) : IMachineAccessScopeProvider
{
    /// <inheritdoc />
    public MachineAccessScope GetScope() =>
        currentUser.Role == UserRole.StationUser
            ? MachineAccessScope.Station(currentUser.GasStationId ?? Guid.Empty)
            : MachineAccessScope.AllStations;
}
