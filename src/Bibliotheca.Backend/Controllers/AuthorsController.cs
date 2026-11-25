using Bibliotheca.Backend.Data;
using Bibliotheca.Backend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bibliotheca.Backend.Controllers;

[ApiController]
[Route("api/authors")]
public class AuthorsController : Controller
{
    [HttpGet]
    [Route("")]
    public async Task<IActionResult> IndexAsync(
        [FromServices] BibliothecaDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var collections = await dbContext
            .Authors
            .AsNoTracking()
            .OrderBy(o => o.AuthorName)
            .Select(a => new AuthorIndexItem(a.AuthorId, a.AuthorName, a.PhotoUrl))
            .ToArrayAsync(cancellationToken);

        return Ok(collections);
    }

    [HttpGet]
    [Route("{id}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> DetailsAsync(
        [FromServices] BibliothecaDbContext dbContext,
        int id,
        CancellationToken cancellationToken,
        [FromQuery] int? page = null,
        [FromQuery] int? size = null)
    {
        if (BookPage.From(page, size) is not { } pagination)
            return BadRequest("Page must be non-negative and size must be between 1 and 100.");

        var query = dbContext.Authors.AsNoTracking().Where(entry => entry.AuthorId == id);
        var author = await query.FirstOrDefaultAsync(cancellationToken);
        if (author is null)
            return NotFound();

        var (books, total) = await CatalogueBookPage.LoadAsync(
            query.SelectMany(entry => entry.Books), User.GetUsername(),
            orderByPublication: true, fixedStatus: null,
            page.HasValue || size.HasValue ? pagination : null, cancellationToken);
        var data = new AuthorDetailsModel
        {
            Id = author.AuthorId,
            Name = author.AuthorName,
            PhotoUrl = author.PhotoUrl,
            Biography = author.Biography,
            BiographySourceUrl = author.BiographySourceUrl,
            Books = books,
            BooksTotal = total,
        };
        return Ok(data);
    }
}

public sealed record AuthorIndexItem(int AuthorId, string AuthorName, string? PhotoUrl);
