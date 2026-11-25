using Bibliotheca.Backend.Data;
using Bibliotheca.Backend.Fetchers;
using Microsoft.EntityFrameworkCore;

namespace Bibliotheca.Backend.Services;

public sealed class StoreExternalEdition(BibliothecaDbContext database, StoreFetchedBookService store)
{
    public Task<Shelve?> FindShelfAsync(string owner, int shelfId, CancellationToken cancellationToken) =>
        database.Shelves.AsTracking().SingleOrDefaultAsync(
            shelf => shelf.ShelveId == shelfId && shelf.User.Name == owner, cancellationToken);

    public async Task<Book?> FindBookAsync(
        string providerId, string externalId, string? editionIsbn, CancellationToken cancellationToken)
    {
        var byReference = await database.BookEditionReferences
            .Where(reference => reference.ProviderId == providerId && reference.ExternalId == externalId)
            .Select(reference => reference.Book).AsTracking().SingleOrDefaultAsync(cancellationToken);
        if (byReference is not null || EditionIsbn.Normalize(editionIsbn) is not { } isbn)
            return byReference;

        var variants = EditionIsbn.CatalogueVariants(isbn).ToArray();
        return await database.Books.AsTracking()
            .Where(book => variants.Contains(book.Isbn.Replace("-", "").Replace(" ", "").ToUpper()))
            .OrderBy(book => book.BookId).FirstOrDefaultAsync(cancellationToken);
    }

    internal async Task<Book> StoreAsync(
        string providerId, ExternalEdition edition, Shelve? shelf, CancellationToken cancellationToken)
    {
        var book = await FindBookAsync(providerId, edition.ExternalId, edition.Isbn, cancellationToken);
        if (book is null)
        {
            book = await store.ProcessNewBook(new StoreFetchedBookArgs { Book = edition.ToFetchedBook() }, cancellationToken);
            if (!string.IsNullOrWhiteSpace(edition.CoverUrl))
                book.ExternalLinks.Add(new BookExternalLinks { Url = edition.CoverUrl, Notes = "External edition cover" });
        }
        else if (edition.Details is { HasStructuredAuthors: true, Authors.Length: > 0 } details)
        {
            await database.Entry(book).Collection(item => item.Authors).LoadAsync(cancellationToken);
            await AuthorCreditRepair.RepairBookAsync(database, book, [details.Authors],
                WookCatalogue.IsWookAddress(edition.SourceUrl), null, cancellationToken);
        }

        await LinkAsync(providerId, edition.ExternalId, book, shelf, cancellationToken);
        return book;
    }

    internal async Task LinkAsync(
        string providerId, string externalId, Book book, Shelve? shelf, CancellationToken cancellationToken)
    {
        if (!await database.BookEditionReferences.AnyAsync(
            reference => reference.ProviderId == providerId && reference.ExternalId == externalId, cancellationToken))
            database.BookEditionReferences.Add(new BookEditionReference
            {
                ProviderId = providerId, ExternalId = externalId, Book = book
            });

        if (shelf is not null)
            await AssignShelfAsync(book, shelf, cancellationToken);
    }

    internal async Task AssignShelfAsync(Book book, Shelve shelf, CancellationToken cancellationToken)
    {
        var entry = await database.UsersBooks.AsTracking().SingleOrDefaultAsync(
            entry => entry.UserId == shelf.UserId && entry.BookId == book.BookId, cancellationToken);
        if (entry is null)
        {
            database.UsersBooks.Add(new UserBook
            {
                UserId = shelf.UserId, Book = book, ShelveId = shelf.ShelveId, Shelve = shelf
            });
        }
        else if (entry.ShelveId != shelf.ShelveId)
        {
            entry.ShelveId = shelf.ShelveId;
            entry.Shelve = shelf;
            entry.ModifiedOn = DateTime.UtcNow;
        }
    }
}
