using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Bibliotheca.Backend.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Net.Http.Headers;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Bibliotheca.Backend.Tests;

public partial class HttpApplicationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BrowserSessionCookieAndTokensRespectRememberMe(bool rememberMe)
    {
        var directory = Directory.CreateTempSubdirectory("bibliotheca-session-");
        try
        {
            await using var factory = CreateFactory(directory);
            using var browser = CreateClient(factory);
            await RegisterAndLoginAsync(browser);
            var cookie = await SignInForBrowserSessionAsync(browser, "alice", rememberMe);

            Assert.True(cookie.Secure);
            Assert.True(cookie.HttpOnly);
            Assert.Equal(SameSiteMode.Lax, cookie.SameSite);
            Assert.Equal("/", cookie.Path.Value);
            Assert.False(cookie.Domain.HasValue);
            Assert.Null(cookie.MaxAge);
            if (rememberMe)
                Assert.InRange(cookie.Expires!.Value, DateTimeOffset.UtcNow.AddDays(14).AddMinutes(-1),
                    DateTimeOffset.UtcNow.AddDays(14).AddMinutes(1));
            else
                Assert.Null(cookie.Expires);

            using var api = CreateApiClient(factory, browser);
            var tokens = await GetTokensAsync(browser, api);
            var refreshed = await RefreshBrowserSessionAsync(api, ReadToken(tokens, "refresh_token"));
            Assert.NotEqual(ReadToken(tokens, "refresh_token"), ReadToken(refreshed, "refresh_token"));
            Assert.InRange(refreshed.GetProperty("expires_in").GetInt32(), 1, 900);
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BibliothecaDbContext>();
            var expirations = await db.Tokens.Where(token => token.Type == TokenTypeIdentifiers.RefreshToken)
                .Select(token => token.ExpirationDate).ToListAsync();
            Assert.Equal(2, expirations.Count);
            var deadline = Assert.Single(expirations.Distinct());
            var expected = DateTimeOffset.UtcNow + (rememberMe ? TimeSpan.FromDays(14) : TimeSpan.FromHours(8));
            Assert.InRange(deadline!.Value, expected.AddMinutes(-1), expected.AddMinutes(1));

            using var reopened = CreateApiClient(factory, browser);
            if (cookie.Expires.HasValue)
                reopened.DefaultRequestHeaders.Add("Cookie", $"{cookie.Name}={cookie.Value}");
            using var authorization = await reopened.GetAsync(CreateBrowserAuthorizationPath());
            Assert.Equal(HttpStatusCode.Redirect, authorization.StatusCode);
            var destination = new Uri(reopened.BaseAddress!, authorization.Headers.Location!);
            Assert.Equal(rememberMe ? "/oidc-callback" : "/account/login", destination.AbsolutePath);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task BrowserSessionRevokesOnlyTheReplacedOrSignedOutGrant()
    {
        var directory = Directory.CreateTempSubdirectory("bibliotheca-session-accounts-");
        try
        {
            await using var factory = CreateFactory(directory);
            using var firstBrowser = CreateClient(factory);
            using var separateBrowser = CreateClient(factory);
            using var api = CreateApiClient(factory, firstBrowser);
            await RegisterAndLoginAsync(firstBrowser);
            await SignInForBrowserSessionAsync(separateBrowser, "alice", rememberMe: true);
            var old = await GetTokensAsync(firstBrowser, api);
            var separate = await GetTokensAsync(separateBrowser, api);

            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BibliothecaDbContext>();
                var hasher = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.IPasswordHasher<User>>();
                var bob = new User { Name = "bob", FullName = "Bob Reader", Email = "bob@example.invalid", Settings = "{}" };
                bob.Password = hasher.HashPassword(bob, "private test password");
                db.Users.Add(bob);
                await db.SaveChangesAsync();
            }

            await SignInForBrowserSessionAsync(firstBrowser, "bob", rememberMe: true);
            var current = await GetTokensAsync(firstBrowser, api);
            await AssertBrowserSessionRevokedAsync(api, old);
            Assert.Equal("alice", await ReadBrowserSubjectAsync(api, separate));
            Assert.Equal("bob", await ReadBrowserSubjectAsync(api, current));

            using var logout = await firstBrowser.GetAsync(QueryHelpers.AddQueryString("/connect/logout", "id_token_hint", ReadToken(current, "id_token")));
            Assert.InRange((int)logout.StatusCode, 200, 399);
            var deleted = ReadBrowserCookie(logout);
            Assert.True(deleted.Expires < DateTimeOffset.UtcNow);
            await AssertBrowserSessionRevokedAsync(api, current);
            Assert.Equal("alice", await ReadBrowserSubjectAsync(api, separate));
            using var authorizeAfterLogout = await firstBrowser.GetAsync(CreateBrowserAuthorizationPath());
            Assert.Equal("/account/login", new Uri(firstBrowser.BaseAddress!, authorizeAfterLogout.Headers.Location!).AbsolutePath);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task BrowserSessionSurvivesProductionHostRestartWithPersistedKeys()
    {
        var directory = Directory.CreateTempSubdirectory("bibliotheca-session-restart-");
        try
        {
            const string frontend = "https://books.example.org";
            const string callback = frontend + "/oidc-callback";
            var settings = new Dictionary<string, string?>
            {
                [HostDefaults.EnvironmentKey] = "Production",
                ["ConnectionStrings:SQLiteDefault"] = $"Data Source={Path.Combine(directory.FullName, "test.db")};Pooling=False",
                ["Authentication:BootstrapCode"] = BootstrapCode,
                ["Authentication:Issuer"] = "https://localhost",
                ["Authentication:FrontendOrigin"] = frontend,
                ["Authentication:SigningCertificate:Path"] = CreateSessionCertificate(directory, "signing", X509KeyUsageFlags.DigitalSignature),
                ["Authentication:EncryptionCertificate:Path"] = CreateSessionCertificate(directory, "encryption", X509KeyUsageFlags.KeyEncipherment),
                ["Authentication:DataProtectionKeyPath"] = Path.Combine(directory.FullName, "keys"),
            };
            SetCookieHeaderValue cookie;
            JsonElement tokens;
            await using (var factory = new SessionProductionFactory(settings))
            using (var browser = CreateClient(factory))
            using (var api = CreateApiClient(factory, browser))
            {
                await RegisterAndLoginAsync(browser);
                cookie = await SignInForBrowserSessionAsync(browser, "alice", rememberMe: true);
                tokens = await ReadBrowserTokensAsync(browser, api, callback);
            }

            await using var production = new SessionProductionFactory(settings);
            using var reopened = CreateClient(production);
            using var afterRestart = CreateApiClient(production, reopened);
            reopened.DefaultRequestHeaders.Add("Cookie", $"{cookie.Name}={cookie.Value}");
            var newTokens = await ReadBrowserTokensAsync(reopened, afterRestart, callback);
            Assert.Equal("alice", await ReadBrowserSubjectAsync(afterRestart, newTokens));
            Assert.Equal("alice", await ReadBrowserSubjectAsync(afterRestart,
                await RefreshBrowserSessionAsync(afterRestart, ReadToken(tokens, "refresh_token"))));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private sealed class SessionProductionFactory(IDictionary<string, string?> settings) : WebApplicationFactory<Program>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(settings));
            return base.CreateHost(builder);
        }
    }

    private static string CreateSessionCertificate(DirectoryInfo directory, string name, X509KeyUsageFlags use)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN=Bibliotheca test {name}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(use, critical: true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var path = Path.Combine(directory.FullName, name + ".pfx");
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx));
        return path;
    }

    private static async Task<SetCookieHeaderValue> SignInForBrowserSessionAsync(HttpClient browser, string account, bool rememberMe)
    {
        var form = await browser.GetStringAsync("/account/login");
        var fields = new Dictionary<string, string>
        {
            ["Username"] = account,
            ["Password"] = "private test password",
            ["__RequestVerificationToken"] = GetFormToken(form),
        };
        if (rememberMe) fields.Add("RememberMe", "true");
        using var response = await browser.PostAsync("/account/login", new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return ReadBrowserCookie(response);
    }

    private static SetCookieHeaderValue ReadBrowserCookie(HttpResponseMessage response) => Assert.Single(
        SetCookieHeaderValue.ParseList(response.Headers.GetValues("Set-Cookie").ToArray()), cookie => cookie.Name == "idp_auth");

    private static string CreateBrowserAuthorizationPath(string callback = "http://localhost:5173/oidc-callback", string? verifier = null) =>
        QueryHelpers.AddQueryString("/connect/authorize", new Dictionary<string, string?>
        {
            ["client_id"] = "bibliotheca-web", ["redirect_uri"] = callback, ["response_type"] = "code",
            ["scope"] = "openid profile email offline_access", ["code_challenge_method"] = "S256",
            ["code_challenge"] = Base64UrlTextEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier ?? "verifier"))),
        });

    private static async Task<JsonElement> ReadBrowserTokensAsync(HttpClient browser, HttpClient api, string callback)
    {
        var verifier = Base64UrlTextEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        using var consent = await browser.GetAsync(CreateBrowserAuthorizationPath(callback, verifier));
        Assert.Equal(HttpStatusCode.Redirect, consent.StatusCode);
        var code = QueryHelpers.ParseQuery(consent.Headers.Location!.Query)["code"].ToString();
        Assert.NotEmpty(code);
        using var token = await api.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = "bibliotheca-web", ["grant_type"] = "authorization_code", ["redirect_uri"] = callback,
            ["code_verifier"] = verifier, ["code"] = code,
        }));
        token.EnsureSuccessStatusCode();
        return await token.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static Task<HttpResponseMessage> SendRefreshAsync(HttpClient api, string token) =>
        api.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = "bibliotheca-web", ["grant_type"] = "refresh_token", ["refresh_token"] = token,
        }));

    private static async Task<JsonElement> RefreshBrowserSessionAsync(HttpClient api, string token)
    {
        using var result = await SendRefreshAsync(api, token);
        result.EnsureSuccessStatusCode();
        return await result.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<string> ReadBrowserSubjectAsync(HttpClient api, JsonElement tokens)
    {
        api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ReadToken(tokens, "access_token"));
        return ReadToken(await api.GetFromJsonAsync<JsonElement>("/connect/userinfo"), "sub");
    }

    private static async Task AssertBrowserSessionRevokedAsync(HttpClient api, JsonElement tokens)
    {
        api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ReadToken(tokens, "access_token"));
        using var revoked = await api.GetAsync("/api/account/settings");
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        api.DefaultRequestHeaders.Authorization = null;
        using var refresh = await SendRefreshAsync(api, ReadToken(tokens, "refresh_token"));
        Assert.Equal(HttpStatusCode.BadRequest, refresh.StatusCode);
        Assert.Equal("invalid_grant", ReadToken(await refresh.Content.ReadFromJsonAsync<JsonElement>(), "error"));
    }

    private static string ReadToken(JsonElement tokens, string name) => tokens.GetProperty(name).GetString()!;
}
