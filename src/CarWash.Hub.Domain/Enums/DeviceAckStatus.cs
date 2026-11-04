namespace CarWash.Hub.Domain.Enums;

/// <summary>What a machine said when it acknowledged a settings revision (<c>settings_ack.status</c>).</summary>
/// <remarks>
/// Layer: Domain. Stored as <c>tinyint</c>. Only <see cref="Saved"/> and <see cref="Unchanged"/>
/// confirm that the revision is now active on the machine; every other status leaves the previous
/// settings in force.
/// </remarks>
public enum DeviceAckStatus : byte
{
    /// <summary>No acknowledgement yet.</summary>
    None = 0,

    /// <summary><c>saved</c>: validated and written to the machine's flash.</summary>
    Saved = 1,

    /// <summary><c>unchanged</c>: already matched the active values, so nothing was written.</summary>
    Unchanged = 2,

    /// <summary><c>busy</c>: not applied while a payment or wash was in progress; the machine tries again.</summary>
    Busy = 3,

    /// <summary><c>storage_error</c>: valid, but saving to flash failed.</summary>
    StorageError = 4,

    /// <summary><c>invalid_settings</c>: values the machine refused; a corrected revision is needed.</summary>
    InvalidSettings = 5,

    /// <summary><c>invalid_command</c>: the command's shape was not understood.</summary>
    InvalidCommand = 6,

    /// <summary><c>invalid_json</c>: the response could not be parsed.</summary>
    InvalidJson = 7,

    /// <summary>A status this server does not know; treated as a failure, never as applied.</summary>
    Unknown = 255,
}
