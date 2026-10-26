namespace CarWash.Hub.Application;

/// <summary>
/// Marker type for the shared string resources. Inject <c>IStringLocalizer&lt;SharedResource&gt;</c>.
/// </summary>
/// <remarks>
/// Layer: Application.
/// <para>
/// The marker pattern gives every layer one localizer to inject rather than a resource class
/// per feature, so a validator, a service and a middleware all resolve the same strings.
/// </para>
/// <para>
/// <b>It lives in the assembly root namespace on purpose.</b> With
/// <c>ResourcesPath = "Resources"</c>, ASP.NET Core looks for the embedded resource at
/// <c>&lt;type namespace with root replaced by ResourcesPath&gt;.&lt;type name&gt;</c>. Putting
/// this class in <c>...Application.Resources</c> would make it search for
/// <c>Application.Resources.Resources.SharedResource</c> - the doubled segment is a
/// well-known trap that fails silently, returning the key instead of the translation.
/// </para>
/// <para>
/// These resources live in Application, not Infrastructure, because FluentValidation
/// validators need them and Application cannot reference an outer layer. The culture
/// <em>providers</em> stay in Infrastructure.
/// </para>
/// </remarks>
public sealed class SharedResource;
