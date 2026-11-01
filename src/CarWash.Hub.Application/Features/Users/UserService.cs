using CarWash.Hub.Application.Abstractions.Persistence;
using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Application.Common.Constants;
using CarWash.Hub.Application.Common.Specifications;
using CarWash.Hub.Application.Common.Validation;
using CarWash.Hub.Application.Exceptions;
using CarWash.Hub.Application.Features.Admin;
using CarWash.Hub.Application.Features.Auth;
using CarWash.Hub.Domain.Entities;
using CarWash.Hub.Domain.Enums;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace CarWash.Hub.Application.Features.Users;

/// <summary>A dashboard user as the admin pages show them.</summary>
/// <param name="Id">The user's key.</param>
/// <param name="UserName">The sign-in name.</param>
/// <param name="Email">The e-mail address.</param>
/// <param name="DisplayName">The name shown in the dashboard.</param>
/// <param name="Role">The role.</param>
/// <param name="GasStationId">The station of a station user.</param>
/// <param name="StationName">That station's name in the current language.</param>
/// <param name="IsActive">Whether the user may sign in.</param>
/// <param name="MustChangePassword">Whether a new password is required at the next sign-in.</param>
/// <param name="LockedOutUntil">When a lockout ends; null when not locked out.</param>
/// <param name="LastSignInAt">When the user last signed in.</param>
/// <param name="RowVersion">The version an edit must start from.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record UserDto(
    Guid Id, string UserName, string Email, string DisplayName, UserRole Role, Guid? GasStationId, string? StationName,
    bool IsActive, bool MustChangePassword, DateTimeOffset? LockedOutUntil, DateTimeOffset? LastSignInAt, string RowVersion);

/// <summary>A user to create (no id, with a password) or update (with an id).</summary>
/// <remarks>Layer: Application.</remarks>
public sealed record SaveUserRequest
{
    /// <summary>Gets the user to update; null to create one.</summary>
    public Guid? Id { get; init; }

    /// <summary>Gets the sign-in name; fixed once created.</summary>
    public string UserName { get; init; } = string.Empty;

    /// <summary>Gets the e-mail address.</summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>Gets the name shown in the dashboard.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Gets the role.</summary>
    public UserRole Role { get; init; } = UserRole.StationUser;

    /// <summary>Gets the station, required for a station user and refused for a Super Admin.</summary>
    public Guid? GasStationId { get; init; }

    /// <summary>Gets a value indicating whether the user may sign in.</summary>
    public bool IsActive { get; init; } = true;

    /// <summary>Gets the first password, for a new user; they must change it at their first sign-in.</summary>
    public string? Password { get; init; }

    /// <summary>Gets the version the edit started from; required for an update.</summary>
    public string? RowVersion { get; init; }
}

/// <summary>A password set by an admin for a user who forgot theirs.</summary>
/// <param name="UserId">The user.</param>
/// <param name="NewPassword">The temporary password; the user must change it at the next sign-in.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record ResetPasswordRequest(Guid UserId, string NewPassword);

/// <summary>A signed-in user's change of their own password.</summary>
/// <param name="CurrentPassword">The password now.</param>
/// <param name="NewPassword">The new one.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>Manages dashboard users.</summary>
/// <remarks>Layer: Application.</remarks>
public interface IUserService
{
    /// <summary>Lists every user. Super Admins only.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The users, by display name.</returns>
    Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates or updates a user. Super Admins only.</summary>
    /// <param name="request">The user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The user as saved.</returns>
    Task<UserDto> SaveAsync(SaveUserRequest request, CancellationToken cancellationToken = default);

    /// <summary>Sets a temporary password and unlocks the account. Super Admins only.</summary>
    /// <param name="request">The user and the password.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when saved.</returns>
    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default);

    /// <summary>Changes the signed-in user's own password.</summary>
    /// <param name="request">The current and new passwords.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The user, with the new security stamp the session must carry from now on.</returns>
    Task<AuthenticatedUser> ChangeOwnPasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default);
}

/// <summary>The default <see cref="IUserService"/>.</summary>
/// <remarks>
/// Layer: Application.
/// <para>
/// <b>Access changes end sessions.</b> Changing a user's role, station or active flag, or their
/// password, issues a new security stamp; every session that carries the old one is signed out at
/// its next check, within minutes. The admin's own access cannot be changed from here, so the last
/// way in is never closed by accident.
/// </para>
/// </remarks>
/// <param name="unitOfWork">Data access.</param>
/// <param name="currentUser">The caller.</param>
/// <param name="passwordHasher">Hashes passwords.</param>
/// <param name="clock">Whether a lockout is still running.</param>
/// <param name="saveValidator">Validates users.</param>
/// <param name="resetValidator">Validates password resets.</param>
/// <param name="changeValidator">Validates password changes.</param>
/// <param name="localizer">Messages and station names.</param>
/// <param name="logger">Logger.</param>
public sealed class UserService(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUser,
    IPasswordHasher passwordHasher,
    IDateTimeProvider clock,
    IValidator<SaveUserRequest> saveValidator,
    IValidator<ResetPasswordRequest> resetValidator,
    IValidator<ChangePasswordRequest> changeValidator,
    ILocalizationService localizer,
    ILogger<UserService> logger) : IUserService
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        AdminRules.EnsureSuperAdmin(currentUser);
        return await ListAsync(null, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<UserDto> SaveAsync(SaveUserRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        AdminRules.EnsureSuperAdmin(currentUser);
        await saveValidator.ValidateAndThrowLocalizedAsync(request, cancellationToken).ConfigureAwait(false);

        var stationId = request.Role == UserRole.StationUser ? request.GasStationId : null;
        if (stationId is { } id)
        {
            var station = await unitOfWork.Repository<GasStation, Guid>().GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
            if (station is not { IsActive: true })
            {
                throw new Exceptions.ValidationException(nameof(SaveUserRequest.GasStationId), localizer.GetString(ResourceKeys.Validation.StationInactive));
            }
        }

        var users = unitOfWork.Repository<User, Guid>();
        User user;

        if (request.Id is { } userId)
        {
            user = await users.GetByIdAsync(userId, cancellationToken).ConfigureAwait(false)
                   ?? throw NotFoundException.For(localizer.GetString(ResourceKeys.Fields.User), userId);
            AdminRules.EnsureUnchanged(user.RowVersion, request.RowVersion);

            var accessChanged = user.Role != request.Role || user.GasStationId != stationId || user.IsActive != request.IsActive;
            if (accessChanged && user.Id == currentUser.UserId)
            {
                throw new BusinessRuleException(ResourceKeys.Admin.CannotChangeOwnAccess);
            }

            if (accessChanged)
            {
                user.SecurityStamp = Guid.NewGuid();
            }
        }
        else
        {
            user = new User
            {
                Id = Guid.CreateVersion7(),
                UserName = request.UserName.Trim(),
                PasswordHash = passwordHasher.Hash(request.Password!),
                MustChangePassword = true,
                SecurityStamp = Guid.NewGuid(),
            };
            await users.AddAsync(user, cancellationToken).ConfigureAwait(false);
        }

        user.Email = request.Email.Trim();
        user.DisplayName = request.DisplayName.Trim();
        user.Role = request.Role;
        user.GasStationId = stationId;
        user.IsActive = request.IsActive;

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("User {UserName} saved by {Admin}", user.UserName, currentUser.UserName);

        return (await ListAsync(user.Id, cancellationToken).ConfigureAwait(false))[0];
    }

    /// <inheritdoc />
    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        AdminRules.EnsureSuperAdmin(currentUser);
        await resetValidator.ValidateAndThrowLocalizedAsync(request, cancellationToken).ConfigureAwait(false);

        var user = await unitOfWork.Repository<User, Guid>().GetByIdAsync(request.UserId, cancellationToken).ConfigureAwait(false)
                   ?? throw NotFoundException.For(localizer.GetString(ResourceKeys.Fields.User), request.UserId);

        user.PasswordHash = passwordHasher.Hash(request.NewPassword);
        user.MustChangePassword = true;
        user.FailedSignInCount = 0;
        user.LockoutEndAt = null;
        user.SecurityStamp = Guid.NewGuid();

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Password of {UserName} reset by {Admin}", user.UserName, currentUser.UserName);
    }

    /// <inheritdoc />
    public async Task<AuthenticatedUser> ChangeOwnPasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var userId = currentUser.UserId ?? throw new UnauthorizedException(ResourceKeys.Errors.Unauthorized);
        await changeValidator.ValidateAndThrowLocalizedAsync(request, cancellationToken).ConfigureAwait(false);

        var user = await unitOfWork.Repository<User, Guid>().GetByIdAsync(userId, cancellationToken).ConfigureAwait(false)
                   ?? throw new UnauthorizedException(ResourceKeys.Errors.Unauthorized);

        if (passwordHasher.Verify(user.PasswordHash, request.CurrentPassword) == PasswordCheck.Failed)
        {
            throw new Exceptions.ValidationException(nameof(ChangePasswordRequest.CurrentPassword), localizer.GetString(ResourceKeys.Admin.CurrentPasswordWrong));
        }

        user.PasswordHash = passwordHasher.Hash(request.NewPassword);
        user.MustChangePassword = false;
        user.SecurityStamp = Guid.NewGuid();
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new AuthenticatedUser(user.Id, user.UserName, user.DisplayName, user.Role, user.GasStationId, user.SecurityStamp, MustChangePassword: false);
    }

    private async Task<List<UserDto>> ListAsync(Guid? userId, CancellationToken cancellationToken)
    {
        var rows = await unitOfWork.Repository<User, Guid>()
            .ListAsync(
                new UsersSpecification(userId),
                user => new
                {
                    user.Id,
                    user.UserName,
                    user.Email,
                    user.DisplayName,
                    user.Role,
                    user.GasStationId,
                    StationName = user.GasStation != null ? user.GasStation.Name : null,
                    StationNameAr = user.GasStation != null ? user.GasStation.NameAr : null,
                    user.IsActive,
                    user.MustChangePassword,
                    user.LockoutEndAt,
                    user.LastSignInAt,
                    user.RowVersion,
                },
                cancellationToken)
            .ConfigureAwait(false);

        var now = clock.UtcNow;
        return
        [
            .. rows.Select(row => new UserDto(
                row.Id, row.UserName, row.Email, row.DisplayName, row.Role, row.GasStationId,
                row.StationName is null ? null : localizer.Localize(row.StationName, row.StationNameAr),
                row.IsActive, row.MustChangePassword, row.LockoutEndAt > now ? row.LockoutEndAt : null, row.LastSignInAt,
                AdminRules.Token(row.RowVersion))),
        ];
    }
}

/// <summary>Validates a user.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed class SaveUserRequestValidator : AbstractValidator<SaveUserRequest>
{
    /// <summary>Builds the rules.</summary>
    /// <param name="localizer">Resolves messages in the caller's language.</param>
    public SaveUserRequestValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(request => request.UserName).Required(localizer, ResourceKeys.Fields.UserName, User.UserNameMaxLength);
        RuleFor(request => request.DisplayName).Required(localizer, ResourceKeys.Fields.DisplayName, User.DisplayNameMaxLength);
        RuleFor(request => request.Email)
            .Required(localizer, ResourceKeys.Fields.Email, User.EmailMaxLength)
            .EmailAddress().WithMessage(_ => localizer.GetString(ResourceKeys.Validation.InvalidEmail, localizer.GetString(ResourceKeys.Fields.Email)));
        RuleFor(request => request.Role).IsInEnum();

        RuleFor(request => request.GasStationId)
            .NotNull().When(request => request.Role == UserRole.StationUser)
            .WithMessage(_ => localizer.GetString(ResourceKeys.Validation.StationRequired));

        RuleFor(request => request.Password!)
            .Password(localizer)
            .When(request => request.Id is null);
    }
}

/// <summary>Validates a password reset.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    /// <summary>Builds the rules.</summary>
    /// <param name="localizer">Resolves messages in the caller's language.</param>
    public ResetPasswordRequestValidator(ILocalizationService localizer) =>
        RuleFor(request => request.NewPassword).Password(localizer);
}

/// <summary>Validates a password change.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    /// <summary>Builds the rules.</summary>
    /// <param name="localizer">Resolves messages in the caller's language.</param>
    public ChangePasswordRequestValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(request => request.CurrentPassword).Required(localizer, ResourceKeys.Fields.Password, 128);
        RuleFor(request => request.NewPassword)
            .Password(localizer)
            .NotEqual(request => request.CurrentPassword).WithMessage(_ => localizer.GetString(ResourceKeys.Admin.PasswordUnchanged));
    }
}

/// <summary>Every user, or one, by display name.</summary>
/// <remarks>Layer: Application.</remarks>
public sealed class UsersSpecification : BaseSpecification<User>
{
    /// <summary>Builds the specification.</summary>
    /// <param name="userId">One user, or null for all.</param>
    public UsersSpecification(Guid? userId = null)
    {
        if (userId is { } id)
        {
            AddCriteria(user => user.Id == id);
        }

        AddOrderBy(user => user.DisplayName);
        AddOrderBy(user => user.Id);
    }
}
