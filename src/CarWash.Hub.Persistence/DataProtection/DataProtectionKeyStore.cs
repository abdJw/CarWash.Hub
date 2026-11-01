using CarWash.Hub.Application.Abstractions.Persistence;
using CarWash.Hub.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace CarWash.Hub.Persistence.DataProtection;

/// <summary>EF Core implementation of <see cref="IDataProtectionKeyStore"/>.</summary>
/// <remarks>Layer: Persistence. Synchronous, as Data Protection's repository contract is.</remarks>
/// <param name="context">The database context.</param>
public sealed class DataProtectionKeyStore(ApplicationDbContext context) : IDataProtectionKeyStore
{
    /// <inheritdoc />
    public IReadOnlyList<string> GetAll() =>
        [.. context.Set<DataProtectionKey>().AsNoTracking().OrderBy(key => key.Id).Select(key => key.Xml)];

    /// <inheritdoc />
    public void Store(string friendlyName, string xml)
    {
        ArgumentException.ThrowIfNullOrEmpty(xml);

        context.Set<DataProtectionKey>().Add(new DataProtectionKey { FriendlyName = friendlyName, Xml = xml });
        context.SaveChanges();
    }
}
