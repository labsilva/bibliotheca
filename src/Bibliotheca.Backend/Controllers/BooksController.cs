using Bibliotheca.Backend.Jobs;
using Bibliotheca.Backend.Data;
using Bibliotheca.Backend.Models;
using Microsoft.AspNetCore.Authorization;
using Bibliotheca.Backend.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bibliotheca.Backend.Controllers;

[ApiController]
[Route("api/books")]
public class BooksController : Controller
{
    private readonly ILogger<BooksController> _logger;
    private readonly IConfiguration? _configuration;

    public BooksController(ILogger<BooksController> logger, IConfiguration? configuration = null)
    {
        _logger = logger;
        _configuration = configuration;
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [Route("")]
    public async Task<IActionResult> ListAsync(
        [FromServices] BibliothecaDbContext dbContext,
        string? list,
        int? page,
        int? size,
        CancellationToken cancellationToken,
        string? search = null)
    {
        if (BookPage.From(page, size) is not { } pagination)
            return BadRequest("Page must be non-negative and size must be between 1 and 100.");

        var username = User.GetUsername();
        var personal = list is "reading" or "read";
        if (personal && string.IsNullOrEmpty(username))
            return Challenge();

        var query = dbContext.Books.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(book => book.Title.Contains(term) ||
                book.Authors.Any(author => author.AuthorName.Contains(term)));
        }
        if (list == "unshelved")
            query = query.Where(book => !book.Users.Any());

        var readingStatus = list == "read" ? BookReadingStatus.Read : BookReadingStatus.Reading;
        if (personal)
            query = query.Where(book => book.Users.Any(userBook =>
                userBook.User.Name == username && userBook.ReadingStatus == readingStatus));

        var total = await query.CountAsync(cancellationToken);
        var ordered = list == "read"
            ? query.OrderByDescending(book => book.Users
                .Where(userBook => userBook.User.Name == username && userBook.ReadingStatus == readingStatus)
                .Max(userBook => userBook.ReadingTimestamp))
            : query.OrderByDescending(book => book.CreatedAt);

        var books = await ordered.ThenBy(book => book.BookId)
            .Skip(pagination.Offset)
            .Take(pagination.Size)
            .Select(s => new BookIndexItem
            {
                Title = s.Title,
                Subtitle = s.Subtitle,
                Author = string.Join(", ", s.Authors.Select(s => s.AuthorName)),
                Status = personal ? readingStatus.ToString() : "Unshelved",
                Id = s.BookId,
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
    [Route("{id}")]
    public async Task<IActionResult> DetailsAsync(
        [FromServices] BibliothecaDbContext dbContext,
        int id,
        CancellationToken cancellationToken)
    {
        var data = await dbContext
            .Books
            .AsNoTracking()
            .AsSplitQuery()
            .Include(i => i.Authors)
            .Include(i => i.Collection)
            .Include(i => i.Publisher)
            .Include(i => i.ExternalLinks)
            .Include(i => i.Genres)
            .Include(i => i.Series)
            .Include(i => i.Tags)
            .Where(w => w.BookId == id)
            .FirstOrDefaultAsync(cancellationToken);

        var username = User.GetUsername();
        UserBook? userBook = null;
        Shelve[]? shelves = null;
        if (!string.IsNullOrEmpty(username))
        {
            userBook = await dbContext
                .UsersBooks
                .AsNoTracking()
                .Include(i => i.Shelve)
                .FirstOrDefaultAsync(f => f.BookId == id &&
                    f.User.Name == username, cancellationToken);

            shelves = await dbContext
                .Shelves
                .AsNoTracking()
                .Where(w => w.User.Name == username)
                .ToArrayAsync(cancellationToken);
        }

        if (data is not null)
        {
            return Ok(new BookDetailsViewModel
            {
                Book = data,
                UserBook = userBook,
                Shelves = shelves,
            });
        }
        else
        {
            return NotFound();
        }
    }

    public record BookDetailsViewModel
    {
        public Book Book { get; init; }
        public UserBook? UserBook { get; init; }
        public Shelve[]? Shelves { get; init; }

        /// <summary>The reader's saved calendar date, if available.</summary>
        public DateOnly? ReadingDate => UserBook?.ReadingTimestamp is { } timestamp
            ? DateOnly.FromDateTime(timestamp)
            : null;
    }

    [HttpGet]
    [Route("list")]
    public async Task<IActionResult> ListAsync(
        [FromServices] BibliothecaDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var books = await dbContext
            .Books
            .Include(i => i.Authors)
            .Include(i => i.Collection)
            .Include(i => i.Publisher)
            .ToArrayAsync(cancellationToken);
        return Ok(books);
    }

    [HttpGet]
    [Route("isbn")]
    public async Task<IActionResult> ListIsbnAsync(
        [FromServices] BibliothecaDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var books = await dbContext
            .Books
            .Select(s => s.Isbn)
            .ToArrayAsync(cancellationToken);
        return Ok(books);
    }

    [HttpGet]
    [AllowAnonymous]
    [Route("{id}/cover")]
    public async Task<IActionResult> CoverAsync(
        [FromServices] BibliothecaDbContext dbContext,
        int id,
        CancellationToken cancellationToken)
    {
        var cover = await dbContext.Books.AsNoTracking().Where(book => book.BookId == id)
            .Select(book => book.BookCover).FirstOrDefaultAsync(cancellationToken);
        if (cover is null || cover.Cover is not { Length: > 0 } || string.IsNullOrWhiteSpace(cover.MimeType))
            return NotFound();

        var seconds = Math.Clamp(_configuration?.GetValue<int?>("CoverCache:MaxAgeSeconds") ?? 86400, 0, 31536000);
        Response.Headers.CacheControl = $"public, max-age={seconds}";
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        hash.AppendData(System.Text.Encoding.UTF8.GetBytes(cover.MimeType));
        hash.AppendData(cover.Cover);
        var etag = new Microsoft.Net.Http.Headers.EntityTagHeaderValue(
            '"' + Convert.ToHexString(hash.GetHashAndReset()) + '"');
        return File(cover.Cover, cover.MimeType, lastModified: null, entityTag: etag);
    }

    [HttpPut]
    [Route("{id}/tag")]
    [Authorize(Policy = CataloguePolicies.Manage)]
    public async Task<IActionResult> AddTagAsync(
        [FromServices] BibliothecaDbContext dbContext,
        int id,
        AddTagBody body,
        CancellationToken cancellationToken)
    {
        var book = await dbContext
            .Books
            .Include(i => i.BookCover)
            .FirstOrDefaultAsync(f => f.BookId == id, cancellationToken);
        if(book is null)
        {
            return NotFound();
        }
        var tagData = await dbContext
            .Tags
            .FirstAsync(f => f.TagId.Equals(body.TagId), cancellationToken);

        book.Tags.Add(tagData);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok();
    }

    public record AddTagBody(int TagId);

    [HttpPost]
    [Route("{id}/status")]
    [Authorize]
    public async Task<IActionResult> UpdateStatusAsync(
        [FromServices] BibliothecaDbContext dbContext,
        int id,
        UpdateStatusBody body,
        CancellationToken cancellationToken)
    {
        var book = await dbContext
            .Books
            .AsTracking()
            .Include(i => i.Users).ThenInclude(i => i.User)
            .FirstOrDefaultAsync(f => f.BookId == id, cancellationToken);

        if(book is null)
        {
            return NotFound();
        }

        var username = User.GetUsername();
        var ub = book.Users.FirstOrDefault(w => w.User.Name.Equals(username));

        if(ub is null)
        {
            ub = new UserBook
            {
                Book = book,
                Shelve = await dbContext.Shelves.FirstAsync(f => f.User.Name == username, cancellationToken),
                User = await dbContext.Users.FirstAsync(f => f.Name == username, cancellationToken)
            };
            book.Users.Add(ub);
        }

        var now = DateTime.Now;
        ub.ReadingStatus = Enum.Parse<BookReadingStatus>(body.Status);
        ub.ModifiedOn = now;
        ub.ReadingTimestamp = ub.ReadingStatus == BookReadingStatus.Read
            ? body.ReadingDate?.ToDateTime(TimeOnly.MinValue) ?? now
            : null;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok();
    }

    /// <summary>A personal reading status and, for a completed book, an optional calendar date.</summary>
    public sealed record UpdateStatusBody
    {
        public string Status { get; init; }
        public DateOnly? ReadingDate { get; init; }
    }

    [HttpPut]
    [Route("{id}/rating")]
    [Authorize]
    public async Task<IActionResult> UpdateRatingAsync(
        [FromServices] BibliothecaDbContext dbContext,
        int id,
        UpdateRatingBody body,
        CancellationToken cancellationToken)
    {
        var book = await dbContext
            .Books
            .AsTracking()
            .Include(i => i.Users).ThenInclude(i => i.User)
            .FirstOrDefaultAsync(f => f.BookId == id, cancellationToken);

        if (book is null)
        {
            return NotFound();
        }

        var username = User.GetUsername();
        var ub = book.Users.FirstOrDefault(w => w.User.Name.Equals(username));

        if (ub is null)
        {
            ub = new UserBook
            {
                Book = book,
                Shelve = await dbContext.Shelves.FirstAsync(f => f.User.Name == username, cancellationToken),
                User = await dbContext.Users.FirstAsync(f => f.Name == username, cancellationToken),
                Rating = body.Rating,
            };
            book.Users.Add(ub);
        }

        ub.Rating = body.Rating;
        ub.ModifiedOn = DateTime.Now;

        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok();
    }

    public record UpdateRatingBody
    {
        public short? Rating { get; set; }
    }

    [HttpPut]
    [Route("{id}/comments")]
    [Authorize]
    public async Task<IActionResult> UpdateCommnetsAsync(
        [FromServices] BibliothecaDbContext dbContext,
        int id,
        UpdateCommentsBody body,
        CancellationToken cancellationToken)
    {
        var book = await dbContext
            .Books
            .AsTracking()
            .Include(i => i.Users).ThenInclude(i => i.User)
            .FirstOrDefaultAsync(f => f.BookId == id, cancellationToken);

        if (book is null)
        {
            return NotFound();
        }

        var username = User.GetUsername();
        var ub = book.Users.FirstOrDefault(w => w.User.Name.Equals(username));

        if (ub is null)
        {
            ub = new UserBook
            {
                Book = book,
                Shelve = await dbContext.Shelves.FirstAsync(f => f.User.Name == username, cancellationToken),
                User = await dbContext.Users.FirstAsync(f => f.Name == username, cancellationToken),
                Comments = body.Comments,
            };
            book.Users.Add(ub);
        }

        ub.Comments = body.Comments;
        ub.ModifiedOn = DateTime.Now;

        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok();
    }

    public record UpdateCommentsBody
    {
        public string? Comments { get; set; }
    }

    [HttpPut]
    [Route("{id}/series")]
    [Authorize(Policy = CataloguePolicies.Manage)]
    public async Task<IActionResult> UpdateSeriesAsync(
        [FromServices] BibliothecaDbContext dbContext,
        int id,
        UpdateSeriesBody body,
        CancellationToken cancellationToken)
    {
        var book = await dbContext
            .Books
            .AsTracking()
            .Include(i => i.Series)
            .FirstOrDefaultAsync(f => f.BookId == id, cancellationToken);

        if (book is null)
        {
            return NotFound();
        }

        if (!string.IsNullOrEmpty(body.Series))
        {
            var series = await dbContext
            .BookSeries
            .FirstOrDefaultAsync(f => f.Name == body.Series, cancellationToken);

            if (series is null)
            {
                return NotFound();
            }
            book.Series = series;
        }
        else
        {
            book.Series = null;
        }

        book.ModifiedOn = DateTime.Now;
        
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok();
    }

    public record UpdateSeriesBody
    {
        public string Series { get; set; }
    }

    [HttpPost]
    [Route("fetch")]
    [Authorize(Policy = CataloguePolicies.Import)]
    public Task<IActionResult> FetchAsync(
        [FromServices] BookImportQueue bookImportQueue,
        string isbn,
        CancellationToken cancellationToken)
        => bookImportQueue.SubmitAsync(User.GetUsername(), [isbn], cancellationToken);

    [HttpDelete]
    [Route("{id}")]
    [Authorize(Policy = CataloguePolicies.Manage)]
    public async Task<IActionResult> DeleteBookAsync(
        [FromServices] BibliothecaDbContext dbContext,
        int id,
        CancellationToken cancellationToken)
    {
        var deleted = await dbContext.Books
            .Where(book => book.BookId == id)
            .ExecuteDeleteAsync(cancellationToken);

        if (deleted == 0)
        {
            return NotFound();
        }

        _logger.LogInformation("Deleted book {BookId} from the catalogue", id);
        return NoContent();
    }
}
