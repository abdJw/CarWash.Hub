namespace CarWash.Hub.Infrastructure;

/// <summary>
/// Marker type used to locate the Infrastructure assembly for reflection-based scanning.
/// </summary>
/// <remarks>Layer: Infrastructure.</remarks>
public static class InfrastructureAssemblyReference
{
    /// <summary>Gets the Infrastructure assembly.</summary>
    public static System.Reflection.Assembly Assembly { get; } = typeof(InfrastructureAssemblyReference).Assembly;
}
