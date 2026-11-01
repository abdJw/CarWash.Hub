using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Application.Common.Constants;
using CarWash.Hub.Application.Exceptions;
using CarWash.Hub.Domain.Enums;

namespace CarWash.Hub.Application.Features.Admin;

/// <summary>
/// The checks every administrative command makes, whoever calls it.
/// </summary>
/// <remarks>
/// Layer: Application. The dashboard hides the admin pages from station users, but the rule lives
/// here too: a command refused only by the UI is one forgotten attribute away from being open.
/// </remarks>
public static class AdminRules
{
    /// <summary>Refuses anyone but a signed-in Super Admin.</summary>
    /// <param name="currentUser">The caller.</param>
    /// <exception cref="ForbiddenException">The caller is not a Super Admin.</exception>
    public static void EnsureSuperAdmin(ICurrentUserService currentUser)
    {
        ArgumentNullException.ThrowIfNull(currentUser);

        if (currentUser.Role != UserRole.SuperAdmin)
        {
            throw new ForbiddenException(ResourceKeys.Admin.SuperAdminOnly);
        }
    }

    /// <summary>Encodes a row version for a form to send back with its edit.</summary>
    /// <param name="rowVersion">The row version.</param>
    /// <returns>Base64.</returns>
    public static string Token(byte[] rowVersion) => Convert.ToBase64String(rowVersion ?? []);

    /// <summary>
    /// Refuses an edit made to an older version of the row than the one stored - someone else saved
    /// in between. The database repeats the check on save, closing the window after this one.
    /// </summary>
    /// <param name="stored">The row version as loaded.</param>
    /// <param name="token">The row version the editor started from.</param>
    /// <exception cref="ConflictException">The row has changed since.</exception>
    public static void EnsureUnchanged(byte[] stored, string? token)
    {
        byte[] edited;
        try
        {
            edited = Convert.FromBase64String(token ?? string.Empty);
        }
        catch (FormatException)
        {
            edited = [];
        }

        if (!edited.AsSpan().SequenceEqual(stored))
        {
            throw new ConflictException(ResourceKeys.Errors.ConcurrencyConflict);
        }
    }
}
