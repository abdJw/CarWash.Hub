using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Application.Exceptions;
using CarWash.Hub.Domain.Common;
using CarWash.Hub.Infrastructure.Auth;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CarWash.Hub.Web.Circuits;

/// <summary>
/// Runs each dashboard operation in a DI scope of its own, as the signed-in user.
/// </summary>
/// <remarks>
/// Layer: Web.
/// <para>
/// <b>Why every component goes through this.</b> A Blazor Server circuit is one long-lived DI
/// scope. Injecting a scoped service there would share one <c>DbContext</c> across every click for
/// hours - stale entities, and "a second operation was started on this context" as soon as two
/// awaits overlap. Here each operation gets a fresh scope, with a fresh unit of work, disposed as
/// soon as it ends.
/// </para>
/// <para>
/// <b>Who the caller is.</b> Scopes are siblings, not children, so nothing from the circuit flows
/// into the new one. The runner copies the circuit's current user - from the authentication state,
/// which revalidation keeps current, not the stale <c>HttpContext</c> - into the new scope's
/// <see cref="CurrentPrincipalHolder"/>. The station scope, the audit columns and the logs all see
/// that user.
/// </para>
/// </remarks>
public sealed class DashboardOperationRunner
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AuthenticationStateProvider _authenticationState;
    private readonly string? _ipAddress;
    private readonly string? _userAgent;

    /// <summary>Creates the runner for one circuit.</summary>
    /// <param name="scopeFactory">Creates a scope per operation.</param>
    /// <param name="authenticationState">The circuit's current user.</param>
    /// <param name="httpContextAccessor">
    /// The request that opened the circuit, read once for the address and browser - all it can be
    /// trusted for once the circuit is running.
    /// </param>
    public DashboardOperationRunner(
        IServiceScopeFactory scopeFactory,
        AuthenticationStateProvider authenticationState,
        IHttpContextAccessor httpContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(httpContextAccessor);

        _scopeFactory = scopeFactory;
        _authenticationState = authenticationState;

        var context = httpContextAccessor.HttpContext;
        _ipAddress = context?.Connection.RemoteIpAddress?.ToString();
        _userAgent = context?.Request.Headers.UserAgent.ToString();
    }

    /// <summary>Runs a read.</summary>
    /// <typeparam name="TService">The service to call.</typeparam>
    /// <typeparam name="T">What it returns.</typeparam>
    /// <param name="operation">The call.</param>
    /// <param name="cancellationToken">Cancellation token; cancel it when the component goes away.</param>
    /// <returns>The result.</returns>
    /// <remarks>Exceptions propagate: a component shows "not found" itself, and anything else reaches the error boundary.</remarks>
    public async Task<T> QueryAsync<TService, T>(Func<TService, CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
        where TService : notnull
    {
        ArgumentNullException.ThrowIfNull(operation);

        await using var scope = await CreateScopeAsync().ConfigureAwait(false);
        return await operation(scope.ServiceProvider.GetRequiredService<TService>(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs a change, turning the expected refusals into a message for the user.</summary>
    /// <typeparam name="TService">The service to call.</typeparam>
    /// <param name="operation">The call.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success, or the localized reason and any field errors.</returns>
    public async Task<CommandResult> CommandAsync<TService>(Func<TService, CancellationToken, Task> operation, CancellationToken cancellationToken = default)
        where TService : notnull
    {
        ArgumentNullException.ThrowIfNull(operation);

        await using var scope = await CreateScopeAsync().ConfigureAwait(false);
        var localizer = scope.ServiceProvider.GetRequiredService<ILocalizationService>();

        try
        {
            await operation(scope.ServiceProvider.GetRequiredService<TService>(), cancellationToken).ConfigureAwait(false);
            return CommandResult.Success;
        }
        catch (ValidationException exception)
        {
            return new CommandResult(false, localizer.GetString(exception.MessageKey, exception.MessageArguments), exception.Errors);
        }
        catch (AppException exception)
        {
            return new CommandResult(false, localizer.GetString(exception.MessageKey, exception.MessageArguments), null);
        }
        catch (DomainRuleException exception)
        {
            return new CommandResult(false, localizer.GetString(exception.Code, [.. exception.Arguments]), null);
        }
    }

    private async Task<AsyncServiceScope> CreateScopeAsync()
    {
        var state = await _authenticationState.GetAuthenticationStateAsync().ConfigureAwait(false);

        var scope = _scopeFactory.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentPrincipalHolder>()
            .Set(state.User, _ipAddress, _userAgent, Guid.CreateVersion7().ToString("N"));

        return scope;
    }
}

/// <summary>The outcome of a dashboard command.</summary>
/// <param name="Succeeded">Whether it succeeded.</param>
/// <param name="Message">Why it did not, in the user's language.</param>
/// <param name="Errors">Field errors, by field, for a validation failure.</param>
/// <remarks>Layer: Web.</remarks>
public sealed record CommandResult(bool Succeeded, string? Message, IReadOnlyDictionary<string, string[]>? Errors)
{
    /// <summary>Gets the result of a command that succeeded.</summary>
    public static CommandResult Success { get; } = new(true, null, null);
}
