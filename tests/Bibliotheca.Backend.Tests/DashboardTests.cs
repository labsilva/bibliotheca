using System.Security.Claims;
using Bibliotheca.Backend.Controllers;
using Bibliotheca.Backend.Data;
using Bibliotheca.Backend.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bibliotheca.Backend.Tests;

public class DashboardTests
{
    [Fact]
    public async Task DashboardMatchesPersonalStatisticsAndOrderedListsOnALargeLibrary()
    {
        await using var db = await TestDatabase.CreateDatabaseAsync();
        var start = new DateTime(DateTime.Today.Year, 1, 1);
        var alice = Shelf("alice");
        var bob = Shelf("bob");
        var authors = new[] { new Author { AuthorName = "Writer A" }, new Author { AuthorName = "Writer B" } };
        object?[] pages = [120, " +28 ", "none", "10 pages", -10, null, true, 3.5, "2147483648"];
        var books = Enumerable.Range(0, 300).Select(i =>
        {
            var pageValue = pages[i / 3 % pages.Length];
            var book = new Book
            {
                BookId = i + 1, Title = $"Book {i}", Isbn = $"test-{i}", CreatedBy = "test", ModifiedBy = "test",
                CreatedAt = start.AddDays(-(i / 8)), Authors = authors.ToList(),
                AdditionalData = new() { ["description"] = new string('a', 1024) },
            };
            if (pageValue != null)
                book.AdditionalData["pages"] = pageValue;
            return new UserBook
            {
                Book = book, User = alice.User, Shelve = alice, ReadingStatus = (BookReadingStatus)(i % 3),
                ModifiedOn = i % 2 == 0 ? start.AddDays(i / 7) : null,
                ReadingTimestamp = i % 7 == 0 ? null : start.AddDays(i % 14 == 1 ? -1 : i),
                AddedToFavoritesAt = i % 3 == 0 ? start.AddDays(i) : null
            };
        }).ToArray();
        db.UsersBooks.AddRange(books);
        db.UsersBooks.Add(new UserBook
        {
            Book = books[0].Book, User = bob.User, Shelve = bob, ReadingStatus = BookReadingStatus.Read,
            ReadingTimestamp = start.AddYears(2)
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var dashboard = await LoadDashboardAsync(db, "alice");

        var completed = books.Where(book => book.ReadingStatus == BookReadingStatus.Read).ToArray();
        var months = Enumerable.Range(1, 12).Select(month => completed.Count(book =>
            book.ReadingTimestamp?.Year == start.Year && book.ReadingTimestamp.Value.Month == month));
        Assert.Equal(months, dashboard.ReadByMonth);
        Assert.Equal(months.Sum(), dashboard.CurrentYearReadBooks.Count);
        Assert.Equal(100, dashboard.ReadBooks.Count);
        Assert.Equal(100, dashboard.ReadingBooks.Count);
        Assert.Equal(100, dashboard.UnreadBooks.Count);
        Assert.Equal(12 * 120 + 11 * 28, dashboard.TotalPagesRead.Count);

        var current = books.Where(book => book.ReadingStatus == BookReadingStatus.Reading)
            .OrderByDescending(book => book.ModifiedOn).ThenBy(book => book.BookId).First();
        Assert.Equal(new DashboardBook(current.BookId, current.Book.Title, "Writer A, Writer B", "alice shelf"), dashboard.CurrentBook);
        var next = books.Where(book => book.ReadingStatus == BookReadingStatus.Unread)
            .OrderBy(book => book.Book.CreatedAt).ThenBy(book => book.BookId).Take(3);
        Assert.Equal(next.Select(book => new DashboardBook(book.BookId, book.Book.Title, "Writer A, Writer B", "alice shelf")), dashboard.NextBooks);
        var activity = completed.Where(book => book.ReadingTimestamp.HasValue)
            .Select(book => new DashboardActivity(book.BookId, book.Book.Title, book.ReadingTimestamp!.Value, "read"))
            .Concat(books.Where(book => book.AddedToFavoritesAt.HasValue)
                .Select(book => new DashboardActivity(book.BookId, book.Book.Title, book.AddedToFavoritesAt!.Value, "favorite")))
            .OrderByDescending(entry => entry.Date).ThenBy(entry => entry.Id).Take(5);
        Assert.Equal(activity, dashboard.Activity);
        Assert.Equal(new DashboardShelf("alice shelf", 300), Assert.Single(dashboard.Shelves));

        var empty = await LoadDashboardAsync(db, "no-books");
        Assert.Null(empty.CurrentBook);
        Assert.Empty(empty.NextBooks);
        Assert.Empty(empty.Activity);
        Assert.Empty(empty.Shelves);
        Assert.Equal(0, empty.TotalPagesRead.Count);
        Assert.Equal(new int[12], empty.ReadByMonth);
    }

    private static Shelve Shelf(string name) => new()
    {
        ShelveId = 1, ShelveName = $"{name} shelf",
        User = new User { Name = name, Email = $"{name}@example.invalid", FullName = name, Password = "test", Settings = "{}" }
    };

    private static async Task<DashboardModel> LoadDashboardAsync(BibliothecaDbContext db, string username)
    {
        var controller = new DashboardController
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", username)], "test"))
                }
            }
        };
        var response = Assert.IsType<OkObjectResult>(await controller.DashboardAsync(db, default));
        return Assert.IsType<DashboardModel>(response.Value);
    }
}
