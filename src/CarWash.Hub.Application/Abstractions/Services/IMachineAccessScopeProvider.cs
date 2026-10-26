namespace CarWash.Hub.Application.Abstractions.Services;

/// <summary>
/// Decides which machines the current caller may see.
/// </summary>
/// <remarks>
/// Layer: Application.
/// <para>
/// <b>The Phase 2 safeguard.</b> Every read in <c>MachineService</c> asks this for a scope and
/// filters its query by it, so station isolation is enforced where the data is read - not left to
/// the UI. In Phase 1 the only readers hold the client read key, and the implementation always
/// answers <see cref="MachineAccessScope.AllStations"/>. Phase 2 registers a user-aware one that
/// answers <see cref="MachineAccessScope.Station(Guid)"/> for a gas station user; no service
/// changes.
/// </para>
/// </remarks>
public interface IMachineAccessScopeProvider
{
    /// <summary>Returns the scope for the current caller.</summary>
    /// <returns>The machines the caller may see.</returns>
    MachineAccessScope GetScope();
}

/// <summary>
/// The machines a caller may see: every station's, or one station's.
/// </summary>
/// <param name="GasStationId">The only station visible, or null for all of them.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record MachineAccessScope(Guid? GasStationId)
{
    /// <summary>Gets the scope that sees every machine, assigned or not.</summary>
    public static MachineAccessScope AllStations { get; } = new((Guid?)null);

    /// <summary>Gets a value indicating whether the scope is limited to one station.</summary>
    public bool IsRestricted => GasStationId is not null;

    /// <summary>Creates a scope limited to one station's machines.</summary>
    /// <param name="gasStationId">The station.</param>
    /// <returns>The scope.</returns>
    public static MachineAccessScope Station(Guid gasStationId) => new(gasStationId);
}
