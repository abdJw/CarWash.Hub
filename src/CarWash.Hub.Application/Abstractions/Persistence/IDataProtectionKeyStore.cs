namespace CarWash.Hub.Application.Abstractions.Persistence;

/// <summary>
/// Stores the ASP.NET Core Data Protection key ring in the database.
/// </summary>
/// <remarks>
/// Layer: Application.
/// <para>
/// <b>Why the database.</b> Data Protection encrypts the dashboard's sign-in cookie and antiforgery
/// tokens. On shared IIS hosting the default key store is lost on every application pool recycle,
/// which would sign every user out and break every open form several times a day. The file system
/// is no better: a deploy that removes extra files deletes the keys. The database survives both.
/// </para>
/// <para>
/// Synchronous because <c>IXmlRepository</c> is; it is read once at startup and written when a key
/// rotates, every 90 days.
/// </para>
/// </remarks>
public interface IDataProtectionKeyStore
{
    /// <summary>Returns every stored key.</summary>
    /// <returns>Each key's XML.</returns>
    IReadOnlyList<string> GetAll();

    /// <summary>Stores a new key.</summary>
    /// <param name="friendlyName">The key's name, as Data Protection gives it.</param>
    /// <param name="xml">The key's XML.</param>
    void Store(string friendlyName, string xml);
}
