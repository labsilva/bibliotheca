using Bibliotheca.Backend.Data;
using Bibliotheca.Backend.Models;
using Diacritics.Extensions;
using Microsoft.AspNetCore.Authorization;
using Bibliotheca.Backend.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Polly;
using System.Security.Cryptography.Xml;

namespace Bibliotheca.Backend.Controllers;

[ApiController]
[Route("api/tags")]
public class TagsController : Controller
{
    [HttpPost]
    [Authorize(Policy = CataloguePolicies.Manage)]
    [Route("")]
    public async Task<IActionResult> CreateTagAsync(
        [FromServices] BibliothecaDbContext dbContext,
        CreateTagBody body,
        CancellationToken cancellationToken)
    {
        var bookTag = await dbContext
            .Tags
            .Where(w => w.Tag.Equals(body.Tag))
            .FirstOrDefaultAsync(cancellationToken);
       
        bookTag ??= new BookTag
            {
                Tag = body.Tag,
            };
        dbContext.Tags.Add(bookTag);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new { tagId = bookTag.TagId, tag = body.Tag });
    }

    [HttpGet]
    [Route("")]
    public async Task<IActionResult> ListAsync(
        [FromServices] BibliothecaDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var tags = await dbContext
            .Tags
            .AsNoTracking()
            .Select(s => new
            {
                s.TagId,
                s.Tag,
            })
            .ToArrayAsync(cancellationToken);

        return Ok(tags);
    }

    [HttpGet]
    [Route("{id}/books")]
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

        var query = dbContext.Tags.AsNoTracking().Where(entry => entry.TagId == id);
        var tag = await query.FirstOrDefaultAsync(cancellationToken);
        if (tag is null)
            return NotFound();

        var (books, total) = await CatalogueBookPage.LoadAsync(
            query.SelectMany(entry => entry.Books), User.GetUsername(),
            orderByPublication: false, fixedStatus: "READ",
            page.HasValue || size.HasValue ? pagination : null, cancellationToken);
        var data = new TagDetailsModel
        {
            Id = tag.TagId,
            Name = tag.Tag,
            Books = books,
            BooksTotal = total,
        };
        return Ok(data);
    }

    public record CreateTagBody(string Tag);

    [HttpGet]
    [Route("search")]
    [ResponseCache(Duration = 5, NoStore = false, Location = ResponseCacheLocation.Client, VaryByQueryKeys = ["tag"])]
    public async Task<IActionResult> SearchTagsAsync(
        [FromServices]BibliothecaDbContext dbContext,
        string tag,
        CancellationToken cancellationToken)
    {
        var tagSearch = tag.RemoveDiacritics().ToLower();
        var tags = await dbContext
            .Tags
            .AsNoTracking()
            .Where(w => EF.Functions.Like(w.TagSearch, $"%{tagSearch}%"))
            .ToArrayAsync(cancellationToken);

        return Json(tags);
    }
}
