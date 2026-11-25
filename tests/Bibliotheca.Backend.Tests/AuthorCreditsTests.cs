using Bibliotheca.Backend.Data;
using Bibliotheca.Backend.Fetchers;
using Bibliotheca.Backend.Initialization;
using Bibliotheca.Backend.Options;
using Bibliotheca.Backend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static Bibliotheca.Backend.Tests.TestDatabase;
using FrameworkOptions = Microsoft.Extensions.Options.Options;

namespace Bibliotheca.Backend.Tests;

public class AuthorCreditsTests
{
    [Fact]
    public async Task StartupRepairsOnlyBookLinksSupportedByStoredEditionOrExplicitCredit()
    {
        await using var db = await CreateDatabaseAsync();
        var rowling = new Author { AuthorName = "J. K. Rowling", PhotoUrl = "https://example.invalid/rowling" };
        var combined = new Author { AuthorName = "J. K. Rowling, John Tiffany", PhotoUrl = "https://example.invalid/legacy" };
        var thorne = new Author { AuthorName = "Jack Thorne" };
        var single = new Author { AuthorName = "Le Guin, Ursula" };
        var labelled = new Author { AuthorName = "J. K. Rowling ; Ilustração: Jim Kay" };
        var supported = CatalogueBook("9789722362030", combined, thorne);
        var ambiguous = CatalogueBook("9780140328721", combined);
        var invertedName = CatalogueBook("9780140328738", single);
        var illustrated = CatalogueBook("9789722356824", labelled);
        illustrated.ExternalLinks.Add(new BookExternalLinks { Url = "https://www.wook.pt/livro/edition/123", Notes = "Metadata fetched from" });
        db.Books.AddRange(supported, ambiguous, invertedName, illustrated);
        db.Authors.Add(rowling);
        await db.SaveChangesAsync();
        var oldImport = new BookImport
        {
            Owner = "test", Isbns = [], EditionProviderId = "wook", EditionExternalId = "edition",
            ResultBookId = supported.BookId, Total = 1, Succeeded = 1, CompletedAtUtc = DateTime.UtcNow,
            SelectedEdition = new ExternalEdition("edition", supported.Title, Isbn: supported.Isbn,
                Details: new(null, [rowling.AuthorName, "John Tiffany", thorne.AuthorName], null, null, null, null, null))
        };
        db.BookImports.Add(oldImport);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var initializer = new EntityFrameworkMigrationInitializer(db, NullLogger<EntityFrameworkMigrationInitializer>.Instance, new PrivateStorageTests.StorageEnvironment());
        await initializer.InitializeAsync(default);
        db.ChangeTracker.Clear();
        await initializer.InitializeAsync(default);
        db.ChangeTracker.Clear();

        var books = await db.Books.Include(book => book.Authors).ToDictionaryAsync(book => book.BookId);
        Assert.Equal(new[] { "J. K. Rowling", "Jack Thorne", "John Tiffany" }, Names(books[supported.BookId]));
        Assert.Equal(new[] { combined.AuthorName }, Names(books[ambiguous.BookId]));
        Assert.Equal(new[] { single.AuthorName }, Names(books[invertedName.BookId]));
        var author = Assert.Single(books[illustrated.BookId].Authors!);
        Assert.Equal(rowling.AuthorId, author.AuthorId);
        Assert.Equal(rowling.PhotoUrl, author.PhotoUrl);
        Assert.Equal(combined.PhotoUrl, (await db.Authors.FindAsync(combined.AuthorId))?.PhotoUrl);
        Assert.Single(await db.Authors.Where(author => author.AuthorName == "John Tiffany").ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StructuredEditionImportsSeparateAuthorsAndPreservesAnInternalComma(bool reimport)
    {
        await using var db = await CreateDatabaseAsync();
        if (reimport)
        {
            db.Books.Add(CatalogueBook("9789722362030", new Author { AuthorName = "J. K. Rowling, John Tiffany" }));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }
        var store = new StoreFetchedBookService(db, NullLogger<StoreFetchedBookService>.Instance, NoAuthorPhotoLookup.Instance);
        await store.ExecuteAsync(new StoreFetchedBookArgs
        {
            Book = new FetchedBook
            {
                Isbn = "9789722362030", Title = "Fixture", Url = "https://books.google.com/books?id=fixture",
                PublishedBy = "Publisher", Authors = ["J. K. Rowling", "John Tiffany", "Le Guin, Ursula"],
                HasStructuredAuthors = true
            }
        });
        db.ChangeTracker.Clear();
        var book = await db.Books.Include(row => row.Authors).SingleAsync();
        Assert.Equal(new[] { "J. K. Rowling", "John Tiffany", "Le Guin, Ursula" }, Names(book));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GoodreadsReadsEachAuthorCreditAndExcludesIllustration(bool useStructuredData)
    {
        var fetcher = new GoodReadsBookFetcher(FrameworkOptions.Create(new FetcherOptions()),
            FrameworkOptions.Create(new PlaywrightOptions()), NullLogger<GoodReadsBookFetcher>.Instance);
        var html = """
            <div class="BookPageTitleSection__title"><h1>Fixture</h1></div>
            <div class="ContributorLinksList">
              <a class="ContributorLink"><span class="ContributorLink__name">J. K. Rowling</span></a>
              <a class="ContributorLink"><span class="ContributorLink__name">John Tiffany</span></a>
              <span class="ContributorLink__role">(Adaptor)</span>
              <a class="ContributorLink"><span class="ContributorLink__name">Jim Kay</span>
                <span class="ContributorLink__role">(Illustrator)</span></a>
            </div>
            <div class="BookPageMetadataSection__description">Synopsis</div>
            <div class="EditionDetails">
              <dl class="DescListItem"><dt>ISBN</dt><dd>9789722362030</dd></dl>
              <dl class="DescListItem"><dt>Format</dt><dd>120 pages</dd></dl>
              <dl class="DescListItem"><dt>Published</dt><dd>January 16, 1988 by Publisher</dd></dl>
            </div>
            """;
        if (useStructuredData)
        {
            // The page can omit some contributors from its initial visible byline.
            html = html.Replace("John Tiffany", "Hidden in byline");
            html += """
                <script id="__NEXT_DATA__" type="application/json">
                {"props":{"pageProps":{"apolloState":{
                  "ROOT_QUERY":{"getBookByLegacyId({\"legacyId\":\"123\"})":{"__ref":"Book:one"}},
                  "Book:one":{"primaryContributorEdge":{"role":"Author","node":{"__ref":"Author:one"}},
                              "secondaryContributorEdges":[{"role":"Adapter","node":{"__ref":"Author:two"}},
                                                           {"role":"Illustrator","node":{"__ref":"Author:three"}}]},
                  "Author:one":{"name":"J. K. Rowling"},"Author:two":{"name":"John Tiffany"},
                  "Author:three":{"name":"Jim Kay"}
                }}}}
                </script>
                """;
        }

        var result = fetcher.ParseResponse("https://www.goodreads.com/book/show/123", ref html);

        Assert.Equal(new[] { "J. K. Rowling", "John Tiffany" }, result.Authors);
    }

    private static Book CatalogueBook(string isbn, params Author[] authors) => new()
    {
        Isbn = isbn, Title = isbn, CreatedBy = "test", ModifiedBy = "test",
        Authors = authors.ToList(), ExternalLinks = []
    };

    private static string[] Names(Book book) => book.Authors!.Select(author => author.AuthorName)
        .Order(StringComparer.Ordinal).ToArray();
}
