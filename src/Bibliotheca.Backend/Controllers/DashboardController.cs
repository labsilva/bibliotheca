using System.Linq.Expressions;
using Bibliotheca.Backend.Data;
using Bibliotheca.Backend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bibliotheca.Backend.Controllers;

[ApiController]
[Route("api/dashboard")]
public class DashboardController : Controller
{
    [HttpGet]
    [Route("")]
    [Authorize]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> DashboardAsync(
        [FromServices]BibliothecaDbContext context,
        CancellationToken cancellationToken)
    {
        var username = User.GetUsername();
        var userBooks = context.UsersBooks.Where(book => book.User.Name == username);
        var counts = await userBooks.GroupBy(book => book.ReadingStatus)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.Status, row => row.Count, cancellationToken);

        var completed = userBooks.Where(book => book.ReadingStatus == BookReadingStatus.Read &&
            book.ReadingTimestamp.HasValue);
        var yearStart = new DateTime(DateTime.Today.Year, 1, 1);
        var yearEnd = yearStart.AddYears(1);
        var monthlyCounts = await completed
            .Where(book => book.ReadingTimestamp >= yearStart && book.ReadingTimestamp < yearEnd)
            .GroupBy(book => book.ReadingTimestamp!.Value.Month)
            .Select(group => new { Month = group.Key, Count = group.Count() })
            .ToArrayAsync(cancellationToken);
        var months = new int[12];
        foreach (var month in monthlyCounts)
            months[month.Month - 1] = month.Count;

        var readActivity = completed.Select(book => new
        {
            Id = book.BookId, book.Book.Title, Date = book.ReadingTimestamp!.Value, Kind = "read"
        });
        var favoriteActivity = userBooks.Where(book => book.AddedToFavoritesAt.HasValue).Select(book => new
        {
            Id = book.BookId, book.Book.Title, Date = book.AddedToFavoritesAt!.Value, Kind = "favorite"
        });
        var activity = await readActivity.Concat(favoriteActivity)
            .OrderByDescending(entry => entry.Date).ThenBy(entry => entry.Id).ThenByDescending(entry => entry.Kind)
            .Take(5).Select(entry => new DashboardActivity(entry.Id, entry.Title, entry.Date, entry.Kind))
            .ToArrayAsync(cancellationToken);

        var shelves = await context.Shelves.AsNoTracking().Where(shelf => shelf.User.Name == username)
            .OrderBy(shelf => shelf.ShelveName)
            .Select(shelf => new DashboardShelf(shelf.ShelveName, shelf.Books.Count))
            .ToArrayAsync(cancellationToken);
        var cutoff = DateTime.UtcNow.AddDays(-7);
        var imports = context.BookImports.Where(import => import.Owner == username);

        var model = new DashboardModel
        {
            CurrentYearReadBooks = new DashboardPanel { Title = "Books read this year", Description = "Books in your library that been read this year", Count = months.Sum() },
            ReadBooks = new DashboardPanel { Title = "Total Read books", Description = "Books in your library that have already been read", Count = counts.GetValueOrDefault(BookReadingStatus.Read) },
            UnreadBooks = new DashboardPanel { Title = "Unread books", Description = "Books in your library that are yet to be read", Count = counts.GetValueOrDefault(BookReadingStatus.Unread) },
            ReadingBooks = new DashboardPanel { Title = "Reading books", Description = "Books in your library that are currently being read", Count = counts.GetValueOrDefault(BookReadingStatus.Reading) },
            TotalPagesRead = new DashboardPanel { Title = "Total pages read", Description = "Sum of pages in your read books", Count = await TotalPagesReadAsync(context, username, cancellationToken) },
            CurrentBook = await userBooks.Where(book => book.ReadingStatus == BookReadingStatus.Reading)
                .OrderByDescending(book => book.ModifiedOn).ThenBy(book => book.BookId)
                .Select(BookProjection).FirstOrDefaultAsync(cancellationToken),
            NextBooks = await userBooks.Where(book => book.ReadingStatus == BookReadingStatus.Unread)
                .OrderBy(book => book.Book.CreatedAt).ThenBy(book => book.BookId)
                .Take(3).Select(BookProjection).ToArrayAsync(cancellationToken),
            ReadByMonth = months,
            Activity = activity,
            Shelves = shelves,
            UnshelvedCount = await context.Books.CountAsync(book => !book.Users.Any(), cancellationToken),
            ActiveImports = await imports.CountAsync(import => import.CompletedAtUtc == null, cancellationToken),
            RecentFailedImports = await imports.CountAsync(import =>
                import.CompletedAtUtc >= cutoff && import.Failed > 0, cancellationToken)
        };

        return Ok(model);
    }

    private static readonly Expression<Func<UserBook, DashboardBook>> BookProjection = book => new DashboardBook(
        book.BookId, book.Book.Title,
        string.Join(", ", book.Book.Authors!.OrderBy(author => author.AuthorId).Select(author => author.AuthorName)),
        book.Shelve.ShelveName);

    private static async Task<int> TotalPagesReadAsync(BibliothecaDbContext context, string username, CancellationToken cancellationToken)
    {
        // AdditionalData uses a value converter. Extract only the page value, then retain the
        // catalogue's integer parsing rules for legacy metadata such as "+120" and "120 pages".
        var values = context.Database.SqlQuery<string>($"""
            SELECT CAST(json_extract(b.AdditionalData, '$.pages') AS TEXT) AS Value
            FROM UsersBooks AS ub
            JOIN Users AS u ON u.Id = ub.UserId
            JOIN Books AS b ON b.BookId = ub.BookId
            WHERE u.Name = {username} AND ub.ReadingStatus = {nameof(BookReadingStatus.Read)}
              AND json_type(b.AdditionalData, '$.pages') IN ('integer', 'text')
            """);
        var total = 0;
        await foreach (var value in values.AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            if (int.TryParse(value, out var pages) && pages > 0)
                total = checked(total + pages);
        }
        return total;
    }
}
