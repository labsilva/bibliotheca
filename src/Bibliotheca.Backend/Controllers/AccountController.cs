using Bibliotheca.Backend.Data;
using Bibliotheca.Backend.Models;
using Bibliotheca.Backend.Authorization;
using Bibliotheca.Backend.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Bibliotheca.Backend.Controllers;

[Route("account")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AccountController : Controller
{
    [HttpGet("login")]
    [AllowAnonymous]
    public async Task<IActionResult> LoginAsync(
        [FromServices] BibliothecaDbContext context,
        CancellationToken cancellationToken,
        string? returnUrl = null)
    {
        var noUsers = !await context
            .Users
            .AsNoTracking()
            .AnyAsync(cancellationToken);

        returnUrl = GetLocalReturnUrl(returnUrl);
        if (noUsers)
            return RedirectToAction("register", new { returnUrl });

        ViewBag.ReturnUrl = returnUrl;

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("account-entry")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LoginPost(
        [FromServices]BibliothecaDbContext context,
        [FromServices] IPasswordHasher<User> passwordHasher,
        CancellationToken cancellationToken,
        string username, 
        string password, 
        string? returnUrl = null,
        [FromForm] bool rememberMe = false)
    {
        returnUrl = GetLocalReturnUrl(returnUrl);
        var user = string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password)
            ? null
            : await context.Users.AsTracking().FirstOrDefaultAsync(w => w.Name == username, cancellationToken);
        var verification = user is null
            ? PasswordVerificationResult.Failed
            : VerifyPassword(passwordHasher, user, password);
        if (user is null || verification == PasswordVerificationResult.Failed)
        {
            ModelState.AddModelError("", "Credenciais inválidas");
            ViewBag.ReturnUrl = returnUrl;
            var model = new LoginViewModel
            {
                Username = username,
                RememberMe = rememberMe,
                ReturnUrl = returnUrl,
                ErrorMessage = "Credenciais inválidas"
            };
            return View("Login", model);
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.Password = passwordHasher.HashPassword(user, password);
            await context.SaveChangesAsync(cancellationToken);
        }

        // Cria a identidade do utilizador autenticado
        var claims = new List<Claim>
        {
            new(OpenIddictConstants.Claims.Subject, user.Name),
            new(OpenIddictConstants.Claims.Name, user.FullName),
            new(OpenIddictConstants.Claims.Email, user.Email)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme, "sub", "roles");
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = rememberMe });

        return LocalRedirect(returnUrl ?? "/");
    }

    private static PasswordVerificationResult VerifyPassword(IPasswordHasher<User> hasher, User user, string password)
    {
        var stored = user.Password;
        if (stored.Length == 64 && stored.All(Uri.IsHexDigit))
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(stored), Convert.FromHexString(password.Sha256()))
                ? PasswordVerificationResult.SuccessRehashNeeded
                : PasswordVerificationResult.Failed;
        }

        if (!System.Buffers.Text.Base64.IsValid(stored))
            return PasswordVerificationResult.Failed;

        return hasher.VerifyHashedPassword(user, stored, password);
    }

    [HttpPost]
    [Route("/api/account/invite-code")]
    [Authorize(Policy = CataloguePolicies.Administrator)]
    public async Task<IActionResult> CreateInviteCodeAsync(
        [FromServices] FirstUserOnboardingService firstUserOnboardingService,
        CancellationToken cancellationToken)
    {
        var inviteCode = await firstUserOnboardingService.ExecuteAsync(new FirstUserOnboardingServiceArgs(), cancellationToken);
        return Ok(inviteCode);
    }

    /// <summary>Returns the current account's invitation permission.</summary>
    [HttpGet("/api/account/permissions")]
    [Authorize]
    public async Task<IActionResult> GetPermissionsAsync([FromServices] IAuthorizationService authorization)
    {
        var result = await authorization.AuthorizeAsync(User, null, CataloguePolicies.Administrator);
        return Ok(new AccountPermissionsResponse(result.Succeeded));
    }

    /// <summary>Permissions evaluated against the current server administrator configuration.</summary>
    public sealed record AccountPermissionsResponse(bool CanInvite);

    [HttpGet("register")]
    [AllowAnonymous]
    public IActionResult Register(
        string? inviteCode,
        string? returnUrl)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        var model = new RegisterViewModel
        {
            InviteCode = inviteCode,
            ReturnUrl = GetLocalReturnUrl(returnUrl),
        };
        return View(model);
    }

    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting("account-entry")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegisterAsync(
        [FromServices]UserRegistrationService userRegistrationService,
        RegisterViewModel model,
        CancellationToken cancellationToken)
    {
        model.ReturnUrl = GetLocalReturnUrl(model.ReturnUrl);
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(model.InviteCode))
        {
            model.ErrorMessage = "Preencha todos os campos obrigatórios.";
            return View(model);
        }

        var args = new UserRegistrationServiceArgs(model.InviteCode, model.Username, model.Name, model.Email, model.Password);
        var result = await userRegistrationService.ExecuteAsync(args, cancellationToken);

        if (result.Success)
        {
            return LocalRedirect(model.ReturnUrl ?? "/account/login");
        }
        else
        {
            model.ErrorMessage = result.ErrorMessage;
            return View(model);
        }
           
    }

    private string? GetLocalReturnUrl(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : null;

    [HttpGet]
    [Route("/api/account/settings")]
    [Authorize]
    public async Task<IActionResult> GetUserSettingsAsync(
        [FromServices]BibliothecaDbContext context,
        CancellationToken cancellationToken)
    {
        var username = User.GetUsername();
        var user = await context
            .Users
            .AsNoTracking()
            .FirstAsync(f => f.Name == username, cancellationToken);
        return Json(user.Settings);
    }

    [HttpPut]
    [Route("/api/account/settings")]
    [Authorize]
    public async Task<IActionResult> PutUserSettingsAsync(
        [FromServices] BibliothecaDbContext context,
        [FromBody] JsonNode userSettings,
        CancellationToken cancellationToken)
    {
        var username = User.GetUsername();
        var user = await context
            .Users
            .AsTracking()
            .FirstAsync(f => f.Name == username, cancellationToken);

        user.Settings = userSettings.ToJsonString();

        await context.SaveChangesAsync(cancellationToken);
        return Ok();
    }
}
