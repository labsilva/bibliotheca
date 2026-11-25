using Bibliotheca.Backend.Data;
using Extensions.Hosting.AsyncInitialization;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Bibliotheca.Backend.Initialization;

public class OpeniddictInitialization : IAsyncInitializer
{
    private readonly BibliothecaDbContext db;
    private readonly IOpenIddictApplicationManager appMgr;
    private readonly IOpenIddictScopeManager scopeMgr;
    private readonly IConfiguration configuration;
    private readonly IWebHostEnvironment environment;

    public OpeniddictInitialization(
        BibliothecaDbContext db,
        IOpenIddictApplicationManager appMgr,
        IOpenIddictScopeManager scopeMgr,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        this.db = db;
        this.appMgr = appMgr;
        this.scopeMgr = scopeMgr;
        this.configuration = configuration;
        this.environment = environment;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await db.Database.MigrateAsync();

        // Scopes (openid/profile/email/offline_access são registados no server, mas criamos na store para permissões)
        async Task EnsureScopeAsync(string name, string? displayName = null)
        {
            if (await scopeMgr.FindByNameAsync(name) is null)
                await scopeMgr.CreateAsync(new OpenIddictScopeDescriptor
                {
                    Name = name,
                    DisplayName = displayName ?? name
                });
        }

        await EnsureScopeAsync(Scopes.OpenId);
        await EnsureScopeAsync(Scopes.Profile);
        await EnsureScopeAsync(Scopes.Email);
        await EnsureScopeAsync(Scopes.OfflineAccess);

        // Aplicação front-end (ex.: bibliotheca-web)
        const string clientId = "bibliotheca-web";
        var origin = configuration["Authentication:FrontendOrigin"] ?? "https://books.example.org";
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var frontend) ||
            frontend.Scheme != Uri.UriSchemeHttps || frontend.AbsolutePath != "/" ||
            frontend.UserInfo.Length > 0 || frontend.Query.Length > 0 || frontend.Fragment.Length > 0)
        {
            throw new InvalidOperationException("Authentication:FrontendOrigin must be an HTTPS origin.");
        }

        var origins = environment.IsDevelopment()
            ? new[] { frontend, new Uri("http://localhost:5173/") }
            : new[] { frontend };
        var redirects = origins.Select(uri => new Uri(uri, "/oidc-callback")).ToArray();
        var existing = await appMgr.FindByClientIdAsync(clientId, cancellationToken);
        if (existing is null)
        {
            var descriptor = new OpenIddictApplicationDescriptor
            {
                ClientId = clientId,
                ConsentType = ConsentTypes.Implicit,
                DisplayName = "Bibliotheca Web",
                Permissions =
            {
                OpenIddictConstants.Permissions.Endpoints.Authorization,
                OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddictConstants.Permissions.Endpoints.EndSession,
                OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                OpenIddictConstants.Permissions.GrantTypes.RefreshToken,

                OpenIddictConstants.Permissions.ResponseTypes.Code,

                OpenIddictConstants.Permissions.Prefixes.Scope + OpenIddictConstants.Scopes.OpenId,
                OpenIddictConstants.Permissions.Prefixes.Scope + OpenIddictConstants.Scopes.Profile,
                OpenIddictConstants.Permissions.Prefixes.Scope + OpenIddictConstants.Scopes.Email
            },
                Requirements =
            {
                Requirements.Features.ProofKeyForCodeExchange // PKCE para SPAs
            }
            };
            descriptor.RedirectUris.UnionWith(redirects);
            descriptor.PostLogoutRedirectUris.UnionWith(origins);
            await appMgr.CreateAsync(descriptor, cancellationToken);
        }
        else
        {
            var descriptor = new OpenIddictApplicationDescriptor();
            await appMgr.PopulateAsync(descriptor, existing, cancellationToken);
            if (descriptor.ConsentType == ConsentTypes.Implicit &&
                descriptor.RedirectUris.SetEquals(redirects) && descriptor.PostLogoutRedirectUris.SetEquals(origins) &&
                descriptor.Permissions.Contains(Permissions.GrantTypes.RefreshToken))
                return;

            descriptor.ConsentType = ConsentTypes.Implicit;
            descriptor.Permissions.Add(Permissions.GrantTypes.RefreshToken);
            descriptor.RedirectUris.Clear();
            descriptor.RedirectUris.UnionWith(redirects);
            descriptor.PostLogoutRedirectUris.Clear();
            descriptor.PostLogoutRedirectUris.UnionWith(origins);
            await appMgr.UpdateAsync(existing, descriptor, cancellationToken);
        }
    }
}
