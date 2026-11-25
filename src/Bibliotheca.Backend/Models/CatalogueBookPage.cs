using Bibliotheca.Backend.Data;
using Microsoft.EntityFrameworkCore;

namespace Bibliotheca.Backend.Models;

internal static class CatalogueBookPage
{
    internal static async Task<(BookIndexItem[] Books, int? Total)> LoadAsync(
        IQueryable<Book> query,
        string? username,
        bool orderByPublication,
        string? fixedStatus,
        BookPage? page,
        CancellationToken cancellationToken)
    {
        var ordered = orderByPublication
            ? query.OrderBy(book => book.PublishedOn).ThenBy(book => book.BookId)
            : query.OrderBy(book => book.BookId);
        int? total = null;
        IQueryable<Book> results = ordered;
        if (page is { } pagination)
        {
            total = await query.CountAsync(cancellationToken);
            results = ordered.Skip(pagination.Offset).Take(pagination.Size);
        }

        var books = await results.Select(book => new BookIndexItem
        {
            Title = book.Title,
            Subtitle = book.Subtitle,
            Author = string.Join(", ", book.Authors.Select(author => author.AuthorName)),
            Status = fixedStatus ?? book.Users
                .Where(entry => entry.User.Name == username)
                .Select(entry => (BookReadingStatus?)entry.ReadingStatus)
                .FirstOrDefault().ToString(),
            Id = book.BookId,
            Cover = $"/api/books/{book.BookId}/cover"
        }).ToArrayAsync(cancellationToken);
        foreach (var book in books)
        {
            if (string.IsNullOrEmpty(book.Status))
                book.Status = "Unshelved";
        }

        return (books, total);
    }
}
