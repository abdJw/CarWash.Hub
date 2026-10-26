namespace CarWash.Hub.Application;

/// <summary>
/// Marker type used to locate the Application assembly for reflection-based scanning
/// (FluentValidation validator discovery).
/// </summary>
/// <remarks>Layer: Application.</remarks>
public static class ApplicationAssemblyReference
{
    /// <summary>Gets the Application assembly.</summary>
    public static System.Reflection.Assembly Assembly { get; } = typeof(ApplicationAssemblyReference).Assembly;
}
