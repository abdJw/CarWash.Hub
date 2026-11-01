using CarWash.Hub.Application.Abstractions.Persistence;
using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CarWash.Hub.Application.Features.Auth;

/// <summary>Checks dashboard credentials and the sessions issued for them.</summary>
/// <remarks>Layer: Application. Issuing the cookie is the Web layer's job; this decides who may have one.</remarks>
public interface ISignInService
{
    /// <summary>Checks a user name (or e-mail address) and password.</summary>
    /// <param name="userName">What was typed as the user name.</param>
    /// <param name="password">The password.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The user, or why not.</returns>
    Task<SignInResult> SignInAsync(string userName, string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-checks a session: the user still exists, is active, is not locked out, and has the
    /// security stamp the session began with.
    /// </summary>
    /// <param name="userId">The session's user.</param>
    /// <param name="securityStamp">The session's stamp.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The user as they are now, or null when the session must end.</returns>
    Task<AuthenticatedUser?> ValidateSessionAsync(Guid userId, Guid securityStamp, CancellationToken cancellationToken = default);
}

/// <summary>The default <see cref="ISignInService"/>.</summary>
/// <remarks>
/// Layer: Application.
/// <para>
/// <b>Lockout.</b> <see cref="SignInOptions.MaxFailedAttempts"/> wrong passwords in a row lock the
/// account for <see cref="SignInOptions.LockoutMinutes"/>; a success resets the count. A locked
/// account refuses even the right password until the lockout ends.
/// </para>
/// <para>
/// <b>No timing oracle.</b> An unknown user name still costs a password hash, so a failed sign-in
/// takes the same time whether or not the name exists.
/// </para>
/// </remarks>
/// <param name="unitOfWork">Data access.</param>
/// <param name="passwordHasher">Verifies passwords.</param>
/// <param name="clock">Lockout windows.</param>
/// <param name="options">Lockout rules.</param>
/// <param name="logger">Logger.</param>
public sealed class SignInService(
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    IDateTimeProvider clock,
    IOptions<SignInOptions> options,
    ILogger<SignInService> logger) : ISignInService
{
    // A real hash of a random value, verified against when the user does not exist, so that path
    // runs the same key derivation as a wrong password. Made by the injected hasher - a hash in any
    // other format would be rejected before the expensive part, which is the very leak to avoid.
    // Static, because computing it costs a sign-in; a race only computes it twice.
    private static string? s_decoyHash;

    private readonly SignInOptions _options = options.Value;

    /// <inheritdoc />
    public async Task<SignInResult> SignInAsync(string userName, string password, CancellationToken cancellationToken = default)
    {
        var name = userName?.Trim() ?? string.Empty;
        if (name.Length == 0 || name.Length > User.EmailMaxLength || string.IsNullOrEmpty(password))
        {
            return Failed();
        }

        var users = unitOfWork.Repository<User, Guid>();
        var user = await users.FirstOrDefaultAsync(new UserBySignInNameSpecification(name), cancellationToken).ConfigureAwait(false);

        if (user is null)
        {
            passwordHasher.Verify(s_decoyHash ??= passwordHasher.Hash(Guid.NewGuid().ToString("N")), password);
            return Failed();
        }

        var now = clock.UtcNow;
        if (user.LockoutEndAt is { } lockedUntil && lockedUntil > now)
        {
            return new SignInResult(null, SignInFailure.LockedOut, lockedUntil);
        }

        var check = passwordHasher.Verify(user.PasswordHash, password);
        if (check == PasswordCheck.Failed || !user.IsActive)
        {
            var result = RecordFailure(user, now);
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }

        if (check == PasswordCheck.SuccessRehashNeeded)
        {
            user.PasswordHash = passwordHasher.Hash(password);
        }

        user.FailedSignInCount = 0;
        user.LockoutEndAt = null;
        user.LastSignInAt = now;
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("User {UserName} signed in", user.UserName);
        return new SignInResult(ToAuthenticated(user), null, null);
    }

    /// <inheritdoc />
    public async Task<AuthenticatedUser?> ValidateSessionAsync(Guid userId, Guid securityStamp, CancellationToken cancellationToken = default)
    {
        var user = await unitOfWork.Repository<User, Guid>().GetByIdAsync(userId, cancellationToken).ConfigureAwait(false);

        if (user is null
            || !user.IsActive
            || user.SecurityStamp != securityStamp
            || (user.LockoutEndAt is { } lockedUntil && lockedUntil > clock.UtcNow))
        {
            return null;
        }

        return ToAuthenticated(user);
    }

    private static SignInResult Failed() => new(null, SignInFailure.InvalidCredentials, null);

    private static AuthenticatedUser ToAuthenticated(User user) => new(
        user.Id,
        user.UserName,
        user.DisplayName,
        user.Role,
        user.GasStationId,
        user.SecurityStamp,
        user.MustChangePassword);

    private SignInResult RecordFailure(User user, DateTimeOffset now)
    {
        user.FailedSignInCount++;

        if (user.FailedSignInCount < _options.MaxFailedAttempts)
        {
            return Failed();
        }

        user.FailedSignInCount = 0;
        user.LockoutEndAt = now.AddMinutes(_options.LockoutMinutes);
        logger.LogWarning("User {UserName} locked out until {LockedUntil} after repeated failed sign-ins", user.UserName, user.LockoutEndAt);

        return new SignInResult(null, SignInFailure.LockedOut, user.LockoutEndAt);
    }
}
