using System.Security.Claims;
using Bibliotheca.Backend.Authorization;
using Bibliotheca.Backend.Controllers;
using Bibliotheca.Backend.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
using static Bibliotheca.Backend.Tests.TestDatabase;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Bibliotheca.Backend.Tests;

public class AuthorizationTests
{
    [Fact]
    public void AnonymousAuthorizationRequestsChallengeTheCookieScheme()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/connect/authorize";
        context.Request.QueryString = new QueryString("?client_id=bibliotheca-web");
        var controller = AuthorizationControllerFor(context);

        var challenge = Assert.IsType<ChallengeResult>(controller.Authorize());

        Assert.Equal(CookieAuthenticationDefaults.AuthenticationScheme, Assert.Single(challenge.AuthenticationSchemes));
        Assert.Equal("/connect/authorize?client_id=bibliotheca-web", challenge.Properties!.RedirectUri);
    }

    [Fact]
    public void AuthenticatedAuthorizationUsesTheCurrentSubjectAndScopes()
    {
        var context = new DefaultHttpContext { User = User("alice") };
        SetRequest(context, new OpenIddictRequest { Scope = "openid profile" });
        var controller = AuthorizationControllerFor(context);

        var result = Assert.IsType<SignInResult>(controller.Authorize());

        Assert.Equal(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, result.AuthenticationScheme);
        Assert.Equal("alice", result.Principal.GetClaim(Claims.Subject));
        Assert.Equal(new[] { Scopes.OpenId, Scopes.Profile }, result.Principal.GetScopes().Order());
        var accepted = Assert.IsType<SignInResult>(controller.Accept());
        Assert.Equal(new[] { Scopes.OpenId, Scopes.Profile }, accepted.Principal.GetScopes().Order());
    }

    [Theory]
    [InlineData(GrantTypes.AuthorizationCode)]
    [InlineData(GrantTypes.RefreshToken)]
    public async Task ExchangeKeepsTheSubjectAndSetsTheApiResource(string grantType)
    {
        var principal = User("alice");
        principal.SetScopes([Scopes.Profile]);
        principal.SetClaim(BrowserSessionEvents.DeadlineClaim, DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeSeconds());
        var authentication = new StubAuthentication(principal);
        using var services = new ServiceCollection().AddSingleton<IAuthenticationService>(authentication).BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        SetRequest(context, new OpenIddictRequest { GrantType = grantType });

        var result = Assert.IsType<SignInResult>(await AuthorizationControllerFor(context).Exchange());

        Assert.Equal(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, authentication.Scheme);
        Assert.Equal("alice", result.Principal.GetClaim(Claims.Subject));
        Assert.Equal("bibliotheca-api", Assert.Single(result.Principal.GetResources()));
        Assert.Contains(Destinations.IdentityToken, result.Principal.FindFirst(Claims.Subject)!.GetDestinations());
        Assert.Equal(Destinations.AccessToken, Assert.Single(result.Principal.FindFirst(ClaimTypes.Email)!.GetDestinations()));
        Assert.Empty(result.Principal.FindFirst(BrowserSessionEvents.DeadlineClaim)!.GetDestinations());
        Assert.InRange(result.Principal.GetRefreshTokenLifetime()!.Value, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2));
        Assert.Equal(result.Principal.GetRefreshTokenLifetime(), result.Principal.GetAccessTokenLifetime());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExchangeRejectsAnExpiredOrLegacyBrowserGrant(bool includeExpiredDeadline)
    {
        var principal = User("alice");
        if (includeExpiredDeadline)
            principal.SetClaim(BrowserSessionEvents.DeadlineClaim, DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds());
        using var services = new ServiceCollection()
            .AddSingleton<IAuthenticationService>(new StubAuthentication(principal)).BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        SetRequest(context, new OpenIddictRequest { GrantType = GrantTypes.RefreshToken });

        var result = Assert.IsType<ForbidResult>(await AuthorizationControllerFor(context).Exchange());

        Assert.Equal(Errors.InvalidGrant, result.Properties!.Items[OpenIddictServerAspNetCoreConstants.Properties.Error]);
    }

    [Fact]
    public async Task UserInfoReturnsStoredProfileOnlyForAuthenticatedUsers()
    {
        await using var db = await CreateDatabaseAsync();
        db.Users.Add(new User
        {
            Name = "alice", FullName = "Alice Reader", Email = "alice@example.invalid", Password = "test", Settings = "{}",
        });
        await db.SaveChangesAsync();
        var context = new DefaultHttpContext();
        var controller = AuthorizationControllerFor(context);
        Assert.IsType<UnauthorizedResult>(await controller.UserInfo(db, default));

        context.User = User("alice");
        context.User.SetScopes(Scopes.Profile, Scopes.Email);
        var result = Assert.IsType<JsonResult>(await controller.UserInfo(db, default));
        var profile = Assert.IsType<Dictionary<string, object?>>(result.Value);
        Assert.Equal("alice", profile["sub"]);
        Assert.Equal("Alice Reader", profile["name"]);
        Assert.Equal("alice@example.invalid", profile["email"]);
    }

    private static ClaimsPrincipal User(string subject) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.Name, subject), new Claim(Claims.Subject, subject), new Claim(ClaimTypes.Email, "alice@example.invalid")],
        "test"));

    private static AuthorizationController AuthorizationControllerFor(HttpContext context) =>
        new() { ControllerContext = new ControllerContext { HttpContext = context } };

    private static void SetRequest(HttpContext context, OpenIddictRequest request)
    {
        context.Features.Set(new OpenIddictServerAspNetCoreFeature
        {
            Transaction = new OpenIddictServerTransaction { Request = request },
        });
    }

    private sealed class StubAuthentication(ClaimsPrincipal principal) : IAuthenticationService
    {
        public string? Scheme { get; private set; }

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme)
        {
            Scheme = scheme;
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, scheme!)));
        }

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            throw new NotSupportedException();

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            throw new NotSupportedException();

        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal user, AuthenticationProperties? properties) =>
            throw new NotSupportedException();

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            throw new NotSupportedException();
    }
}
