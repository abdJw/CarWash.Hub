using CarWash.Hub.Application.Abstractions.Persistence;
using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Application.Common.Specifications;
using CarWash.Hub.Application.Features.Auth;
using CarWash.Hub.Domain.Entities;
using CarWash.Hub.Domain.Enums;
using CarWash.Hub.Infrastructure.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace CarWash.Hub.UnitTests.Auth;

public class SignInServiceTests
{
    private const string Password = "correct horse battery";
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly PasswordHasherAdapter Hasher = new();
    private static readonly string Hash = Hasher.Hash(Password);

    private readonly IGenericRepository<User, Guid> _users = Substitute.For<IGenericRepository<User, Guid>>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();

    public SignInServiceTests()
    {
        _unitOfWork.Repository<User, Guid>().Returns(_users);
        _clock.UtcNow.Returns(Now);
    }

    private SignInService Service(int maxFailedAttempts = 3) => new(
        _unitOfWork,
        Hasher,
        _clock,
        Options.Create(new SignInOptions { MaxFailedAttempts = maxFailedAttempts, LockoutMinutes = 15 }),
        NullLogger<SignInService>.Instance);

    private User Existing(Action<User>? configure = null)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = "station1",
            Email = "station1@example.com",
            DisplayName = "Station One",
            PasswordHash = Hash,
            Role = UserRole.StationUser,
            GasStationId = Guid.NewGuid(),
        };
        configure?.Invoke(user);

        _users.FirstOrDefaultAsync(Arg.Any<ISpecification<User>>(), Arg.Any<CancellationToken>()).Returns(user);
        _users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        return user;
    }

    [Fact]
    public async Task The_right_password_signs_in_and_resets_the_failure_count()
    {
        var user = Existing(u => u.FailedSignInCount = 2);

        var result = await Service().SignInAsync("station1", Password, TestContext.Current.CancellationToken);

        result.Succeeded.ShouldBeTrue();
        result.User!.Id.ShouldBe(user.Id);
        result.User.Role.ShouldBe(UserRole.StationUser);
        result.User.GasStationId.ShouldBe(user.GasStationId);
        user.FailedSignInCount.ShouldBe(0);
        user.LastSignInAt.ShouldBe(Now);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_wrong_password_fails_and_is_counted()
    {
        var user = Existing();

        var result = await Service().SignInAsync("station1", "nope", TestContext.Current.CancellationToken);

        result.Succeeded.ShouldBeFalse();
        result.Failure.ShouldBe(SignInFailure.InvalidCredentials);
        user.FailedSignInCount.ShouldBe(1);
    }

    [Fact]
    public async Task Too_many_wrong_passwords_lock_the_account()
    {
        var user = Existing();
        var service = Service(maxFailedAttempts: 3);

        await service.SignInAsync("station1", "nope", TestContext.Current.CancellationToken);
        await service.SignInAsync("station1", "nope", TestContext.Current.CancellationToken);
        var third = await service.SignInAsync("station1", "nope", TestContext.Current.CancellationToken);

        third.Failure.ShouldBe(SignInFailure.LockedOut);
        third.LockedOutUntil.ShouldBe(Now.AddMinutes(15));
        user.LockoutEndAt.ShouldBe(Now.AddMinutes(15));
    }

    [Fact]
    public async Task A_locked_account_refuses_even_the_right_password()
    {
        Existing(u => u.LockoutEndAt = Now.AddMinutes(5));

        var result = await Service().SignInAsync("station1", Password, TestContext.Current.CancellationToken);

        result.Failure.ShouldBe(SignInFailure.LockedOut);
    }

    [Fact]
    public async Task An_expired_lockout_no_longer_blocks()
    {
        Existing(u => u.LockoutEndAt = Now.AddMinutes(-1));

        (await Service().SignInAsync("station1", Password, TestContext.Current.CancellationToken)).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task A_deactivated_account_reads_as_wrong_credentials()
    {
        Existing(u => u.IsActive = false);

        var result = await Service().SignInAsync("station1", Password, TestContext.Current.CancellationToken);

        result.Failure.ShouldBe(SignInFailure.InvalidCredentials);
    }

    [Fact]
    public async Task An_unknown_user_reads_as_wrong_credentials_and_saves_nothing()
    {
        _users.FirstOrDefaultAsync(Arg.Any<ISpecification<User>>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await Service().SignInAsync("nobody", Password, TestContext.Current.CancellationToken);

        result.Failure.ShouldBe(SignInFailure.InvalidCredentials);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("", Password)]
    [InlineData("   ", Password)]
    [InlineData("station1", "")]
    public async Task Blank_input_is_refused_without_a_lookup(string userName, string password)
    {
        var result = await Service().SignInAsync(userName, password, TestContext.Current.CancellationToken);

        result.Failure.ShouldBe(SignInFailure.InvalidCredentials);
        await _users.DidNotReceive().FirstOrDefaultAsync(Arg.Any<ISpecification<User>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_session_with_the_current_stamp_stays_valid()
    {
        var user = Existing();

        var session = await Service().ValidateSessionAsync(user.Id, user.SecurityStamp, TestContext.Current.CancellationToken);

        session.ShouldNotBeNull().UserName.ShouldBe("station1");
    }

    [Fact]
    public async Task A_changed_security_stamp_ends_the_session()
    {
        var user = Existing();

        (await Service().ValidateSessionAsync(user.Id, Guid.NewGuid(), TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task Deactivating_a_user_ends_their_sessions()
    {
        var user = Existing(u => u.IsActive = false);

        (await Service().ValidateSessionAsync(user.Id, user.SecurityStamp, TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public void The_hasher_round_trips_and_rejects_corrupt_hashes()
    {
        Hasher.Verify(Hash, Password).ShouldBe(PasswordCheck.Success);
        Hasher.Verify(Hash, "other").ShouldBe(PasswordCheck.Failed);
        Hasher.Verify("not-base64!", Password).ShouldBe(PasswordCheck.Failed);
        Hasher.Hash(Password).ShouldNotBe(Hash, "every hash has its own salt");
    }
}
