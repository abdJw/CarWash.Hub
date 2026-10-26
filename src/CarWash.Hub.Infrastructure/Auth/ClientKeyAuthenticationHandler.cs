using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Application.Common.Constants;
using CarWash.Hub.Domain.Constants;
using CarWash.Hub.Infrastructure.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CarWash.Hub.Infrastructure.Auth;

/// <summary>
/// Settings of one client-key scheme: which header it reads, which keys it accepts, which scope it grants.
/// </summary>
/// <remarks>Layer: Infrastructure. Set once per scheme in <see cref="ClientKeyAuthenticationExtensions"/>.</remarks>
public sealed class ClientKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    /// <summary>Gets or sets the header carrying the key, for example <c>client-secret-key</c>.</summary>
    public string HeaderName { get; set; } = string.Empty;

    /// <summary>Gets or sets the scope a matching key grants.</summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>Gets or sets the problem code (and resource key) for a request without the header.</summary>
    public string MissingKeyCode { get; set; } = ResourceKeys.Errors.Unauthorized;

    /// <summary>Gets or sets the problem code (and resource key) for a key that matches nothing.</summary>
    public string InvalidKeyCode { get; set; } = ResourceKeys.Errors.Unauthorized;

    /// <summary>Gets or sets which of the configured lists this scheme checks.</summary>
    public Func<ClientKeyOptions, IEnumerable<ClientKeyEntry>> SelectKeys { get; set; } = _ => [];
}

/// <summary>
/// Authenticates a request by a shared key in a header.
/// </summary>
/// <remarks>
/// Layer: Infrastructure.
/// <para>
/// One handler, registered as two schemes: <c>ClientSecretKey</c> reads <c>client-secret-key</c>
/// against the ingest keys, <c>ClientReadKey</c> reads <c>client-read-key</c> against the read keys.
/// Each endpoint's policy names its scheme, so a request is only ever checked against the one key
/// that endpoint accepts.
/// </para>
/// <para>
/// <b>Constant-time comparison of equal-length digests.</b> Both the presented key and each
/// configured key are hashed with SHA-256 and compared with
/// <see cref="CryptographicOperations.FixedTimeEquals"/>, and every configured key is compared
/// even after a match. Comparing the strings directly would leak, through response timing, how
/// many leading characters of a guess were right - and its length.
/// </para>
/// <para>
/// <b>One message for every wrong key.</b> Unknown, malformed or retired keys all get the same
/// <c>Invalid</c> 401, so a caller learns nothing about why. A missing header gets its own code,
/// because that is a wiring mistake the engineer needs to see straight away.
/// </para>
/// </remarks>
/// <param name="options">The per-scheme settings.</param>
/// <param name="logger">Logger factory.</param>
/// <param name="encoder">URL encoder.</param>
/// <param name="keys">The configured keys; a monitor, so a rotated key applies without a restart.</param>
/// <param name="localizer">Resolves the 401 message in the caller's language.</param>
public sealed class ClientKeyAuthenticationHandler(
    IOptionsMonitor<ClientKeyAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptionsMonitor<ClientKeyOptions> keys,
    ILocalizationService localizer)
    : AuthenticationHandler<ClientKeyAuthenticationOptions>(options, logger, encoder)
{
    /// <summary>
    /// The longest header value considered. Anything longer is refused without being hashed, so an
    /// oversized header costs nothing.
    /// </summary>
    public const int MaxPresentedKeyLength = 512;

    /// <summary>Finds the configured key a presented value matches, in constant time per key.</summary>
    /// <param name="presented">The value from the header.</param>
    /// <param name="configured">The keys to compare against.</param>
    /// <returns>The matching entry, or <see langword="null"/>.</returns>
    public static ClientKeyEntry? Match(string presented, IEnumerable<ClientKeyEntry> configured)
    {
        ArgumentNullException.ThrowIfNull(presented);
        ArgumentNullException.ThrowIfNull(configured);

        Span<byte> presentedDigest = stackalloc byte[SHA256.HashSizeInBytes];
        Span<byte> configuredDigest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.UTF8.GetBytes(presented), presentedDigest);

        ClientKeyEntry? match = null;
        foreach (var entry in configured)
        {
            if (string.IsNullOrEmpty(entry.Key))
            {
                continue;
            }

            SHA256.HashData(Encoding.UTF8.GetBytes(entry.Key), configuredDigest);

            // No early exit: every key is compared, so the time taken does not reveal which matched.
            if (CryptographicOperations.FixedTimeEquals(presentedDigest, configuredDigest))
            {
                match ??= entry;
            }
        }

        return match;
    }

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Options.HeaderName, out var values) || string.IsNullOrEmpty(values.ToString()))
        {
            // Not this scheme's request. NoResult rather than Fail: the challenge decides the
            // message, and a missing header is reported as such.
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        // Exactly one value, of a sane length. Two copies of the header are refused rather than
        // guessed between.
        if (values.Count != 1 || values[0] is not { Length: > 0 and <= MaxPresentedKeyLength } presented)
        {
            return Task.FromResult(AuthenticateResult.Fail(Options.InvalidKeyCode));
        }

        var match = Match(presented, Options.SelectKeys(keys.CurrentValue));
        if (match is null)
        {
            // Never log the presented value: it may be a real key with a typo.
            Logger.LogWarning(
                "Rejected a request with an invalid {HeaderName} from {IpAddress}",
                Options.HeaderName,
                Context.Connection.RemoteIpAddress);
            return Task.FromResult(AuthenticateResult.Fail(Options.InvalidKeyCode));
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(CustomClaimTypes.KeyId, match.Id),
                new Claim(CustomClaimTypes.Scope, Options.Scope),
            ],
            Scheme.Name,
            nameType: CustomClaimTypes.KeyId,
            roleType: null);

        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Writes a localized problem document. Without it the framework would send an empty 401, and
    /// firmware would have nothing to log but a status code.
    /// </remarks>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var sent = Request.Headers.TryGetValue(Options.HeaderName, out var values) && !string.IsNullOrEmpty(values.ToString());
        var code = sent ? Options.InvalidKeyCode : Options.MissingKeyCode;

        return ProblemResponseWriter.WriteAsync(Context, StatusCodes.Status401Unauthorized, localizer.GetString(code), "Unauthorized", code);
    }

    /// <inheritdoc />
    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        ProblemResponseWriter.WriteAsync(
            Context,
            StatusCodes.Status403Forbidden,
            localizer.GetString(ResourceKeys.Errors.Forbidden),
            "Forbidden",
            ResourceKeys.Errors.Forbidden);
}
