using System.Text.RegularExpressions;
using CarWash.Hub.Domain.Entities;

namespace CarWash.Hub.Application.Features.Ingestion;

/// <summary>
/// The format a machine identifier must have.
/// </summary>
/// <remarks>
/// Layer: Application.
/// <para>
/// 1 to 100 characters of letters, digits, <c>.</c>, <c>_</c>, <c>:</c> and <c>-</c>, starting with a
/// letter or digit. Narrow on purpose: the identifier appears in URLs
/// (<c>/machines/{machineId}</c>), log lines and file names, and a slash, a space or a control
/// character would break one of them. It can be widened later without a data migration; it could
/// not be narrowed once data exists.
/// </para>
/// </remarks>
public static partial class MachineIdRules
{
    /// <summary>The longest identifier accepted.</summary>
    public const int MaxLength = Machine.CodeMaxLength;

    /// <summary>Determines whether an identifier meets the format rule.</summary>
    /// <param name="value">The identifier, already trimmed.</param>
    /// <returns><see langword="true"/> when it is acceptable.</returns>
    public static bool IsValid(string value) => ValidId().IsMatch(value);

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._:\-]{0,99}$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidId();
}
