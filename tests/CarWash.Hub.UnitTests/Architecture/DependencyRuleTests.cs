using System.Reflection;
using CarWash.Hub.Application;
using CarWash.Hub.Application.Abstractions.Persistence;
using CarWash.Hub.Domain;
using CarWash.Hub.Infrastructure;
using CarWash.Hub.Persistence;
using CarWash.Hub.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;

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
    private const string WebAssembly = "CarWash.Hub.Web";

    private static Assembly Web => typeof(DashboardResource).Assembly;

    private static IEnumerable<Type> Components =>
        Web.GetTypes().Where(type => typeof(ComponentBase).IsAssignableFrom(type) && !type.IsAbstract);

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

    [Fact]
    public void Web_does_not_reference_Persistence_or_Api()
    {
        // The dashboard reaches data only through Application services; the host composes it.
        var references = ReferencedAssemblyNames(Web);

        references.ShouldNotContain(PersistenceAssembly);
        references.ShouldNotContain(ApiAssembly);
    }

    [Fact]
    public void No_inner_layer_references_Web()
    {
        ReferencedAssemblyNames(DomainAssemblyReference.Assembly).ShouldNotContain(WebAssembly);
        ReferencedAssemblyNames(ApplicationAssemblyReference.Assembly).ShouldNotContain(WebAssembly);
        ReferencedAssemblyNames(InfrastructureAssemblyReference.Assembly).ShouldNotContain(WebAssembly);
        ReferencedAssemblyNames(PersistenceAssemblyReference.Assembly).ShouldNotContain(WebAssembly);
    }

    [Fact]
    public void Interactive_components_reach_application_services_only_through_the_runner()
    {
        // A circuit lives for hours; a scoped service injected into it would share one DbContext
        // across every click. Only the static account pages, which run in a request scope, may.
        var violations = Components
            .Where(component => component.GetCustomAttribute<ExcludeFromInteractiveRoutingAttribute>() is null)
            .SelectMany(component => component
                .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(property => property.GetCustomAttribute<InjectAttribute>() is not null && IsScopedDataAccess(property.PropertyType))
                .Select(property => $"{component.Name}.{property.Name} ({property.PropertyType.Name})"))
            .ToList();

        violations.ShouldBeEmpty("inject DashboardOperationRunner and call the service through it");

        static bool IsScopedDataAccess(Type type) =>
            (type.Namespace?.StartsWith("CarWash.Hub.Application.Features", StringComparison.Ordinal) ?? false)
            || type == typeof(IUnitOfWork)
            || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IGenericRepository<,>));
    }

    [Fact]
    public void Every_routable_component_states_who_may_open_it()
    {
        // The router enforces [Authorize] inside the circuit; a page with neither attribute would be
        // open to anyone already connected.
        var undecided = Components
            .Where(component => component.GetCustomAttributes<RouteAttribute>().Any())
            .Where(component => !component.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any()
                                && !component.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any())
            .Select(component => component.FullName)
            .ToList();

        undecided.ShouldBeEmpty();
    }
}
