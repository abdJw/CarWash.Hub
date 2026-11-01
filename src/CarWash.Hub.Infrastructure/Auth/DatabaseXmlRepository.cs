using System.Xml.Linq;
using CarWash.Hub.Application.Abstractions.Persistence;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace CarWash.Hub.Infrastructure.Auth;

/// <summary>
/// Keeps the Data Protection key ring in the database, through <see cref="IDataProtectionKeyStore"/>.
/// </summary>
/// <remarks>
/// Layer: Infrastructure. Data Protection holds its repository for the life of the process, so the
/// store - which needs a scoped database context - is resolved in a scope of its own on each call.
/// </remarks>
/// <param name="scopeFactory">Creates the scope for each read or write.</param>
public sealed class DatabaseXmlRepository(IServiceScopeFactory scopeFactory) : IXmlRepository
{
    /// <inheritdoc />
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var scope = scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IDataProtectionKeyStore>();

        return [.. store.GetAll().Select(xml => XElement.Parse(xml))];
    }

    /// <inheritdoc />
    public void StoreElement(XElement element, string friendlyName)
    {
        ArgumentNullException.ThrowIfNull(element);

        using var scope = scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<IDataProtectionKeyStore>()
            .Store(friendlyName, element.ToString(SaveOptions.DisableFormatting));
    }
}
