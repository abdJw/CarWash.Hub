using CarWash.Hub.Application.Abstractions.Services;
using Microsoft.AspNetCore.Identity;

namespace CarWash.Hub.Infrastructure.Auth;

/// <summary>
/// <see cref="IPasswordHasher"/> over ASP.NET Core's <see cref="PasswordHasher{TUser}"/>.
/// </summary>
/// <remarks>
/// Layer: Infrastructure. The format is the Identity V3 one (PBKDF2 with HMAC-SHA512, a per-hash
/// salt and the iteration count stored in the hash), so a future increase in the iteration count
/// verifies old hashes and reports them for rehashing on the next sign-in. The hasher does not use
/// its user argument, so a placeholder type stands in for one.
/// </remarks>
public sealed class PasswordHasherAdapter : IPasswordHasher
{
    private static readonly object NoUser = new();
    private readonly PasswordHasher<object> _hasher = new();

    /// <inheritdoc />
    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        return _hasher.HashPassword(NoUser, password);
    }

    /// <inheritdoc />
    public PasswordCheck Verify(string hash, string password)
    {
        if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(password))
        {
            return PasswordCheck.Failed;
        }

        try
        {
            return _hasher.VerifyHashedPassword(NoUser, hash, password) switch
            {
                PasswordVerificationResult.Success => PasswordCheck.Success,
                PasswordVerificationResult.SuccessRehashNeeded => PasswordCheck.SuccessRehashNeeded,
                _ => PasswordCheck.Failed,
            };
        }
        catch (FormatException)
        {
            // A corrupt stored hash refuses the sign-in rather than failing the request.
            return PasswordCheck.Failed;
        }
    }
}
