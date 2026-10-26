namespace CarWash.Hub.Persistence;

/// <summary>
/// Marker type used to locate the Persistence assembly for reflection-based scanning.
/// </summary>
/// <remarks>
/// Layer: Persistence. Used by <c>ModelBuilder.ApplyConfigurationsFromAssembly</c> so that adding
/// an <c>IEntityTypeConfiguration&lt;T&gt;</c> requires no change to the DbContext.
/// </remarks>
public static class PersistenceAssemblyReference
{
    /// <summary>Gets the Persistence assembly.</summary>
    public static System.Reflection.Assembly Assembly { get; } = typeof(PersistenceAssemblyReference).Assembly;
}
