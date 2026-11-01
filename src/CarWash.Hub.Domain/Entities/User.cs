using CarWash.Hub.Domain.Common;
using CarWash.Hub.Domain.Enums;

namespace CarWash.Hub.Domain.Entities;

/// <summary>
/// A person who signs in to the dashboard.
/// </summary>
/// <remarks>
/// Layer: Domain.
/// <para>
/// Two roles, so no ASP.NET Core Identity: the password is a PBKDF2 hash from
/// <c>PasswordHasher</c>, and lockout and the security stamp are a few columns here.
/// </para>
/// <para>
/// A <see cref="UserRole.StationUser"/> belongs to exactly one <see cref="GasStation"/>, and a
/// <see cref="UserRole.SuperAdmin"/> to none; check constraints hold the database to that.
/// </para>
/// </remarks>
public class User : AuditableEntity<Guid>, IHasRowVersion
{
    /// <summary>The longest user name accepted.</summary>
    public const int UserNameMaxLength = 64;

    /// <summary>The longest e-mail address accepted.</summary>
    public const int EmailMaxLength = 256;

    /// <summary>The longest display name accepted.</summary>
    public const int DisplayNameMaxLength = 100;

    /// <summary>Gets or sets the sign-in name; unique and case-insensitive.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Gets or sets the e-mail address alerts are sent to; unique and case-insensitive.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Gets or sets the name shown in the dashboard.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Gets or sets the password hash; never the password.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Gets or sets the role.</summary>
    public UserRole Role { get; set; }

    /// <summary>Gets or sets the station of a station user; null for a Super Admin.</summary>
    public Guid? GasStationId { get; set; }

    /// <summary>Gets or sets the station of a station user.</summary>
    public GasStation? GasStation { get; set; }

    /// <summary>Gets or sets a value indicating whether the user may sign in.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Gets or sets a value that changes whenever the user's credentials or access change. Sessions
    /// carry the value they began with, and end when it no longer matches.
    /// </summary>
    public Guid SecurityStamp { get; set; } = Guid.NewGuid();

    /// <summary>Gets or sets a value indicating whether the user must choose a new password at the next sign-in.</summary>
    public bool MustChangePassword { get; set; }

    /// <summary>Gets or sets the failed sign-ins since the last success or lockout.</summary>
    public int FailedSignInCount { get; set; }

    /// <summary>Gets or sets when a lockout ends, in UTC; null when not locked out.</summary>
    public DateTimeOffset? LockoutEndAt { get; set; }

    /// <summary>Gets or sets when the user last signed in, in UTC.</summary>
    public DateTimeOffset? LastSignInAt { get; set; }

    /// <inheritdoc />
    public byte[] RowVersion { get; set; } = [];
}
