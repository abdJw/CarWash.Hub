namespace CarWash.Hub.Domain.Enums;

/// <summary>How much of a service's last refill is left.</summary>
/// <remarks>Layer: Domain. Stored as <c>tinyint</c>; ordered from calm to urgent.</remarks>
public enum ServiceLevel : byte
{
    /// <summary>Refills of this service are not tracked.</summary>
    NotTracked = 0,

    /// <summary>Tracked, but no refill has been recorded yet, so the level is unknown.</summary>
    NeverRefilled = 1,

    /// <summary>Above the service's low threshold.</summary>
    Ok = 2,

    /// <summary>At or below the low threshold, not yet empty.</summary>
    Low = 3,

    /// <summary>Every use of the last refill has been used.</summary>
    Empty = 4,

    /// <summary>
    /// Still being used after reaching empty: either the refill was not recorded, or customers are
    /// paying for a service that has run dry.
    /// </summary>
    Overused = 5,
}

/// <summary>Why derived state must be recomputed.</summary>
/// <remarks>Layer: Domain. Stored as <c>tinyint</c>.</remarks>
public enum RebuildReason : byte
{
    /// <summary>A refill was recorded or voided.</summary>
    RefillChanged = 1,

    /// <summary>A service's tracking or threshold changed.</summary>
    CatalogChanged = 2,
}
