using static Bibliotheca.Backend.Tests.TestDatabase;
using System.Security.Claims;
using Bibliotheca.Backend.Controllers;
using Bibliotheca.Backend.Data;
using Bibliotheca.Backend.Fetchers;
using Bibliotheca.Backend.Models;
using Bibliotheca.Backend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bibliotheca.Backend.Tests;

public class CatalogueTests
{
    [Fact]
    public async Task DetailsReturnOnlyCurrentUsersReadingDataAndShelves()
    {
        await using var db = await CreateDatabaseAsync();
        var (book, alice, bob) = await SeedBookAsync(db);
        var controller = BooksControllerFor(alice.User.Name);

        var response = await controller.DetailsAsync(db, book.BookId, default);

        var details = Assert.IsType<BooksController.BookDetailsViewModel>(Assert.IsType<OkObjectResult>(response).Value);
        Assert.Equal(book.BookId, details.Book.BookId);
        var personal = Assert.IsType<UserBook>(details.UserBook);
        Assert.Equal(alice.Comments, personal.Comments);
        Assert.Equal(alice.Shelve.ShelveName, Assert.Single(Assert.IsType<Shelve[]>(details.Shelves)).ShelveName);
        Assert.NotEqual(bob.Comments, personal.Comments);
        Assert.IsType<NotFoundResult>(await controller.DetailsAsync(db, book.BookId + 1, default));
    }

    [Fact]
    public async Task UpdatingPersonalFieldsKeepsOtherReadersData()
    {
        await using var db = await CreateDatabaseAsync();
        var (book, alice, bob) = await SeedBookAsync(db);
        var controller = BooksControllerFor(alice.User.Name);

        Assert.IsType<OkResult>(await controller.UpdateStatusAsync(db, book.BookId, new() { Status = "Read" }, default));
        Assert.IsType<OkResult>(await controller.UpdateRatingAsync(db, book.BookId, new() { Rating = 5 }, default));
        Assert.IsType<OkResult>(await controller.UpdateCommnetsAsync(db, book.BookId, new() { Comments = "Finished it" }, default));

        db.ChangeTracker.Clear();
        var updated = await db.UsersBooks.SingleAsync(row => row.BookId == book.BookId && row.UserId == alice.UserId);
        Assert.Equal(BookReadingStatus.Read, updated.ReadingStatus);
        Assert.NotNull(updated.ReadingTimestamp);
        Assert.Equal((short)5, updated.Rating);
        Assert.Equal("Finished it", updated.Comments);

        var unrelated = await db.UsersBooks.SingleAsync(row => row.BookId == book.BookId && row.UserId == bob.UserId);
        Assert.Equal(BookReadingStatus.Unread, unrelated.ReadingStatus);
        Assert.Null(unrelated.Rating);
        Assert.Equal("Bob's notes", unrelated.Comments);
    }

    [Fact]
    public async Task MovingAndUnshelvingBookOnlyAffectsCurrentUser()
    {
        await using var db = await CreateDatabaseAsync();
        var (book, alice, bob) = await SeedBookAsync(db);
        const string destination = "Favorites";
        db.Shelves.Add(new Shelve { UserId = alice.UserId, ShelveId = 2, ShelveName = destination, ShelveSearch = "read later" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var controller = new LibrariesController { ControllerContext = ContextFor("alice") };

        Assert.IsType<NotFoundResult>(await controller.AddOrUpdateBookToShelfAsync(db, bob.Shelve.ShelveName, book.BookId, default));
        Assert.IsType<OkResult>(await controller.AddOrUpdateBookToShelfAsync(db, destination, book.BookId, default));
        db.ChangeTracker.Clear();
        var moved = await db.UsersBooks.Include(row => row.Shelve).SingleAsync(row => row.UserId == alice.UserId);
        Assert.Equal(destination, moved.Shelve.ShelveName);
        var addedAt = Assert.IsType<DateTime>(moved.AddedToFavoritesAt);
        Assert.IsType<OkResult>(await controller.AddOrUpdateBookToShelfAsync(db, destination, book.BookId, default));
        db.ChangeTracker.Clear();
        var dashboardController = new DashboardController { ControllerContext = ContextFor("alice") };
        var result = Assert.IsType<OkObjectResult>(await dashboardController.DashboardAsync(db, default));
        var activity = Assert.Single(Assert.IsType<DashboardModel>(result.Value).Activity);
        Assert.Equal("favorite", activity.Kind);
        Assert.Equal(addedAt, activity.Date);

        Assert.IsType<OkResult>(await controller.RemoveBookFromShelfAsync(db, destination, book.BookId, default));
        Assert.Equal(bob.UserId, (await db.UsersBooks.SingleAsync()).UserId);
        Assert.True(await db.Books.AnyAsync(row => row.BookId == book.BookId));
    }

    [Fact]
    public async Task DeletingCatalogueBookRemovesCoverAndPersonalRecords()
    {
        await using var db = await CreateDatabaseAsync();
        var (book, _, _) = await SeedBookAsync(db);
        var controller = BooksControllerFor("alice");

        Assert.IsType<NoContentResult>(await controller.DeleteBookAsync(db, book.BookId, default));

        Assert.False(await db.Books.AnyAsync());
        Assert.False(await db.UsersBooks.AnyAsync());
        Assert.False(await db.Set<BookCover>().AnyAsync());
        Assert.Equal(2, await db.Shelves.CountAsync());
        Assert.IsType<NotFoundResult>(await controller.DeleteBookAsync(db, book.BookId, default));
    }

    [Fact]
    public async Task GenresListIsAlphabeticalAndCountsSharedCatalogueBooks()
    {
        await using var db = await CreateDatabaseAsync();
        var (book, _, _) = await SeedBookAsync(db);
        var storedBook = await db.Books.SingleAsync(row => row.BookId == book.BookId);
        var unshelvedBook = new Book
        {
            Isbn = "9780141321059", Title = "The Witches", CreatedBy = "test", ModifiedBy = "test",
        };
        var fantasy = new BookGenre { Genre = "Fantasy", Books = [storedBook, unshelvedBook] };
        var youngAdult = new BookGenre { Genre = "Young adult", Books = [unshelvedBook] };
        db.Genres.AddRange(youngAdult, new BookGenre { Genre = "Biography" }, fantasy);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var controller = new GenresController { ControllerContext = ContextFor("alice") };

        var index = Assert.IsType<OkObjectResult>(await controller.IndexAsync(db, default));
        Assert.Equal(
            [new GenreIndexItem(fantasy.BookGenreId, "Fantasy", 2), new GenreIndexItem(youngAdult.BookGenreId, "Young adult", 1)],
            Assert.IsType<GenreIndexItem[]>(index.Value));

        var response = Assert.IsType<OkObjectResult>(await controller.DetailsAsync(db, fantasy.BookGenreId, default));
        var details = Assert.IsType<GenreDetailsViewModel>(response.Value);
        Assert.Equal("Fantasy", details.Name);
        Assert.Equal([book.BookId, unshelvedBook.BookId], details.Books.Select(item => item.Id).Order());
        Assert.IsType<NotFoundResult>(await controller.DetailsAsync(db, -1, default));
    }

    [Fact]
    public async Task GenreAndCollectionListsFollowBookAssociations()
    {
        await using var db = await CreateDatabaseAsync();
        await SeedBookAsync(db);
        var book = await db.Books.SingleAsync();
        var genre = new BookGenre { Genre = "Fantasy", Books = [] };
        var collection = new Collection { Name = "Classics" };
        db.Genres.Add(genre);
        db.Collections.Add(collection);
        await db.SaveChangesAsync();

        var genres = new GenresController();
        var collections = new CollectionsController();
        var initialGenres = Assert.IsType<OkObjectResult>(await genres.IndexAsync(db, default));
        var initialCollections = Assert.IsType<OkObjectResult>(await collections.IndexAsync(db, default));
        Assert.Empty(Assert.IsType<GenreIndexItem[]>(initialGenres.Value));
        Assert.Empty(Assert.IsType<Collection[]>(initialCollections.Value));

        genre.Books.Add(book);
        book.Collection = collection;
        await db.SaveChangesAsync();

        var populatedGenres = Assert.IsType<OkObjectResult>(await genres.IndexAsync(db, default));
        var populatedCollections = Assert.IsType<OkObjectResult>(await collections.IndexAsync(db, default));
        Assert.Equal(new GenreIndexItem(genre.BookGenreId, "Fantasy", 1), Assert.Single(Assert.IsType<GenreIndexItem[]>(populatedGenres.Value)));
        Assert.Equal(collection.CollectionId, Assert.Single(Assert.IsType<Collection[]>(populatedCollections.Value)).CollectionId);

        genre.Books.Remove(book);
        book.Collection = null;
        await db.SaveChangesAsync();

        var finalGenres = Assert.IsType<OkObjectResult>(await genres.IndexAsync(db, default));
        var finalCollections = Assert.IsType<OkObjectResult>(await collections.IndexAsync(db, default));
        Assert.Empty(Assert.IsType<GenreIndexItem[]>(finalGenres.Value));
        Assert.Empty(Assert.IsType<Collection[]>(finalCollections.Value));

        var genreDetails = Assert.IsType<OkObjectResult>(await genres.DetailsAsync(db, genre.BookGenreId, default));
        var collectionDetails = Assert.IsType<OkObjectResult>(await collections.DetailsAsync(db, collection.CollectionId, default));
        Assert.Empty(Assert.IsType<GenreDetailsViewModel>(genreDetails.Value).Books);
        Assert.Empty(Assert.IsType<CollectionDetailsModel>(collectionDetails.Value).Books);
    }

    [Fact]
    public async Task SearchMatchesAccentedTextAndIsbnAcrossCatalogue()
    {
        await using var db = await CreateDatabaseAsync();
        var (book, alice, _) = await SeedBookAsync(db);
        var storedBook = await db.Books.Include(row => row.Authors).SingleAsync();
        storedBook.Title = "Árvore";
        Assert.Single(storedBook.Authors!).AuthorName = "João Árvore";
        var shelf = await db.Shelves.SingleAsync(row => row.UserId == alice.UserId);
        shelf.ShelveName = "Leituras Árvore";
        db.BookSeries.Add(new BookSeries { Name = "Árvore Collection" });
        db.Collections.Add(new Collection { Name = "arvore editions" });
        await db.SaveChangesAsync();
        var controller = new SearchController { ControllerContext = ContextFor("alice") };

        var result = Assert.IsType<OkObjectResult>(await controller.SearchAsync(db, "ÁRVORE", default));
        var matches = Assert.IsAssignableFrom<IEnumerable<SearchController.SearchResultItem>>(result.Value).ToArray();

        Assert.Equal(new[] { "Author", "Book", "Collection", "Series", "Shelve" }, matches.Select(item => item.Area).Order());
        Assert.Equal($"/api/books/{book.BookId}/cover", Assert.Single(matches, item => item.Area == "Book").ImagePath);

        var byIsbn = Assert.IsType<OkObjectResult>(await controller.SearchAsync(db, book.Isbn, default));
        Assert.Equal(book.BookId.ToString(), Assert.Single(Assert.IsAssignableFrom<IEnumerable<SearchController.SearchResultItem>>(byIsbn.Value)).Id);
    }

    [Fact]
    public async Task CreatedTagIsSearchableAndCanBeAttachedToABook()
    {
        await using var db = await CreateDatabaseAsync();
        var (book, _, _) = await SeedBookAsync(db);
        var tags = new TagsController();
        var books = BooksControllerFor("alice");

        Assert.IsType<OkObjectResult>(await tags.CreateTagAsync(db, new("Ficção"), default));
        var tag = await db.Tags.SingleAsync();
        Assert.Equal("ficcao", tag.TagSearch);
        Assert.IsType<OkResult>(await books.AddTagAsync(db, book.BookId, new(tag.TagId), default));

        var search = Assert.IsType<JsonResult>(await tags.SearchTagsAsync(db, "FICÇÃO", default));
        Assert.Equal(tag.TagId, Assert.Single(Assert.IsType<BookTag[]>(search.Value)).TagId);
        var details = Assert.IsType<OkObjectResult>(await tags.DetailsAsync(db, tag.TagId, default));
        Assert.Equal(book.BookId, Assert.Single(Assert.IsType<TagDetailsModel>(details.Value).Books).Id);
        Assert.IsType<NotFoundResult>(await tags.DetailsAsync(db, -1, default));
    }

    [Fact]
    public async Task ReimportUpdatesMetadataAndReusesCatalogueEntities()
    {
        await using var db = await CreateDatabaseAsync();
        var service = new StoreFetchedBookService(db, NullLogger<StoreFetchedBookService>.Instance, NoAuthorPhotoLookup.Instance);
        var original = await service.ExecuteAsync(ImportedBook("978-0140328721", "Fantasy", "120"));
        db.ChangeTracker.Clear();

        var updated = await service.ExecuteAsync(ImportedBook("9780140328721", "Adventure", "140"));
        db.ChangeTracker.Clear();

        var book = await db.Books.Include(row => row.Authors).Include(row => row.Genres)
            .Include(row => row.Publisher).Include(row => row.Collection).Include(row => row.BookCover)
            .Include(row => row.ExternalLinks).SingleAsync();
        Assert.True(original.Result);
        Assert.True(updated.Result);
        Assert.Equal(original.BookId, updated.BookId);
        Assert.Equal("9780140328721", book.Isbn);
        Assert.Equal("Roald Dahl", Assert.Single(book.Authors!).AuthorName);
        Assert.Equal(new[] { "Adventure", "Fantasy" }, book.Genres!.Select(genre => genre.Genre).Order());
        Assert.Equal("Test publisher", book.Publisher!.Name);
        Assert.Equal("Test collection", book.Collection!.Name);
        Assert.Equal(new byte[] { 140 }, book.BookCover!.Cover);
        Assert.Equal("https://example.invalid/140.png", Assert.Single(book.ExternalLinks, link => link.Notes == "Book cover").Url);
        Assert.Equal(1, await db.Authors.CountAsync());
        Assert.Equal(1, await db.Publishers.CountAsync());
        Assert.Equal(1, await db.Collections.CountAsync());
    }

    [Fact]
    public async Task CollectionAndSeriesDetailsIncludePersonalReadingStatus()
    {
        await using var db = await CreateDatabaseAsync();
        var (book, alice, _) = await SeedBookAsync(db);
        var storedBook = await db.Books.SingleAsync();
        storedBook.Collection = new Collection { Name = "Classics" };
        storedBook.Series = new BookSeries { Name = "Adventures" };
        var personal = await db.UsersBooks.SingleAsync(row => row.UserId == alice.UserId);
        personal.ReadingStatus = BookReadingStatus.Read;
        await db.SaveChangesAsync();

        var collections = new CollectionsController { ControllerContext = ContextFor("alice") };
        var series = new BookSeriesController { ControllerContext = ContextFor("alice") };
        var collectionIndex = Assert.IsType<OkObjectResult>(await collections.IndexAsync(db, default));
        var seriesIndex = Assert.IsType<OkObjectResult>(await series.IndexAsync(db, default));
        var collectionId = Assert.Single(Assert.IsType<Collection[]>(collectionIndex.Value)).CollectionId;
        var seriesId = Assert.Single(Assert.IsType<BookSeries[]>(seriesIndex.Value)).SeriesId;

        var collectionResult = Assert.IsType<OkObjectResult>(await collections.DetailsAsync(db, collectionId, default));
        var seriesResult = Assert.IsType<OkObjectResult>(await series.DetailsAsync(db, seriesId, default));
        var collectionBook = Assert.Single(Assert.IsType<CollectionDetailsModel>(collectionResult.Value).Books);
        var seriesBook = Assert.Single(Assert.IsType<SeriesDetailsViewModel>(seriesResult.Value).Books);
        Assert.Equal(book.BookId, collectionBook.Id);
        Assert.Equal(book.BookId, seriesBook.Id);
        Assert.Equal("Read", collectionBook.Status);
        Assert.Equal("Read", seriesBook.Status);
        Assert.IsType<NotFoundResult>(await collections.DetailsAsync(db, -1, default));
        Assert.IsType<NotFoundResult>(await series.DetailsAsync(db, -1, default));
    }

    [Fact]
    public async Task CreatingDuplicateSeriesReturnsConflict()
    {
        await using var db = await CreateDatabaseAsync();
        var controller = new BookSeriesController();
        var series = new SeriesCreateModel { Name = "Adventures" };

        Assert.IsType<OkResult>(await controller.CreateAsync(db, series, default));

        var duplicate = Assert.IsType<ObjectResult>(await controller.CreateAsync(db, series, default));
        Assert.Equal(409, duplicate.StatusCode);
        Assert.Equal(1, await db.BookSeries.CountAsync());
    }

    [Fact]
    public async Task DashboardCountsCurrentYearsCompletedBooksForCurrentUser()
    {
        await using var db = await CreateDatabaseAsync();
        var (_, alice, bob) = await SeedBookAsync(db);
        var book = await db.Books.SingleAsync();
        book.AdditionalData = new() { ["pages"] = "140" };
        var records = await db.UsersBooks.ToDictionaryAsync(row => row.UserId);
        records[alice.UserId].ReadingStatus = BookReadingStatus.Read;
        records[alice.UserId].ReadingTimestamp = DateTime.Today;
        records[bob.UserId].ReadingStatus = BookReadingStatus.Reading;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var controller = new DashboardController { ControllerContext = ContextFor("alice") };

        var result = Assert.IsType<OkObjectResult>(await controller.DashboardAsync(db, default));
        var dashboard = Assert.IsType<DashboardModel>(result.Value);
        Assert.Equal(1, dashboard.CurrentYearReadBooks.Count);
        Assert.Equal(1, dashboard.ReadBooks.Count);
        Assert.Equal(0, dashboard.ReadingBooks.Count);
        Assert.Equal(0, dashboard.UnreadBooks.Count);
        Assert.Equal(140, dashboard.TotalPagesRead.Count);
    }

    [Fact]
    public async Task LibraryListsOnlyTheCurrentUsersMatchingBooksAndShelves()
    {
        await using var db = await CreateDatabaseAsync();
        var (book, alice, bob) = await SeedBookAsync(db);
        var records = await db.UsersBooks.ToDictionaryAsync(row => row.UserId);
        records[alice.UserId].ReadingStatus = BookReadingStatus.Reading;
        records[bob.UserId].ReadingStatus = BookReadingStatus.Read;
        await db.SaveChangesAsync();
        var controller = new LibrariesController { ControllerContext = ContextFor("alice") };

        var index = Assert.IsType<OkObjectResult>(await controller.IndexAsync(db, default));
        Assert.Equal(new[] { "alice", "bob" }, Assert.IsType<LibrariesController.LibraryIndexViewModel>(index.Value)
            .Libraries.Select(library => library.Library).Order());

        var shelves = Assert.IsType<OkObjectResult>(await controller.ShelvesAsync(db, default));
        var personal = Assert.IsType<LibrariesController.ShelvesIndexViewModel>(shelves.Value);
        Assert.Equal(alice.UserId, personal.LibraryId);
        Assert.Equal(alice.Shelve.ShelveName, Assert.Single(personal.Shelves).Shelve);
        Assert.Equal(1, Assert.Single(personal.Shelves).BookCount);

        var books = Assert.IsType<OkObjectResult>(await controller.GetBooksFromShelfAsync(db, alice.Shelve.ShelveName, default));
        var item = Assert.Single(Assert.IsType<BookIndexItem[]>(books.Value));
        Assert.Equal(book.BookId, item.Id);
        Assert.Equal("Roald Dahl", item.Author);
        Assert.Equal("Reading", item.Status);
        Assert.IsType<NotFoundResult>(await controller.GetBooksFromShelfAsync(db, bob.Shelve.ShelveName, default));

        foreach (var list in new[] { "reading", "read", "unread" })
        {
            var page = Assert.IsType<OkObjectResult>(await controller.ListAsync(db, list, 0, 10, default));
            var items = Assert.IsType<PaginatedResult<BookIndexItem>>(page.Value).Items;
            if (list == "reading")
                Assert.Equal(book.BookId, Assert.Single(items).Id);
            else
                Assert.Empty(items);
        }

        Assert.IsType<OkResult>(await controller.AddShelveAsync(db, "To sort", default));
        Assert.True(await db.Shelves.AnyAsync(shelf => shelf.ShelveName == "To sort" && shelf.UserId == alice.UserId));
    }

    [Fact]
    public async Task DetailAndShelfPagesBreakTiesAndScopeReadingStateToTheCurrentAccount()
    {
        await using var db = await CreateDatabaseAsync();
        var (first, alice, bob) = await SeedBookAsync(db);
        var author = await db.Authors.SingleAsync();
        var second = new Book
        {
            Isbn = "9780000000002", Title = first.Title, Search = first.Search,
            CreatedBy = "test", ModifiedBy = "test", Authors = [author],
        };
        db.UsersBooks.Add(new UserBook
        {
            Book = second, UserId = alice.UserId,
            Shelve = await db.Shelves.SingleAsync(shelf => shelf.UserId == alice.UserId),
            ReadingStatus = BookReadingStatus.Reading,
        });
        var sameNamedShelf = await db.Shelves.SingleAsync(shelf => shelf.UserId == bob.UserId);
        sameNamedShelf.ShelveName = alice.Shelve.ShelveName;
        await db.SaveChangesAsync();
        var authors = new AuthorsController { ControllerContext = ContextFor("bob") };
        var library = new LibrariesController { ControllerContext = ContextFor("alice") };

        var firstAuthorPage = Assert.IsType<AuthorDetailsModel>(Assert.IsType<OkObjectResult>(
            await authors.DetailsAsync(db, author.AuthorId, default, page: 0, size: 1)).Value);
        var secondAuthorPage = Assert.IsType<AuthorDetailsModel>(Assert.IsType<OkObjectResult>(
            await authors.DetailsAsync(db, author.AuthorId, default, page: 1, size: 1)).Value);
        Assert.Equal(2, firstAuthorPage.BooksTotal);
        Assert.Equal(first.BookId, Assert.Single(firstAuthorPage.Books).Id);
        Assert.Equal(second.BookId, Assert.Single(secondAuthorPage.Books).Id);
        Assert.Equal("Unshelved", Assert.Single(secondAuthorPage.Books).Status);

        var firstShelfPage = Assert.IsType<PaginatedResult<BookIndexItem>>(Assert.IsType<OkObjectResult>(
            await library.GetBooksFromShelfAsync(db, sameNamedShelf.ShelveName, default, page: 0, size: 1)).Value);
        var secondShelfPage = Assert.IsType<PaginatedResult<BookIndexItem>>(Assert.IsType<OkObjectResult>(
            await library.GetBooksFromShelfAsync(db, sameNamedShelf.ShelveName, default, page: 1, size: 1)).Value);
        Assert.Equal(2, firstShelfPage.Total);
        Assert.Equal(first.BookId, Assert.Single(firstShelfPage.Items).Id);
        Assert.Equal(second.BookId, Assert.Single(secondShelfPage.Items).Id);
        Assert.Equal("Reading", Assert.Single(secondShelfPage.Items).Status);

        library.ControllerContext = ContextFor("bob");
        var bobPage = Assert.IsType<PaginatedResult<BookIndexItem>>(Assert.IsType<OkObjectResult>(
            await library.GetBooksFromShelfAsync(db, sameNamedShelf.ShelveName, default, page: 1, size: 1)).Value);
        Assert.Equal(1, bobPage.Total);
        Assert.Empty(bobPage.Items);
        Assert.IsType<BadRequestObjectResult>(
            await authors.DetailsAsync(db, author.AuthorId, default, page: -1, size: 1));
    }

    [Fact]
    public async Task CatalogueDistinguishesRecentUnshelvedAndReadingLists()
    {
        await using var db = await CreateDatabaseAsync();
        var (book, alice, bob) = await SeedBookAsync(db);
        var records = await db.UsersBooks.ToDictionaryAsync(row => row.UserId);
        records[alice.UserId].ReadingStatus = BookReadingStatus.Reading;
        records[bob.UserId].ReadingStatus = BookReadingStatus.Read;
        var newer = new Book
        {
            Isbn = "9780000000002", Title = "Waiting to be shelved", CreatedAt = DateTime.Today,
            CreatedBy = "test", ModifiedBy = "test",
        };
        db.Books.Add(newer);
        await db.SaveChangesAsync();
        var controller = BooksControllerFor("alice");

        foreach (var (list, expected) in new[]
        {
            ("recent", newer.BookId), ("unshelved", newer.BookId),
            ("reading", book.BookId),
        })
        {
            var result = Assert.IsType<OkObjectResult>(await controller.ListAsync(db, list, 0, 1, default));
            var item = Assert.Single(Assert.IsType<PaginatedResult<BookIndexItem>>(result.Value).Items);
            Assert.Equal(expected, item.Id);
            Assert.Equal($"/api/books/{expected}/cover", item.Cover);
        }

        var read = Assert.IsType<OkObjectResult>(await controller.ListAsync(db, "read", 0, 1, default));
        Assert.Empty(Assert.IsType<PaginatedResult<BookIndexItem>>(read.Value).Items);
        var bobRead = Assert.IsType<OkObjectResult>(await BooksControllerFor("bob").ListAsync(db, "read", 0, 1, default));
        Assert.Equal(book.BookId, Assert.Single(Assert.IsType<PaginatedResult<BookIndexItem>>(bobRead.Value).Items).Id);

        var second = Assert.IsType<OkObjectResult>(await controller.ListAsync(db, "recent", 1, 1, default));
        var secondPage = Assert.IsType<PaginatedResult<BookIndexItem>>(second.Value);
        Assert.Equal(1, secondPage.Page);
        Assert.Equal(2, secondPage.Total);
        Assert.Equal(book.BookId, Assert.Single(secondPage.Items).Id);
        Assert.IsType<BadRequestObjectResult>(await controller.ListAsync(db, "recent", 0, 101, default));
        var cover = Assert.IsType<FileContentResult>(await controller.CoverAsync(db, book.BookId, default));
        Assert.Equal("image/png", cover.ContentType);
        Assert.Equal(new byte[] { 1 }, cover.FileContents);
        Assert.IsType<NotFoundResult>(await controller.CoverAsync(db, newer.BookId, default));
    }

    [Fact]
    public async Task BookSearchMatchesTitleAndAuthorBeforePagination()
    {
        await using var db = await CreateDatabaseAsync();
        var (book, _, _) = await SeedBookAsync(db);
        var controller = BooksControllerFor("alice");
        var found = Assert.IsType<OkObjectResult>(await controller.ListAsync(db, "recent", 0, 10, default, "Dahl"));
        Assert.Equal(book.BookId, Assert.Single(Assert.IsType<PaginatedResult<BookIndexItem>>(found.Value).Items).Id);
        var missing = Assert.IsType<OkObjectResult>(await controller.ListAsync(db, "recent", 0, 10, default, "unmatched"));
        Assert.Empty(Assert.IsType<PaginatedResult<BookIndexItem>>(missing.Value).Items);
    }

    [Fact]
    public async Task ExistingSeriesCanBeAssignedAndClearedWithoutCreatingAnUnknownSeries()
    {
        await using var db = await CreateDatabaseAsync();
        var (book, _, _) = await SeedBookAsync(db);
        db.BookSeries.Add(new BookSeries { Name = "Classics" });
        await db.SaveChangesAsync();
        var controller = BooksControllerFor("alice");

        Assert.IsType<NotFoundResult>(await controller.UpdateSeriesAsync(db, book.BookId, new() { Series = "Unknown" }, default));
        Assert.IsType<NotFoundResult>(await controller.UpdateSeriesAsync(db, -1, new() { Series = "Classics" }, default));
        Assert.IsType<OkResult>(await controller.UpdateSeriesAsync(db, book.BookId, new() { Series = "Classics" }, default));
        db.ChangeTracker.Clear();
        Assert.Equal("Classics", (await db.Books.Include(row => row.Series).SingleAsync()).Series!.Name);

        Assert.IsType<OkResult>(await controller.UpdateSeriesAsync(db, book.BookId, new() { Series = "" }, default));
        db.ChangeTracker.Clear();
        Assert.Null((await db.Books.Include(row => row.Series).SingleAsync()).Series);
        Assert.Equal(1, await db.BookSeries.CountAsync());
    }

    [Fact]
    public async Task AuthorsDetailAndCatalogueExportsReferenceTheSameBook()
    {
        await using var db = await CreateDatabaseAsync();
        var (book, _, _) = await SeedBookAsync(db);
        var authors = new AuthorsController { ControllerContext = ContextFor("alice") };
        var catalogue = BooksControllerFor("alice");

        var index = Assert.IsType<OkObjectResult>(await authors.IndexAsync(db, default));
        var author = Assert.Single(Assert.IsType<AuthorIndexItem[]>(index.Value));
        var response = Assert.IsType<OkObjectResult>(await authors.DetailsAsync(db, author.AuthorId, default));
        var details = Assert.IsType<AuthorDetailsModel>(response.Value);
        Assert.Equal("Roald Dahl", details.Name);
        Assert.Equal(book.BookId, Assert.Single(details.Books).Id);
        Assert.Equal("Unread", Assert.Single(details.Books).Status);
        Assert.IsType<NotFoundResult>(await authors.DetailsAsync(db, -1, default));

        var fullExport = Assert.IsType<OkObjectResult>(await catalogue.ListAsync(db, default));
        Assert.Equal("Roald Dahl", Assert.Single(Assert.Single(Assert.IsType<Book[]>(fullExport.Value)).Authors!).AuthorName);
        var isbnExport = Assert.IsType<OkObjectResult>(await catalogue.ListIsbnAsync(db, default));
        Assert.Equal(book.Isbn, Assert.Single(Assert.IsType<string[]>(isbnExport.Value)));
    }

    private static StoreFetchedBookArgs ImportedBook(string isbn, string genre, string pages)
    {
        var book = new FetchedBook
        {
            Isbn = isbn,
            Title = "Matilda",
            Authors = ["Roald Dahl"],
            PublishedBy = "Test publisher",
            Url = "https://example.invalid/book",
        };
        book.Metadata[FetchedBook.FetchedBookMetadataGenres] = new[] { genre };
        book.Metadata[FetchedBook.FetchedBookMetadataCollection] = "Test collection";
        book.Metadata[FetchedBook.FetchedBookMetadataTotalPages] = pages;
        return new StoreFetchedBookArgs
        {
            Book = book,
            Cover = new FetchedCover { Image = [byte.Parse(pages)], MimeType = "image/png", Url = $"https://example.invalid/{pages}.png" },
        };
    }

    private static async Task<(Book Book, UserBook Alice, UserBook Bob)> SeedBookAsync(BibliothecaDbContext db)
    {
        var book = new Book
        {
            Isbn = "9780140328721",
            Title = "Matilda",
            Search = "matilda",
            CreatedBy = "test",
            ModifiedBy = "test",
            Authors = [new Author { AuthorName = "Roald Dahl", AuthorSearch = "roald dahl" }],
            BookCover = new BookCover { Cover = [1], MimeType = "image/png" },
        };
        var alice = PersonalRecord("alice", "Alice's notes", book);
        var bob = PersonalRecord("bob", "Bob's notes", book);
        db.UsersBooks.AddRange(alice, bob);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return (book, alice, bob);
    }

    private static UserBook PersonalRecord(string name, string comments, Book book)
    {
        var user = new User { Name = name, Email = $"{name}@example.invalid", FullName = name, Password = "test", Settings = "{}" };
        var shelf = new Shelve { User = user, ShelveId = 1, ShelveName = $"{name} shelf", ShelveSearch = $"{name} shelf" };
        return new UserBook { User = user, Book = book, Shelve = shelf, ReadingStatus = BookReadingStatus.Unread, Comments = comments };
    }

    private static BooksController BooksControllerFor(string name) =>
        new(NullLogger<BooksController>.Instance) { ControllerContext = ContextFor(name) };

    private static ControllerContext ContextFor(string name) => new()
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", name)], "test")),
        },
    };
}
