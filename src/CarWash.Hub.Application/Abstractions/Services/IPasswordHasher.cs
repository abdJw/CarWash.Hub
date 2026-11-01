namespace CarWash.Hub.Application.Abstractions.Services;

/// <summary>Hashes and verifies dashboard passwords.</summary>
/// <remarks>
/// Layer: Application. Implemented in Infrastructure over ASP.NET Core's <c>PasswordHasher</c>
/// (PBKDF2-SHA512, salted, versioned), so the Application layer never references ASP.NET Core.
/// </remarks>
public interface IPasswordHasher
{
    /// <summary>Hashes a password.</summary>
    /// <param name="password">The password.</param>
    /// <returns>The hash, including its salt and format version.</returns>
    string Hash(string password);

    /// <summary>Checks a password against a stored hash.</summary>
    /// <param name="hash">The stored hash.</param>
    /// <param name="password">The password offered.</param>
    /// <returns>Whether it matches, and whether the hash should be upgraded.</returns>
    PasswordCheck Verify(string hash, string password);
}

/// <summary>The outcome of a password check.</summary>
/// <remarks>Layer: Application.</remarks>
public enum PasswordCheck
{
    /// <summary>The password does not match.</summary>
    Failed,

    /// <summary>The password matches.</summary>
    Success,

    /// <summary>The password matches, but the hash uses an older format and should be replaced.</summary>
    SuccessRehashNeeded,
}

/// <summary>Converts instants to the business time zone the dashboard shows.</summary>
/// <remarks>
/// Layer: Application. Everything is stored in UTC; "today", charts by day and the times on screen
/// are in this zone (<c>Dashboard:TimeZone</c>, Asia/Amman by default).
/// </remarks>
public interface IBusinessTimeZone
{
    /// <summary>Gets the zone.</summary>
    TimeZoneInfo Zone { get; }

    /// <summary>Converts an instant to the business zone.</summary>
    /// <param name="instant">The instant.</param>
    /// <returns>The same instant, at the business zone's offset.</returns>
    DateTimeOffset ToLocal(DateTimeOffset instant);

    /// <summary>Returns the start of the business day containing an instant.</summary>
    /// <param name="instant">The instant.</param>
    /// <returns>Local midnight of that day, at the business zone's offset.</returns>
    DateTimeOffset StartOfDay(DateTimeOffset instant);
}
