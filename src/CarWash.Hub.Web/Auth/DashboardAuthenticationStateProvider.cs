using System.Security.Claims;
using CarWash.Hub.Application.Features.Auth;
using CarWash.Hub.Domain.Constants;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CarWash.Hub.Web.Auth;

/// <summary>
/// Re-checks a live dashboard connection's user, so a deactivated user is signed out of an open
/// tab too, not only at the next page load.
/// </summary>
/// <remarks>
/// Layer: Web. A Blazor Server page can stay open for days on one connection, during which the
/// cookie is never sent again. Every <see cref="DashboardCookieOptions.RevalidationMinutes"/> the
/// user is checked in a fresh scope; a session that is no longer valid becomes anonymous, and the
/// router sends the tab to the sign-in page.
/// </remarks>
/// <param name="loggerFactory">Logger factory, for the base class.</param>
/// <param name="scopeFactory">A scope per check.</param>
/// <param name="options">The revalidation interval.</param>
public sealed class DashboardAuthenticationStateProvider(
    ILoggerFactory loggerFactory,
    IServiceScopeFactory scopeFactory,
    IOptions<DashboardCookieOptions> options) : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    /// <inheritdoc />
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(options.Value.RevalidationMinutes);

    /// <inheritdoc />
    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState authenticationState, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authenticationState);

        var principal = authenticationState.User;
        if (!Guid.TryParse(principal.FindFirstValue(CustomClaimTypes.UserId), out var userId)
            || !Guid.TryParse(principal.FindFirstValue(CustomClaimTypes.SecurityStamp), out var securityStamp))
        {
            return false;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var user = await scope.ServiceProvider.GetRequiredService<ISignInService>()
            .ValidateSessionAsync(userId, securityStamp, cancellationToken)
            .ConfigureAwait(false);

        return user is not null;
    }
}
