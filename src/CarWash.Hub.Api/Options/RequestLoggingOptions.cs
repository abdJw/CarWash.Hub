using System.ComponentModel.DataAnnotations;

namespace CarWash.Hub.Api.Options;

/// <summary>
/// Configures the request logging middleware.
/// </summary>
/// <remarks>
/// Layer: Api. Bound from the <c>RequestLogging</c> configuration section.
/// <para>
/// Unlike cyberani_oms_api there is no body capture. The bodies are machine messages, already
/// stored verbatim in the database where they belong; copying them into log files as well would
/// double the storage and spread the data to a second place with weaker access control.
/// </para>
/// </remarks>
public sealed class RequestLoggingOptions
{
    /// <summary>The configuration section this binds to.</summary>
    public const string SectionName = "RequestLogging";

    /// <summary>Gets or sets a value indicating whether request logging runs at all.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the duration above which a request is logged as a warning.</summary>
    [Range(1, 600_000)]
    public int SlowRequestThresholdMs { get; set; } = 2000;

    /// <summary>
    /// Gets path prefixes that are not logged. Health probes fire every few seconds from
    /// orchestrators and would otherwise dominate the log.
    /// </summary>
    public IList<string> ExcludedPathPrefixes { get; } = [];
}
