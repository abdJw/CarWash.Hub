namespace CarWash.Hub.Domain.Enums;

/// <summary>Where a stored machine message is in the parsing pipeline.</summary>
/// <remarks>
/// Layer: Domain. Stored as <c>tinyint</c>. <see cref="Pending"/> is zero on purpose: the column was
/// added with a default of zero, so every message stored before parsing existed is picked up and
/// backfilled, and a reprocess is just a reset to zero.
/// </remarks>
public enum ProcessingStatus : byte
{
    /// <summary>Stored, not parsed yet. The worker's queue.</summary>
    Pending = 0,

    /// <summary>Parsed into usage events.</summary>
    Processed = 1,

    /// <summary>Parsed, but carries nothing to record, such as a status-only or non-JSON payload.</summary>
    Ignored = 2,

    /// <summary>Could not be processed; <c>processing_error</c> says why.</summary>
    Failed = 3,
}

/// <summary>Which clock an occurrence time came from.</summary>
/// <remarks>Layer: Domain. Stored as <c>tinyint</c>.</remarks>
public enum OccurrenceTimeSource : byte
{
    /// <summary>The machine's own reading time (<c>readAt</c>), within the accepted clock skew.</summary>
    Device = 0,

    /// <summary>
    /// The server's receive time, used when the machine sent no reading time or its clock was too
    /// far off to trust.
    /// </summary>
    Server = 1,
}
