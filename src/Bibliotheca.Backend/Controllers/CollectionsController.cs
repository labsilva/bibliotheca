using Bibliotheca.Backend.Data;
using Bibliotheca.Backend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bibliotheca.Backend.Controllers;

[ApiController]
[Route("api/collections")]
public class CollectionsController : Controller
{
    [HttpGet]
    [Route("")]
    public async Task<IActionResult> IndexAsync(
        [FromServices] BibliothecaDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var collections = await dbContext
            .Collections
            .AsNoTracking()
            .Where(collection => collection.Books.Any())
            .OrderBy(o => o.Name)
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

        var query = dbContext.Collections.AsNoTracking().Where(entry => entry.CollectionId == id);
        var collection = await query.FirstOrDefaultAsync(cancellationToken);
        if (collection is null)
            return NotFound();

        var (books, total) = await CatalogueBookPage.LoadAsync(
            query.SelectMany(entry => entry.Books), User.GetUsername(),
            orderByPublication: false, fixedStatus: null,
            page.HasValue || size.HasValue ? pagination : null, cancellationToken);
        var data = new CollectionDetailsModel
        {
            Id = collection.CollectionId,
            Name = collection.Name,
            Books = books,
            BooksTotal = total,
        };
        return Ok(data);
    }
}
