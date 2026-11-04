namespace CarWash.Hub.Domain.Enums;

/// <summary>Why a machine's settings could not be brought up to date with its station.</summary>
/// <remarks>
/// Layer: Domain. Stored as <c>tinyint</c>. While there is an issue, the machine keeps being sent the
/// last complete settings it was given - a machine is never sent a partial snapshot.
/// </remarks>
public enum DeviceSettingsIssue : byte
{
    /// <summary>The settings are complete and current.</summary>
    None = 0,

    /// <summary>The machine stands at no station, so it has no prices or run times.</summary>
    Unassigned = 1,

    /// <summary>The machine's station is out of service.</summary>
    StationInactive = 2,

    /// <summary>A service has no price today at the machine's station.</summary>
    MissingPrice = 3,

    /// <summary>A service's price today is one a machine cannot take (not 0.05 to 9.95 in steps of 0.05).</summary>
    InvalidPrice = 4,

    /// <summary>A service has no run times saved at the machine's station.</summary>
    MissingDurations = 5,
}
