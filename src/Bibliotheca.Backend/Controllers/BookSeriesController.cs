using Bibliotheca.Backend.Data;
using Bibliotheca.Backend.Models;
using Microsoft.AspNetCore.Authorization;
using Bibliotheca.Backend.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bibliotheca.Backend.Controllers;

[ApiController]
[Route("api/series")]
public class BookSeriesController : Controller
{
    [HttpGet]
    [Route("")]
    public async Task<IActionResult> IndexAsync(
        [FromServices] BibliothecaDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var series = await dbContext
            .BookSeries
            .OrderBy(o => o.Name)
            .ToArrayAsync(cancellationToken);

        return Ok(series);
    }

    [HttpPost]
    [Authorize(Policy = CataloguePolicies.Manage)]
    [Route("")]
    public async Task<IActionResult> CreateAsync(
        [FromServices] BibliothecaDbContext dbContext,
        SeriesCreateModel model,
        CancellationToken cancellationToken)
    {
        var series = new BookSeries
        {
            Name = model.Name,
        };

        try
        {
            await dbContext.BookSeries.AddAsync(series, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Ok();
        }
        catch (DbUpdateException ex)
        {
            return Problem(
                statusCode: 409,
                detail: $"Series {model.Name} already exists");
        }
        catch (Exception ex)
        {
            return Problem(
                statusCode: 500,
                detail: ex.Message);
        }
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

        var query = dbContext.BookSeries.AsNoTracking().Where(entry => entry.SeriesId == id);
        var series = await query.FirstOrDefaultAsync(cancellationToken);
        if (series is null)
            return NotFound();

        var (books, total) = await CatalogueBookPage.LoadAsync(
            query.SelectMany(entry => entry.Books), User.GetUsername(),
            orderByPublication: true, fixedStatus: null,
            page.HasValue || size.HasValue ? pagination : null, cancellationToken);
        var data = new SeriesDetailsViewModel
        {
            Id = series.SeriesId,
            Name = series.Name,
            Books = books,
            BooksTotal = total,
        };
        return Ok(data);
    }
}

public record SeriesCreateModel
{
    public string Name { get; set; }
}

public record SeriesDetailsViewModel
{
    public int Id { get; set; }
    public string Name { get; set; }
    public BookIndexItem[] Books { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? BooksTotal { get; set; }
}
