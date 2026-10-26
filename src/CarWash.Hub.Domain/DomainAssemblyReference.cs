namespace CarWash.Hub.Domain;

/// <summary>
/// Marker type used to locate the Domain assembly for reflection-based scanning.
/// </summary>
/// <remarks>
/// Layer: Domain. Prefer this over <c>typeof(SomeEntity).Assembly</c> so that moving or
/// renaming an entity never breaks assembly discovery.
/// </remarks>
public static class DomainAssemblyReference
{
    /// <summary>Gets the Domain assembly.</summary>
    public static System.Reflection.Assembly Assembly { get; } = typeof(DomainAssemblyReference).Assembly;
}
