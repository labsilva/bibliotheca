using Bibliotheca.Backend.Authorization;
using Bibliotheca.Backend.Data;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using System.Security.Claims;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Bibliotheca.Backend.Controllers;

public sealed class AuthorizationController : Controller
{
    // GET /connect/authorize
    [HttpGet("~/connect/authorize")]
    [AllowAnonymous]
    public IActionResult Authorize()
    {
        // Se não está autenticado, redireciona para o login (cookie)
        if (!User.Identity?.IsAuthenticated ?? true)
            return Challenge(new AuthenticationProperties { RedirectUri = Request.Path + Request.QueryString },
                             CookieAuthenticationDefaults.AuthenticationScheme);

        var oidcRequest = HttpContext.GetOpenIddictServerRequest()
                  ?? throw new InvalidOperationException("OpenIddict request não disponível.");

        // Cria uma identidade baseada no utilizador autenticado
        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType);

        identity.AddClaim(Claims.Subject, User.GetUsername());
        identity.AddClaim(Claims.Name, User.GetClaim(Claims.Name) ?? User.GetUsername());

        // Cria o principal e define os scopes solicitados
        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(oidcRequest.GetScopes());
        principal.SetAuthorizationId(User.GetAuthorizationId());
        principal.SetClaim(BrowserSessionEvents.DeadlineClaim, User.GetClaim(BrowserSessionEvents.DeadlineClaim));

        // Diz ao OpenIddict para emitir o code/tokens imediatamente
        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    // POST /connect/authorize (submetido após consentimento)
    [HttpPost("~/connect/authorize")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public IActionResult Accept() => Authorize();

    [Authorize(AuthenticationSchemes =
        OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)]
    [HttpGet("~/connect/userinfo")]
    public async Task<IActionResult> UserInfo(
        [FromServices]BibliothecaDbContext context,
        CancellationToken cancellationToken)
    {
        // Obtém o utilizador autenticado a partir do contexto
        var user = HttpContext.User;
        if (user?.Identity is not { IsAuthenticated: true })
            return Unauthorized();

        // Extrai claims relevantes (podes ajustar consoante o teu cenário)
        var subject = user.FindFirst(Claims.Subject)?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var dbUser = await context.Users.FirstOrDefaultAsync(w => w.Name.Equals(subject), cancellationToken);

        // Cria o objeto de resposta conforme o padrão OIDC
        if (dbUser is null)
            return Unauthorized();

        var result = new Dictionary<string, object?> { ["sub"] = subject };
        if (user.HasScope(Scopes.Profile))
            result["name"] = dbUser.FullName;
        if (user.HasScope(Scopes.Email))
            result["email"] = dbUser.Email;

        return Json(result);
    }

    [HttpGet("~/connect/logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return SignOut(new AuthenticationProperties { RedirectUri = "/" },
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }
    [HttpPost("~/connect/token")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken] // importante para chamadas OAuth/OIDC
    public async Task<IActionResult> Exchange()
    {
        var request = HttpContext.GetOpenIddictServerRequest()
                      ?? throw new InvalidOperationException("OpenIddict request não disponível.");

        // authorization_code OU refresh_token: o OpenIddict já autenticou o 'principal'
        if (request.IsAuthorizationCodeGrantType() || request.IsRefreshTokenGrantType())
        {
            var auth = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            var principal = auth.Principal ?? throw new InvalidOperationException("Principal em falta.");

            if (!long.TryParse(principal.GetClaim(BrowserSessionEvents.DeadlineClaim), out var deadline))
                return ForbidOAuth(Errors.InvalidGrant, "A new browser sign-in is required.");

            var remaining = DateTimeOffset.FromUnixTimeSeconds(deadline) - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
                return ForbidOAuth(Errors.InvalidGrant, "The browser session has expired.");

            principal.SetRefreshTokenLifetime(remaining);
            principal.SetAccessTokenLifetime(remaining < BrowserSessionEvents.AccessTokenLifetime
                ? remaining : BrowserSessionEvents.AccessTokenLifetime);

            principal.SetResources("bibliotheca-api");

            principal.SetDestinations(claim => claim.Type switch
            {
                BrowserSessionEvents.DeadlineClaim => [],
                Claims.Name => IncludeId(principal, Scopes.Profile),
                Claims.Subject => IncludeId(principal, Scopes.Profile),
                ClaimTypes.Email => IncludeId(principal, Scopes.Email),
                _ => new[] { Destinations.AccessToken }
            });

            return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        // Grant não suportado
        return ForbidOAuth(Errors.UnsupportedGrantType, $"Grant type '{request.GrantType}' não suportado.");
    }

    // Helper: destinos a incluir também no id_token se o scope existir
    private static string[] IncludeId(ClaimsPrincipal principal, string scopeWhenAlsoInIdToken) =>
        principal.HasScope(scopeWhenAlsoInIdToken)
            ? new[] { Destinations.AccessToken, Destinations.IdentityToken }
            : new[] { Destinations.AccessToken };

    // Helper: Forbid com payload OAuth padronizado
    private ForbidResult ForbidOAuth(string error, string description) =>
        Forbid(new AuthenticationProperties(new Dictionary<string, string?>
        {
            [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description
        }), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
}
