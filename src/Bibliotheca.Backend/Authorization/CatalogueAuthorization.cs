using Bibliotheca.Backend.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Bibliotheca.Backend.Authorization;

public static class CataloguePolicies
{
    public const string Import = "ImportBooks";
    public const string Manage = "ManageCatalogue";
    public const string Administrator = "Administrator";
}

public sealed class AdministratorRequirement : IAuthorizationRequirement { }

public sealed class AdministratorHandler(BibliothecaDbContext database, IConfiguration configuration)
    : AuthorizationHandler<AdministratorRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, AdministratorRequirement requirement)
    {
        var subject = context.User.GetUsername();
        var allowedIds = configuration.GetSection("Authorization:AdministratorUserIds").GetChildren()
            .Select(entry => int.TryParse(entry.Value, out var id) ? id : 0).Where(id => id > 0).ToArray();
        if (context.User.Identity?.IsAuthenticated == true && subject.Length > 0 &&
            await database.Users.AnyAsync(user => user.Name == subject && allowedIds.Contains(user.Id)))
            context.Succeed(requirement);
    }
}

public sealed class ManageCatalogueRequirement : IAuthorizationRequirement
{
}

public sealed class ManageCatalogueHandler(BibliothecaDbContext database, IConfiguration configuration)
    : AuthorizationHandler<ManageCatalogueRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, ManageCatalogueRequirement requirement)
    {
        string subject = context.User.GetUsername();
        var allowedIds = configuration.GetSection("Authorization:CatalogueManagerUserIds").Get<int[]>() ?? [];
        if (context.User.Identity?.IsAuthenticated == true && subject.Length > 0 && allowedIds.Length > 0 &&
            await database.Users.AnyAsync(user => user.Name == subject && allowedIds.Contains(user.Id)))
        {
            context.Succeed(requirement);
        }
    }
}
