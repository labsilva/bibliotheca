using System.Security.Claims;
using System.Text.Json.Nodes;
using Bibliotheca.Backend.Controllers;
using Bibliotheca.Backend.Data;
using Bibliotheca.Backend.Models;
using Bibliotheca.Backend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using static Bibliotheca.Backend.Tests.TestDatabase;

namespace Bibliotheca.Backend.Tests;

public class AccountTests
{
    private static readonly PasswordHasher<User> Hasher = new();
    private static readonly IConfiguration Configuration = new ConfigurationBuilder().Build();

    [Fact]
    public async Task FirstLoginRedirectsWithoutCreatingOrDisclosingAnInvite()
    {
        await using var db = await CreateDatabaseAsync();
        var account = CreateAccountController();

        var result = Assert.IsType<RedirectToActionResult>(
            await account.LoginAsync(db, default, "/dashboard"));

        Assert.Empty(await db.UserInvites.ToListAsync());
        Assert.Equal("register", result.ActionName);
        Assert.Equal("/dashboard", result.RouteValues!["returnUrl"]);

        var registration = Assert.IsType<ViewResult>(account.Register(null, "/dashboard"));
        var model = Assert.IsType<RegisterViewModel>(registration.Model);
        Assert.Null(model.InviteCode);
        Assert.Equal("/dashboard", model.ReturnUrl);
    }

    [Fact]
    public async Task RegistrationConsumesInviteAndCreatesPrivateFavoritesShelf()
    {
        await using var db = await CreateDatabaseAsync();
        var onboarding = new FirstUserOnboardingService(db, NullLogger<FirstUserOnboardingService>.Instance);
        var registration = new UserRegistrationService(db, Hasher, Configuration);
        db.Users.Add(NewUser("existing"));
        await db.SaveChangesAsync();
        var invite = await onboarding.ExecuteAsync(new());
        var model = new RegisterViewModel
        {
            InviteCode = invite.InviteCode, Username = "alice", Name = "Alice",
            Email = "alice@example.invalid", Password = "example secret", ReturnUrl = "/dashboard",
        };
        var controller = CreateAccountController();

        var result = Assert.IsType<LocalRedirectResult>(await controller.RegisterAsync(registration, model, default));
        Assert.Equal("/dashboard", result.Url);
        db.ChangeTracker.Clear();
        var user = await db.Users.Include(row => row.Shelves).SingleAsync(user => user.Name == "alice");
        Assert.Equal("alice", user.Name);
        Assert.Equal(PasswordVerificationResult.Success, Hasher.VerifyHashedPassword(user, user.Password, model.Password));
        Assert.Equal("{}", user.Settings);
        Assert.Equal("Favorites", Assert.Single(user.Shelves).ShelveName);
        Assert.True((await db.UserInvites.SingleAsync()).Used);

        var retry = Assert.IsType<ViewResult>(await controller.RegisterAsync(registration, model, default));
        Assert.Equal("Invite code was already used", Assert.IsType<RegisterViewModel>(retry.Model).ErrorMessage);
        Assert.Equal(2, await db.Users.CountAsync());
        Assert.IsType<ViewResult>(await controller.LoginAsync(db, default));
    }

    [Theory]
    [InlineData("missing", "Invalid invite code")]
    [InlineData("expired", "Expired invite code")]
    [InlineData("used", "Invite code was already used")]
    [InlineData("valid", "That username is already taken")]
    public async Task RejectsInvalidInvitesAndDuplicateUsernames(string code, string expected)
    {
        await using var db = await CreateDatabaseAsync();
        db.Users.Add(NewUser("alice"));
        db.UserInvites.AddRange(
            new UserInvite { InviteCode = "expired", InviteExpiresOn = DateTimeOffset.Now.AddDays(-1) },
            new UserInvite { InviteCode = "used", Used = true, InviteExpiresOn = DateTimeOffset.Now.AddDays(1) },
            new UserInvite { InviteCode = "valid", InviteExpiresOn = DateTimeOffset.Now.AddDays(1) });
        await db.SaveChangesAsync();
        var registration = new UserRegistrationService(db, Hasher, Configuration);

        var result = await registration.ExecuteAsync(new(code, "alice", "Alice", "alice@example.invalid", "secret"));

        Assert.False(result.Success);
        Assert.Equal(expected, result.ErrorMessage);
        Assert.Equal(1, await db.Users.CountAsync());
        Assert.False((await db.UserInvites.SingleAsync(invite => invite.InviteCode == "valid")).Used);
    }

    [Theory]
    [InlineData("", "secret")]
    [InlineData("missing", "secret")]
    [InlineData("alice", "wrong")]
    public async Task LoginRejectsInvalidCredentials(string username, string password)
    {
        await using var db = await CreateDatabaseAsync();
        db.Users.Add(NewUser("alice"));
        await db.SaveChangesAsync();
        var controller = CreateAccountController();

        var result = Assert.IsType<ViewResult>(await controller.LoginPost(db, Hasher, default, username, password, "/dashboard"));

        Assert.False(controller.ModelState.IsValid);
        Assert.Equal("Login", result.ViewName);
        var model = Assert.IsType<LoginViewModel>(result.Model);
        Assert.Equal(username, model.Username);
        Assert.Equal("/dashboard", model.ReturnUrl);
        Assert.Equal("Credenciais inválidas", model.ErrorMessage);
    }

    [Fact]
    public async Task SettingsReadAndWriteOnlyTheAuthenticatedUser()
    {
        await using var db = await CreateDatabaseAsync();
        db.Users.AddRange(NewUser("alice"), NewUser("bob"));
        await db.SaveChangesAsync();
        var controller = CreateAccountController("alice");
        var settings = new JsonObject { ["library"] = new JsonObject { ["showShelves"] = false } };

        Assert.IsType<OkResult>(await controller.PutUserSettingsAsync(db, settings, default));

        var result = Assert.IsType<JsonResult>(await controller.GetUserSettingsAsync(db, default));
        Assert.Equal(settings.ToJsonString(), result.Value);
        Assert.Equal("{}", (await db.Users.SingleAsync(user => user.Name == "bob")).Settings);
    }

    private static User NewUser(string name) => new()
    {
        Name = name, FullName = name, Email = $"{name}@example.invalid",
        Password = "secret".Sha256(), Settings = "{}",
    };

    private static AccountController CreateAccountController(string? user = null)
    {
        var controller = new AccountController
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = user is null ? new ClaimsPrincipal() :
                        new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", user)], "test")),
                },
            },
        };
        controller.Url = new UrlHelper(new ActionContext(controller.HttpContext, new RouteData(), new ActionDescriptor()));
        return controller;
    }
}
