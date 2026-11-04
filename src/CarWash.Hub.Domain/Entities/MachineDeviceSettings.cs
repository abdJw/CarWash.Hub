using CarWash.Hub.Domain.Common;
using CarWash.Hub.Domain.Enums;

namespace CarWash.Hub.Domain.Entities;

/// <summary>
/// The settings a machine should run - its station's prices and run times - and what the machine
/// has confirmed it runs.
/// </summary>
/// <remarks>
/// Layer: Domain.
/// <para>
/// <b>Desired and applied.</b> The machine polls for its settings about every 15 seconds and is
/// answered with <see cref="DesiredRevision"/> and <see cref="ServicesJson"/>. Every change to the
/// values gets the next revision; a revision is never reused for different values. The machine
/// applies a revision when idle and acknowledges it, and only a <c>saved</c> or <c>unchanged</c>
/// acknowledgement of a revision moves <see cref="AppliedRevision"/> up to it. The desired snapshot
/// is kept after it is applied: a machine that reboots asks again.
/// </para>
/// <para>
/// Shares the machine's key, like <see cref="MachineState"/>, and like it has no rowversion: only
/// the processing worker writes it - the reconciler for the desired side, the message processor for
/// acknowledgements - and the worker lease keeps that to one writer.
/// </para>
/// </remarks>
public class MachineDeviceSettings : BaseEntity<Guid>
{
    /// <summary>The longest services document kept: the firmware's limit for the command without its revision.</summary>
    public const int ServicesJsonMaxLength = 640;

    /// <summary>The highest revision the firmware accepts (an unsigned 32-bit number).</summary>
    public const long MaxRevision = uint.MaxValue;

    /// <summary>Gets or sets the revision the machine should run; 0 until there is one.</summary>
    public long DesiredRevision { get; set; }

    /// <summary>
    /// Gets or sets the <c>services</c> object of the settings document for <see cref="DesiredRevision"/>,
    /// as the minified JSON sent to the machine; null until there is one.
    /// </summary>
    public string? ServicesJson { get; set; }

    /// <summary>Gets or sets when <see cref="DesiredRevision"/> was issued, in UTC.</summary>
    public DateTimeOffset? DesiredAt { get; set; }

    /// <summary>Gets or sets why the station's current values could not be published, if they could not.</summary>
    public DeviceSettingsIssue Issue { get; set; }

    /// <summary>Gets or sets the service <see cref="Issue"/> is about, when it is about one.</summary>
    public string? IssueService { get; set; }

    /// <summary>Gets or sets the newest revision the machine confirmed it runs; 0 until it confirms one.</summary>
    public long AppliedRevision { get; set; }

    /// <summary>Gets or sets when the machine confirmed <see cref="AppliedRevision"/>, in UTC.</summary>
    public DateTimeOffset? AppliedAt { get; set; }

    /// <summary>Gets or sets the revision the machine's newest acknowledgement was about.</summary>
    public long LastAckRevision { get; set; }

    /// <summary>Gets or sets what the machine said in its newest acknowledgement.</summary>
    public DeviceAckStatus LastAckStatus { get; set; }

    /// <summary>Gets or sets when the newest acknowledgement arrived, in UTC.</summary>
    public DateTimeOffset? LastAckAt { get; set; }

    /// <summary>Gets or sets the message the newest acknowledgement came in; acknowledgements are ordered by it.</summary>
    public long? LastAckMessageId { get; set; }

    /// <summary>
    /// Gets or sets the highest revision any acknowledgement named. Normally at most
    /// <see cref="DesiredRevision"/>; higher only if the database was restored to an earlier state,
    /// and then the next revision is issued above it, so the machine never mistakes new values for
    /// ones it already has.
    /// </summary>
    public long MaxReportedRevision { get; set; }

    /// <summary>Gets or sets when the worker last wrote this row, in UTC.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
