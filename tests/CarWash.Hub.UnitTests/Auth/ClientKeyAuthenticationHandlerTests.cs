using System.Text.Encodings.Web;
using System.Text.Json;
using CarWash.Hub.Application.Common.Constants;
using CarWash.Hub.Domain.Constants;
using CarWash.Hub.Infrastructure.Auth;
using CarWash.Hub.UnitTests.TestSupport;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace CarWash.Hub.UnitTests.Auth;

public class ClientKeyAuthenticationHandlerTests
{
    private const string CurrentKey = "K7f2c9d4e1a8b3f6c0d5e2a9b4c7f1e8d3a6b0c5f";
    private const string NextKey = "N7f2c9d4e1a8b3f6c0d5e2a9b4c7f1e8d3a6b0c5f";

    private static ClientKeyOptions Keys()
    {
        var keys = new ClientKeyOptions();
        keys.IngestKeys.Add(new ClientKeyEntry { Id = "2026-10", Key = CurrentKey });
        keys.IngestKeys.Add(new ClientKeyEntry { Id = "2026-11", Key = NextKey });
        return keys;
    }

    private static async Task<(ClientKeyAuthenticationHandler Handler, HttpContext Context)> CreateAsync(params string[] headerValues)
    {
        var schemeOptions = new ClientKeyAuthenticationOptions
        {
            HeaderName = HttpHeaderNames.ClientSecretKey,
            Scope = ClientScopes.Ingest,
            MissingKeyCode = ResourceKeys.Auth.ClientSecretKeyMissing,
            InvalidKeyCode = ResourceKeys.Auth.ClientSecretKeyInvalid,
            SelectKeys = keys => keys.IngestKeys,
        };

        var schemeMonitor = Substitute.For<IOptionsMonitor<ClientKeyAuthenticationOptions>>();
        schemeMonitor.Get(ClientKeySchemes.SecretKey).Returns(schemeOptions);

        var keyMonitor = Substitute.For<IOptionsMonitor<ClientKeyOptions>>();
        keyMonitor.CurrentValue.Returns(Keys());

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        if (headerValues.Length > 0)
        {
            context.Request.Headers[HttpHeaderNames.ClientSecretKey] = headerValues;
        }

        var handler = new ClientKeyAuthenticationHandler(schemeMonitor, NullLoggerFactory.Instance, UrlEncoder.Default, keyMonitor, new EchoLocalizer());
        await handler.InitializeAsync(
            new AuthenticationScheme(ClientKeySchemes.SecretKey, displayName: null, typeof(ClientKeyAuthenticationHandler)),
            context);

        return (handler, context);
    }

    [Fact]
    public void Match_finds_the_current_and_the_rotation_key_and_nothing_else()
    {
        var keys = Keys().IngestKeys;

        ClientKeyAuthenticationHandler.Match(CurrentKey, keys).ShouldNotBeNull().Id.ShouldBe("2026-10");
        ClientKeyAuthenticationHandler.Match(NextKey, keys).ShouldNotBeNull().Id.ShouldBe("2026-11");
        ClientKeyAuthenticationHandler.Match(CurrentKey + "x", keys).ShouldBeNull();
        ClientKeyAuthenticationHandler.Match(CurrentKey.ToUpperInvariant(), keys).ShouldBeNull();
        ClientKeyAuthenticationHandler.Match(string.Empty, keys).ShouldBeNull();
        ClientKeyAuthenticationHandler.Match(CurrentKey, []).ShouldBeNull();
    }

    [Fact]
    public async Task A_correct_key_authenticates_with_its_id_and_scope_but_never_the_key()
    {
        var (handler, _) = await CreateAsync(NextKey);

        var result = await handler.AuthenticateAsync();

        result.Succeeded.ShouldBeTrue();
        result.Principal!.FindFirst(CustomClaimTypes.KeyId)!.Value.ShouldBe("2026-11");
        result.Principal.FindFirst(CustomClaimTypes.Scope)!.Value.ShouldBe(ClientScopes.Ingest);
        result.Principal.Claims.ShouldNotContain(claim => claim.Value == NextKey);
    }

    [Fact]
    public async Task No_header_is_no_result_so_the_challenge_can_say_it_is_missing()
    {
        var (handler, _) = await CreateAsync();

        (await handler.AuthenticateAsync()).None.ShouldBeTrue();
    }

    [Theory]
    [InlineData("wrong-key-wrong-key-wrong-key-wrong-key")]
    [InlineData("K7f2c9d4e1a8b3f6c0d5e2a9b4c7f1e8d3a6b0c5")]
    public async Task A_wrong_key_fails(string presented)
    {
        var (handler, _) = await CreateAsync(presented);

        (await handler.AuthenticateAsync()).Failure.ShouldNotBeNull();
    }

    [Fact]
    public async Task Two_copies_of_the_header_fail_even_if_one_is_right()
    {
        var (handler, _) = await CreateAsync(CurrentKey, "other");

        (await handler.AuthenticateAsync()).Failure.ShouldNotBeNull();
    }

    [Fact]
    public async Task An_oversized_header_fails_without_being_hashed()
    {
        var (handler, _) = await CreateAsync(new string('a', ClientKeyAuthenticationHandler.MaxPresentedKeyLength + 1));

        (await handler.AuthenticateAsync()).Failure.ShouldNotBeNull();
    }

    [Theory]
    [InlineData(false, "Auth_ClientSecretKeyMissing")]
    [InlineData(true, "Auth_ClientSecretKeyInvalid")]
    public async Task The_challenge_writes_a_401_problem_naming_what_was_wrong(bool sendWrongKey, string expectedCode)
    {
        var (handler, context) = sendWrongKey ? await CreateAsync("wrong-key-wrong-key-wrong-key-wrong-key") : await CreateAsync();

        await handler.ChallengeAsync(new AuthenticationProperties());

        context.Response.StatusCode.ShouldBe(401);
        context.Response.ContentType.ShouldBe("application/problem+json");
        context.Response.Body.Position = 0;
        using var problem = await JsonDocument.ParseAsync(context.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
        problem.RootElement.GetProperty("code").GetString().ShouldBe(expectedCode);
        problem.RootElement.GetProperty("status").GetInt32().ShouldBe(401);
    }
}
