using Microsoft.AspNetCore.Authentication.Cookies;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Bibliotheca.Backend.Authorization;

// One grant per password sign-in, shared by the OIDC exchanges from this browser.
public sealed class BrowserSessionEvents(
    IOpenIddictApplicationManager applications,
    IOpenIddictAuthorizationManager authorizations) : CookieAuthenticationEvents
{
    public const string DeadlineClaim = "bibliotheca_session_expires";
    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(15);

    public override async Task SigningIn(CookieSigningInContext context)
    {
        await RevokeCurrentGrantAsync(context.HttpContext);
        var ct = context.HttpContext.RequestAborted;
        var client = await applications.FindByClientIdAsync("bibliotheca-web", ct)
            ?? throw new InvalidOperationException("The web client has not been initialized.");
        var principal = context.Principal!;
        var grant = await authorizations.CreateAsync(new OpenIddictAuthorizationDescriptor
        {
            ApplicationId = await applications.GetIdAsync(client, ct),
            Subject = principal.GetClaim(Claims.Subject),
            Type = AuthorizationTypes.AdHoc,
            Status = Statuses.Valid,
            Scopes = { Scopes.OpenId, Scopes.Profile, Scopes.Email, Scopes.OfflineAccess },
        }, ct);

        if (context.Properties.IsPersistent)
            context.Properties.ExpiresUtc = context.Properties.IssuedUtc!.Value.AddDays(14);
        principal.SetAuthorizationId(await authorizations.GetIdAsync(grant, ct));
        principal.SetClaim(DeadlineClaim, context.Properties.ExpiresUtc!.Value.ToUnixTimeSeconds());
    }

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var ct = context.HttpContext.RequestAborted;
        var id = context.Principal?.GetAuthorizationId();
        if (id is null || await authorizations.FindByIdAsync(id, ct) is not { } grant ||
            !await authorizations.HasStatusAsync(grant, Statuses.Valid, ct))
            context.RejectPrincipal();
    }

    public override Task SigningOut(CookieSigningOutContext context) => RevokeCurrentGrantAsync(context.HttpContext);

    private async Task RevokeCurrentGrantAsync(HttpContext context)
    {
        var ct = context.RequestAborted;
        if (context.User.GetAuthorizationId() is { } id &&
            await authorizations.FindByIdAsync(id, ct) is { } grant &&
            !await authorizations.TryRevokeAsync(grant, ct) && await authorizations.HasStatusAsync(grant, Statuses.Valid, ct))
            throw new InvalidOperationException("Could not revoke the previous browser session.");
    }
}
