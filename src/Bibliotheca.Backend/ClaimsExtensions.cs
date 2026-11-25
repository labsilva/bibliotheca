using Bibliotheca.Backend.Data;
using OpenIddict.Abstractions;
using System.Security.Claims;

namespace Bibliotheca.Backend;

public static class ClaimsExtensions
{
    public static string GetUsername(this ClaimsPrincipal principal)
    {
        return principal?.GetClaim(OpenIddictConstants.Claims.Subject) ?? string.Empty;
    }
}
