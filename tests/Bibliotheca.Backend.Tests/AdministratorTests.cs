using System.Security.Claims;
using Bibliotheca.Backend.Authorization;
using Bibliotheca.Backend.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using static Bibliotheca.Backend.Tests.TestDatabase;

namespace Bibliotheca.Backend.Tests;

public class AdministratorTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("1", true)]
    [InlineData("2", false)]
    [InlineData("99", false)]
    public async Task AdministratorMustBeExplicitlyConfiguredEvenForFirstUserOrManager(string? configuredId, bool expected)
    {
        await using var db = await CreateDatabaseAsync();
        db.Users.Add(new User { Id = 1, Name = "alice", FullName = "Alice", Email = "alice@example.invalid", Password = "fixture", Settings = "{}" });
        await db.SaveChangesAsync();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authorization:CatalogueManagerUserIds:0"] = "1",
            ["Authorization:AdministratorUserIds:0"] = configuredId,
        }).Build();
        var requirement = new AdministratorRequirement();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "alice")], "test"));
        var context = new AuthorizationHandlerContext([requirement], principal, null);
        await new AdministratorHandler(db, configuration).HandleAsync(context);
        Assert.Equal(expected, context.HasSucceeded);
        var anonymous = new AuthorizationHandlerContext([requirement], new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "alice")])), null);
        await new AdministratorHandler(db, configuration).HandleAsync(anonymous);
        Assert.False(anonymous.HasSucceeded);
    }
}
