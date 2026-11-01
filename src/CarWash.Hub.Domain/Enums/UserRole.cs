namespace CarWash.Hub.Domain.Enums;

/// <summary>What a dashboard user may see and do.</summary>
/// <remarks>Layer: Domain. Stored as <c>tinyint</c>; zero is deliberately unused, so an unset role is never valid.</remarks>
public enum UserRole : byte
{
    /// <summary>The owner: every station and machine, and the administration pages.</summary>
    SuperAdmin = 1,

    /// <summary>A gas station's staff: only that station's machines, including recording their refills.</summary>
    StationUser = 2,
}
