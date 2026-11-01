namespace CarWash.Hub.Domain.Enums;

/// <summary>What an alert is about.</summary>
/// <remarks>
/// Layer: Domain. Stored as <c>tinyint</c>. The four level alerts are ordered from calm to urgent: a
/// service has at most one of them open at a time, the most urgent that applies.
/// </remarks>
public enum AlertType : byte
{
    /// <summary>At the usual pace, the service will run out within a day, though it is not yet low.</summary>
    RunsOutSoon = 1,

    /// <summary>The service is at or below its low threshold.</summary>
    Low = 2,

    /// <summary>Every use of the service's last refill has been used.</summary>
    Empty = 3,

    /// <summary>The service is still being used after running empty.</summary>
    UsedAfterEmpty = 4,

    /// <summary>The machine has sent nothing for longer than expected.</summary>
    NoActivity = 10,
}

/// <summary>How urgent an alert is.</summary>
/// <remarks>Layer: Domain. Stored as <c>tinyint</c>.</remarks>
public enum AlertSeverity : byte
{
    /// <summary>Plan for it: refill soon, or check on the machine.</summary>
    Warning = 1,

    /// <summary>Customers are affected now.</summary>
    Critical = 2,
}

/// <summary>Why an alert closed.</summary>
/// <remarks>Layer: Domain. Stored as <c>tinyint</c>.</remarks>
public enum AlertResolution : byte
{
    /// <summary>A refill was recorded for the service.</summary>
    Refilled = 1,

    /// <summary>A more urgent alert for the same service replaced it.</summary>
    Escalated = 2,

    /// <summary>
    /// The condition no longer applies without a refill: a refill was voided, a threshold changed,
    /// the forecast moved out, or the service stopped being tracked.
    /// </summary>
    Cleared = 3,

    /// <summary>The machine was heard from again.</summary>
    ActivityResumed = 4,

    /// <summary>The machine was unassigned, taken out of service or deleted, or its station was.</summary>
    NotEligible = 5,
}
