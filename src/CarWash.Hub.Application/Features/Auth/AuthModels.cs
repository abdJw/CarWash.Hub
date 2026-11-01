using System.ComponentModel.DataAnnotations;
using CarWash.Hub.Application.Common.Specifications;
using CarWash.Hub.Domain.Entities;
using CarWash.Hub.Domain.Enums;

namespace CarWash.Hub.Application.Features.Auth;

/// <summary>A signed-in dashboard user, as the session cookie carries them.</summary>
/// <param name="Id">The user's id.</param>
/// <param name="UserName">The sign-in name.</param>
/// <param name="DisplayName">The name shown in the dashboard.</param>
/// <param name="Role">The role.</param>
/// <param name="GasStationId">The station of a station user; null for a Super Admin.</param>
/// <param name="SecurityStamp">The stamp the session began with.</param>
/// <param name="MustChangePassword">Whether the user must choose a new password first.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record AuthenticatedUser(
    Guid Id,
    string UserName,
    string DisplayName,
    UserRole Role,
    Guid? GasStationId,
    Guid SecurityStamp,
    bool MustChangePassword);

/// <summary>Why a sign-in failed.</summary>
/// <remarks>
/// Layer: Application. Deliberately coarse: an unknown user, a wrong password and a deactivated
/// account all read <see cref="InvalidCredentials"/>, so the form cannot be used to find out which
/// user names exist. Only a lockout is reported as such - the person needs to know to wait.
/// </remarks>
public enum SignInFailure
{
    /// <summary>The user name or password is wrong, or the account cannot sign in.</summary>
    InvalidCredentials,

    /// <summary>Too many failed attempts; the account is locked for a while.</summary>
    LockedOut,
}

/// <summary>The outcome of a sign-in attempt.</summary>
/// <param name="User">The signed-in user; null on failure.</param>
/// <param name="Failure">Why it failed; null on success.</param>
/// <param name="LockedOutUntil">When a lockout ends, for <see cref="SignInFailure.LockedOut"/>.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record SignInResult(AuthenticatedUser? User, SignInFailure? Failure, DateTimeOffset? LockedOutUntil)
{
    /// <summary>Gets a value indicating whether the sign-in succeeded.</summary>
    public bool Succeeded => User is not null;
}

/// <summary>Sign-in rules.</summary>
/// <remarks>Layer: Application. Bound from <c>DashboardAuth</c>.</remarks>
public sealed class SignInOptions
{
    /// <summary>The configuration section this binds to.</summary>
    public const string SectionName = "DashboardAuth";

    /// <summary>Gets or sets the failed attempts in a row that lock an account.</summary>
    [Range(1, 100)]
    public int MaxFailedAttempts { get; set; } = 5;

    /// <summary>Gets or sets how long a lockout lasts, in minutes.</summary>
    [Range(1, 1440)]
    public int LockoutMinutes { get; set; } = 15;
}

/// <summary>One user by sign-in name or e-mail address, tracked to record the attempt.</summary>
/// <remarks>Layer: Application. Case-insensitive through the database collation.</remarks>
public sealed class UserBySignInNameSpecification : BaseSpecification<User>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="name">The user name or e-mail address typed.</param>
    public UserBySignInNameSpecification(string name)
        : base(user => user.UserName == name || user.Email == name)
    {
        EnableTracking();
    }
}

/// <summary>Whether any Super Admin exists - the startup seed's question.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed class SuperAdminsSpecification : BaseSpecification<User>
{
    /// <summary>Builds the specification.</summary>
    public SuperAdminsSpecification()
        : base(user => user.Role == UserRole.SuperAdmin)
    {
        IncludeSoftDeleted();
    }
}
