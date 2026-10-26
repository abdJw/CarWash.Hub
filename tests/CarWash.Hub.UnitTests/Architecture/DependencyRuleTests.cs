using System.Reflection;
using CarWash.Hub.Application;
using CarWash.Hub.Domain;
using CarWash.Hub.Infrastructure;
using CarWash.Hub.Persistence;

namespace CarWash.Hub.UnitTests.Architecture;

/// <summary>
/// Enforces the Clean Architecture dependency rule at test time: inner layers never reference
/// outer ones. Project references alone prevent a direct violation, but nothing stops someone
/// adding a reference the wrong way round in a hurry - these tests make that a red build.
/// </summary>
public class DependencyRuleTests
{
    private const string DomainAssembly = "CarWash.Hub.Domain";
    private const string ApplicationAssembly = "CarWash.Hub.Application";
    private const string InfrastructureAssembly = "CarWash.Hub.Infrastructure";
    private const string PersistenceAssembly = "CarWash.Hub.Persistence";
    private const string ApiAssembly = "CarWash.Hub.Api";

    private static IReadOnlyCollection<string> ReferencedAssemblyNames(Assembly assembly) =>
        [.. assembly.GetReferencedAssemblies().Select(a => a.Name!)];

    [Fact]
    public void Domain_references_no_other_solution_layer()
    {
        var references = ReferencedAssemblyNames(DomainAssemblyReference.Assembly);

        references.ShouldNotContain(ApplicationAssembly);
        references.ShouldNotContain(InfrastructureAssembly);
        references.ShouldNotContain(PersistenceAssembly);
        references.ShouldNotContain(ApiAssembly);
    }

    [Fact]
    public void Domain_references_no_third_party_packages()
    {
        var references = ReferencedAssemblyNames(DomainAssemblyReference.Assembly);

        references.ShouldNotContain(name => name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        references.ShouldNotContain(name => name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
        references.ShouldNotContain(name => name.StartsWith("FluentValidation", StringComparison.Ordinal));
        references.ShouldNotContain(name => name.StartsWith("Serilog", StringComparison.Ordinal));
    }

    [Fact]
    public void Application_references_only_Domain()
    {
        var references = ReferencedAssemblyNames(ApplicationAssemblyReference.Assembly);

        references.ShouldContain(DomainAssembly);
        references.ShouldNotContain(InfrastructureAssembly);
        references.ShouldNotContain(PersistenceAssembly);
        references.ShouldNotContain(ApiAssembly);
    }

    [Fact]
    public void Application_has_no_dependency_on_EntityFrameworkCore_or_AspNetCore()
    {
        // The repository and specification abstractions exist so the Application layer expresses
        // queries without knowing EF Core exists. If this fails, an IQueryable or a DbContext has
        // leaked out of Persistence - or an HttpContext into the ingestion logic.
        var references = ReferencedAssemblyNames(ApplicationAssemblyReference.Assembly);

        references.ShouldNotContain(name => name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        references.ShouldNotContain(name => name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
    }

    [Fact]
    public void Infrastructure_does_not_reference_Persistence_or_Api()
    {
        var references = ReferencedAssemblyNames(InfrastructureAssemblyReference.Assembly);

        references.ShouldNotContain(PersistenceAssembly);
        references.ShouldNotContain(ApiAssembly);
    }

    [Fact]
    public void Persistence_does_not_reference_Infrastructure_or_Api()
    {
        var references = ReferencedAssemblyNames(PersistenceAssemblyReference.Assembly);

        references.ShouldNotContain(InfrastructureAssembly);
        references.ShouldNotContain(ApiAssembly);
    }
}
