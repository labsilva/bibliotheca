using Bibliotheca.Backend.Data;
using Bibliotheca.Backend.Models;
using Bibliotheca.Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Bibliotheca.Backend.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bibliotheca.Backend.Controllers;

[ApiController]
[Route("/api/library")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class LibrariesController : Controller
{
    [HttpGet]
    [Route("")]
    public async Task<IActionResult> IndexAsync(
        [FromServices]BibliothecaDbContext context,
        CancellationToken cancellationToken)
    {
        var data = await context
            .Users
            .AsNoTracking()
            .GroupBy(g => g.FullName, e => new { e.FullName, e.Id, Total = e.Books.Count() })
            .Select(s => new LibraryIndexViewModelItem
            {
                LibraryId = s.First().Id,
                Library = s.First().FullName,
                BookCount = s.First().Total
            })
            .ToArrayAsync(cancellationToken);
        return Ok(new LibraryIndexViewModel
        {
            Libraries = data
        });
    }

    public record LibraryIndexViewModel
    {
        public LibraryIndexViewModelItem[] Libraries { get; init; }
    }

    public record LibraryIndexViewModelItem
    {
        public int LibraryId { get; init; }
        public string Library { get; init; }
        public int BookCount { get; init; }
    }

    [HttpGet]
    [Route("books")]
    [Authorize]
    public async Task<IActionResult> ListAsync(
        [FromServices] BibliothecaDbContext dbContext,
        string? list,
        int? page,
        int? size,
        CancellationToken cancellationToken)
    {
        if (BookPage.From(page, size) is not { } pagination)
            return BadRequest("Page must be non-negative and size must be between 1 and 100.");

        var username = User.GetUsername();
        var query = dbContext.UsersBooks.AsNoTracking()
            .Where(userBook => userBook.User.Name == username);
        query = list switch
        {
            "unread" => query.Where(userBook => userBook.ReadingStatus == BookReadingStatus.Unread),
            "reading" => query.Where(userBook => userBook.ReadingStatus == BookReadingStatus.Reading),
            "read" => query.Where(userBook => userBook.ReadingStatus == BookReadingStatus.Read),
            _ => query
        };

        var total = await query.CountAsync(cancellationToken);
        var ordered = list == "read"
            ? query.OrderByDescending(userBook => userBook.ReadingTimestamp)
            : query.OrderByDescending(userBook => userBook.Book.CreatedAt);

        var books = await ordered.ThenBy(userBook => userBook.BookId)
            .Skip(pagination.Offset)
            .Take(pagination.Size)
            .Select(s => new BookIndexItem
            {
                Title = s.Book.Title,
                Subtitle = s.Book.Subtitle,
                Author = string.Join(", ", s.Book.Authors.Select(s => s.AuthorName)),
                Status = s.ReadingStatus.ToString(),
                Id = s.BookId,
                Series = s.Book.Series != null ? s.Book.Series!.Name : null,
                Cover = $"/api/books/{s.BookId}/cover"
            }).ToArrayAsync(cancellationToken);

        return Ok(new PaginatedResult<BookIndexItem>
        {
            Items = books,
            Page = pagination.Index,
            Total = total
        });
    }

    [HttpGet]
    [Route("shelves")]
    [Authorize]
    public async Task<IActionResult> ShelvesAsync(
        [FromServices] BibliothecaDbContext context,
        CancellationToken cancellationToken)
    {
        var username = User.GetUsername();
        var data = await context
            .Shelves
            .AsNoTracking()
            .AsSplitQuery()
            .Include(i => i.User)
            .Where(w => w.User.Name.Equals(username))
            .Select(s => new
            {
                LibraryId = s.User.Id,
                Library = s.User.FullName,
                ShelveId = s.ShelveId,
                Shelve = s.ShelveName,
                BookCount = s.Books.Count(),
            })
            .GroupBy(g => new
            {
                g.LibraryId,
                g.Library,
                g.ShelveId,
                g.Shelve
            }, e => new
            {
                e.LibraryId,
                e.Library,
                e.ShelveId,
                e.Shelve,
                e.BookCount
            })
            .ToArrayAsync(cancellationToken);

        return Ok(new ShelvesIndexViewModel
        {
            LibraryId = data.First().Key.LibraryId,
            Library = data.First().Key.Library,
            Shelves = data.Select(s => new ShelvesIndexViewModelItem
            {
                ShelveId = s.Key.ShelveId,
                Shelve = s.Key.Shelve,
                BookCount = s.First().BookCount
            }).ToArray()
        });
    }

    public record ShelvesIndexViewModel
    {
        public int LibraryId { get; init; }
        public string Library { get; init; }
        public ShelvesIndexViewModelItem[] Shelves { get; init; }
    }

    public record ShelvesIndexViewModelItem
    {
        public int ShelveId { get; init; }
        public string Shelve { get; init; }
        public int BookCount { get; init; }
    }

    [HttpPost]
    [Route("shelves/{name}")]
    [Authorize]
    public async Task<IActionResult> AddShelveAsync(
        [FromServices] BibliothecaDbContext context,
        string name,
        CancellationToken cancellationToken)
    {
        var username = User.GetUsername();
        var user = await context
            .Users
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Name.Equals(username), cancellationToken);

        if (user == null) 
        {
            return NotFound();
        }

        var shelve = new Shelve
        {
            ShelveId = (await context.Shelves.CountAsync(cancellationToken)) + 1,
            UserId = user.Id,
            ShelveName = name
        };
        await context.Shelves.AddAsync(shelve, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        return Ok();
    }

    [HttpGet]
    [Route("shelves/{name}/books")]
    [Authorize]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetBooksFromShelfAsync(
        [FromServices] BibliothecaDbContext context,
        string name,
        CancellationToken cancellationToken,
        [FromQuery] int? page = null,
        [FromQuery] int? size = null)
    {
        if (BookPage.From(page, size) is not { } pagination)
            return BadRequest("Page must be non-negative and size must be between 1 and 100.");

        var username = User.GetUsername();
        var shelf = context.Shelves.AsNoTracking()
            .Where(entry => entry.ShelveName == name && entry.User.Name == username);
        if (!await shelf.AnyAsync(cancellationToken))
            return NotFound();

        var query = shelf.SelectMany(entry => entry.Books);
        var ordered = query.OrderBy(entry => entry.Book.Title).ThenBy(entry => entry.BookId);
        var paged = page.HasValue || size.HasValue;
        var total = paged ? await query.CountAsync(cancellationToken) : 0;
        var content = await (paged ? ordered.Skip(pagination.Offset).Take(pagination.Size) : ordered)
            .Select(entry => new BookIndexItem
            {
                Title = entry.Book.Title,
                Subtitle = entry.Book.Subtitle,
                Author = string.Join(", ", entry.Book.Authors.Select(author => author.AuthorName)),
                AuthorId = entry.Book.Authors.Select(author => (int?)author.AuthorId).FirstOrDefault(),
                Status = entry.ReadingStatus.ToString(),
                Id = entry.BookId,
                SeriesId = entry.Book.Series == null ? null : entry.Book.Series.SeriesId,
                Series = entry.Book.Series == null ? null : entry.Book.Series.Name,
                Cover = $"/api/books/{entry.BookId}/cover"
            })
            .ToArrayAsync(cancellationToken);

        if (!paged)
            return Ok(content);

        return Ok(new PaginatedResult<BookIndexItem>
        {
            Items = content,
            Page = pagination.Index,
            Total = total
        });
    }

    [HttpPut]
    [Route("shelves/{name}/books/{bookId}")]
    [Authorize]
    public async Task<IActionResult> AddOrUpdateBookToShelfAsync(
        [FromServices] BibliothecaDbContext context,
        string name,
        int bookId,
        CancellationToken cancellationToken)
    {
        var username = User.GetUsername();

        var shelve = await context
            .Shelves
            .FirstOrDefaultAsync(f => f.ShelveName == name && f.User.Name == username, cancellationToken);

        if(shelve is null)
        {
            return NotFound();
        }

        var user = await context
            .Users
            .FirstAsync(f => f.Name == username, cancellationToken);

        var userBook = await context
            .UsersBooks
            .AsTracking()
            .Include(book => book.Shelve)
            .FirstOrDefaultAsync(f => f.BookId == bookId && f.UserId == user.Id, cancellationToken);

        var addedToFavorites = shelve.IsFavorites && userBook?.Shelve?.IsFavorites != true;
        if(userBook is null)
        {
            // Add to shelf
            userBook = new UserBook
            {
                BookId = bookId,
                Shelve = shelve,
                User = user,
                ReadingStatus = BookReadingStatus.Unread,
                ModifiedOn = DateTime.Now,
                Favorite = false,
            };
            await context.UsersBooks.AddAsync(userBook, cancellationToken);
        }
        else
        {
            // Update shelf
            userBook.Shelve = shelve;
        }

        if (addedToFavorites)
            userBook.AddedToFavoritesAt = DateTime.Now;

        try
        {

            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            return Problem(
                statusCode: 500,
                detail: ex.Message);
        }

        return Ok();
    }

    [HttpDelete]
    [Route("shelves/{name}/books/{bookId}")]
    [Authorize]
    public async Task<IActionResult> RemoveBookFromShelfAsync(
        [FromServices] BibliothecaDbContext context,
        string name,
        int bookId,
        CancellationToken cancellationToken)
    {
        var username = User.GetUsername();

        var user = await context
            .Users
            .FirstAsync(f => f.Name == username, cancellationToken);

        var userBook = await context
            .UsersBooks
            .FirstOrDefaultAsync(f => f.User.Name == username &&
                f.Shelve.ShelveName == name && 
                f.BookId == bookId, cancellationToken);

        if(userBook is null)
        {
            return NotFound();
        }
        
        try
        {
            context.UsersBooks.Remove(userBook);
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            return Problem(
                statusCode: 500,
                detail: ex.Message);
        }

        return Ok();
    }

    [HttpPost]
    [Route("_upload")]
    [RequestSizeLimit(1024 * 1024)]
    [Authorize(Policy = CataloguePolicies.Import)]
    public async Task<IActionResult> UploadBooksAsync(
        [FromServices] IsbnFileUploadPreProcessService preProcessService,
        [FromServices] IsbnFileUploadProcessService isbnFileUploadProcess,
        LibraryUploadModel model,
        CancellationToken cancellationToken)
    {
        if(model.File is null)
        {
            return Problem("No file provided");
        }

        // Parse file type
        var preProcessResult = await preProcessService.ExecuteAsync(new IsbnFileUploadPreProcessArgs { File = model.File }, cancellationToken);
        if(!preProcessResult.Success)
        {
            return Problem(
                preProcessResult.ProblemDetails!.Detail,
                preProcessResult.ProblemDetails!.Instance,
                preProcessResult.ProblemDetails!.Status,
                preProcessResult.ProblemDetails!.Title,
                preProcessResult.ProblemDetails!.Type,
                preProcessResult.ProblemDetails!.Extensions);
        }

        return await isbnFileUploadProcess.ExecuteAsync(new IsbnFileUploadProcessArgs
        {
            File = model.File,
            Separator = preProcessResult.Separator,
            Owner = User.GetUsername()
        }, cancellationToken);
    }

    public record LibraryUploadModel
    {
        public int LibraryId { get; set; }
        public IFormFile File { get; set; }
    }
}
