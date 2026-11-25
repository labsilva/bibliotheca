using Bibliotheca.Backend.Data;
using Bibliotheca.Backend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bibliotheca.Backend.Controllers;

[ApiController]
[Route("api/genres")]
public class GenresController : Controller
{
    [HttpGet]
    [Route("")]
    public async Task<IActionResult> IndexAsync(
        [FromServices] BibliothecaDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var genres = await dbContext
            .Genres
            .Where(genre => genre.Books.Any())
            .OrderBy(genre => genre.Genre)
            .Select(genre => new GenreIndexItem(genre.BookGenreId, genre.Genre, genre.Books.Count))
            .ToArrayAsync(cancellationToken);

        return Ok(genres);
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

        var query = dbContext.Genres.AsNoTracking().Where(entry => entry.BookGenreId == id);
        var genres = await query.FirstOrDefaultAsync(cancellationToken);
        if (genres is null)
            return NotFound();

        var (books, total) = await CatalogueBookPage.LoadAsync(
            query.SelectMany(entry => entry.Books), User.GetUsername(),
            orderByPublication: true, fixedStatus: null,
            page.HasValue || size.HasValue ? pagination : null, cancellationToken);
        var data = new GenreDetailsViewModel
        {
            Id = genres.BookGenreId,
            Name = genres.Genre,
            Books = books,
            BooksTotal = total,
        };
        return Ok(data);
    }
}

public sealed record GenreIndexItem(int BookGenreId, string Genre, int BookCount);

public record GenreDetailsViewModel
{
    public int Id { get; set; }
    public string Name { get; set; }
    public BookIndexItem[] Books { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? BooksTotal { get; set; }
}
