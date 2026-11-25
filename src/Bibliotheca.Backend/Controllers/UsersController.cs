using Bibliotheca.Backend.Authorization;
using Bibliotheca.Backend.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bibliotheca.Backend.Controllers;

[ApiController, Route("api/users"), Authorize(Policy = CataloguePolicies.Manage)]
public sealed class UsersController(BibliothecaDbContext database) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<UserDirectoryResponse>> List(string? search = null, string sort = "name",
        int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        if (page < 1 || page > 1000000 || pageSize is < 1 or > 100 || sort is not ("name" or "library"))
            return BadRequest(new ProblemDetails { Detail = "Invalid pagination or sort. Use name or library." });
        var users = database.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            var pattern = $"%{term}%";
            users = users.Where(user => EF.Functions.Like(user.Name, pattern, "\\") ||
                                        EF.Functions.Like(user.FullName, pattern, "\\"));
        }
        var total = await users.CountAsync(cancellationToken);
        var sorted = sort == "library"
            ? users.OrderByDescending(user => user.Books.Count).ThenBy(user => user.Name)
            : users.OrderBy(user => user.Name);

        // The UserBook key contains UserId and BookId, so the count includes each personal book once.
        var items = await sorted.ThenBy(user => user.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(user => new UserDirectoryEntry(user.Id, user.Name, user.FullName, user.Email,
                user.Books.Count,
                user.Books.Count(book => book.ReadingStatus == BookReadingStatus.Read),
                user.Books.Count(book => book.ReadingStatus == BookReadingStatus.Reading),
                user.Books.Count(book => book.ReadingStatus == BookReadingStatus.Unread)))
            .ToArrayAsync(cancellationToken);
        return Ok(new UserDirectoryResponse(items, total, page, pageSize));
    }
}

/// <summary>Basic account details and aggregated personal library counts.</summary>
public sealed record UserDirectoryEntry(int Id, string Username, string FullName, string Email, int BookCount,
    int Read, int Reading, int Unread);
/// <summary>A bounded page of registered accounts, available to catalogue managers.</summary>
public sealed record UserDirectoryResponse(UserDirectoryEntry[] Items, int Total, int Page, int PageSize);
