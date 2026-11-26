using Bibliotheca.Backend.Controllers;
using Bibliotheca.Backend.Fetchers;
using Bibliotheca.Backend.Options;
using Bibliotheca.Backend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static Bibliotheca.Backend.Tests.TestDatabase;
using FrameworkOptions = Microsoft.Extensions.Options.Options;

namespace Bibliotheca.Backend.Tests;

public class WookMetadataTests
{
    [Fact]
    public void EmptySearchIsReportedAsNotFound()
    {
        var html = "<main><p>NÃO ENCONTROU O QUE PROCURA?</p></main>";
        var book = CreateFetcher(new FetcherOptions()).ParseResponse(
            "https://www.wook.pt/pesquisa?keyword=9722514091", ref html);

        Assert.False(book.Success);
    }

    [Fact]
    public void UnrecognizedSearchIsReportedAsProviderFailure()
    {
        var html = "<main><h1>Please verify your browser</h1></main>";
        Assert.Throws<InvalidDataException>(() => CreateFetcher(new FetcherOptions()).ParseResponse(
            "https://www.wook.pt/pesquisa?keyword=9722514091", ref html));
    }

    [Fact]
    public void ParsesCatalogueMetadataFromCapturedProductHtml()
    {
        var fetcher = CreateFetcher(new FetcherOptions());
        var html = ProductHtml("978-0140328721");

        var book = fetcher.ParseResponse("https://example.invalid/product", ref html);

        Assert.True(book.Success);
        Assert.Equal("Matilda", book.Title);
        Assert.Equal("A novel", book.Subtitle);
        Assert.Equal(new[] { "Roald Dahl" }, book.Authors);
        Assert.Equal("978-0140328721", book.Isbn);
        Assert.Equal("Test publisher", book.PublishedBy);
        Assert.Equal(new DateTime(1988, 3, 1), book.PublishedOn);
        Assert.Equal("https://example.invalid/product", book.Url);
        Assert.Equal("Children's classics", book.Metadata[FetchedBook.FetchedBookMetadataCollection]);
        Assert.Equal("240", book.Metadata[FetchedBook.FetchedBookMetadataTotalPages]);
        Assert.Equal("Português", book.Metadata[FetchedBook.FetchedBookMetadataLanguage]);
        Assert.Equal("A young reader discovers her powers.", book.Metadata[FetchedBook.FetchedBookMetadataSynopsis]);
        Assert.Equal(new[] { "Fantasy" }, Assert.IsType<string[]>(book.Metadata[FetchedBook.FetchedBookMetadataGenres]));
    }

    [Fact]
    public void ReadsMultipleLinkedCreditsAndPreservesCommasWithinALink()
    {
        var html = ProductHtml("9789722362030").Replace("de Roald Dahl ; Ilustração:", """
            de <a href="/autor/j-k-rowling/1">J. K. Rowling</a>, <a href="/autor/john-tiffany/2">John Tiffany</a>
            e <a href="/autor/ursula-le-guin/3">Le Guin, Ursula</a>; Ilustração:
            """);

        var book = CreateFetcher(new FetcherOptions()).ParseResponse("https://www.wook.pt/livro/edition/123", ref html);

        Assert.True(book.HasStructuredAuthors);
        Assert.Equal(new[] { "J. K. Rowling", "John Tiffany", "Le Guin, Ursula" }, book.Authors);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("January 16, 1988", 1)]
    [InlineData("December 31, 2020", 12)]
    public void EnglishDatesPreserveThePublicationMonth(string? value, int? month)
    {
        var date = value!.ToDateFromEnglishDate();
        Assert.Equal(month, date?.Month);
        if (date.HasValue)
            Assert.Equal(1, date.Value.Day);
    }

    [Fact]
    public async Task ImportsSavedProductAndReportsMissingCover()
    {
        var directory = Directory.CreateTempSubdirectory("bibliotheca-fetch-tests-");
        try
        {
            await using var db = await CreateDatabaseAsync();
            var options = FrameworkOptions.Create(new FetcherOptions { TempFilesSaveLocation = directory.FullName });
            const string isbn = "9780140328721";
            const string withoutCover = "9780000000002";
            await File.WriteAllTextAsync(Path.Combine(directory.FullName, isbn + ".html"), ProductHtml(isbn));
            await File.WriteAllTextAsync(Path.Combine(directory.FullName, withoutCover + ".html"), ProductHtml(withoutCover));
            await File.WriteAllBytesAsync(Path.Combine(directory.FullName, isbn + ".jpg"), [1, 2, 3]);
            var controller = new FetchController(NullLogger<FetchController>.Instance);
            var store = new StoreFetchedBookService(db, NullLogger<StoreFetchedBookService>.Instance, NoAuthorPhotoLookup.Instance);

            var response = Assert.IsType<OkObjectResult>(await controller.ProcessExisting(
                [], store, CreateFetcher(options.Value), options, default));

            var imported = Assert.IsType<Dictionary<string, bool>>(response.Value);
            Assert.True(imported[isbn]);
            Assert.False(imported[withoutCover]);
            var book = await db.Books.Include(item => item.BookCover).SingleAsync();
            Assert.Equal(isbn, book.Isbn);
            Assert.Equal(new byte[] { 1, 2, 3 }, book.BookCover!.Cover);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static WookBookFetcher CreateFetcher(FetcherOptions options) =>
        new(FrameworkOptions.Create(options), FrameworkOptions.Create(new PlaywrightOptions()), NullLogger<WookBookFetcher>.Instance);

    internal static string ProductHtml(string isbn) => $"""
        <!-- https://example.invalid/product -->
        <div class="flex">
          <table>
            <caption>
              <p class="font-medium"> Matilda </p>
              <p class="subtitle">A novel</p>
              <p class="authors">de Roald Dahl ; Ilustração: <a href="/autor/quentin-blake/123">Quentin Blake</a></p>
            </caption>
            <tbody>
              <tr><td>ISBN</td><td>{isbn}</td></tr>
              <tr><td>Editor</td><td>Test publisher</td></tr>
              <tr><td>Lançamento</td><td>março de 1988</td></tr>
              <tr><td>Idioma</td><td>Português</td></tr>
              <tr><td>Páginas</td><td>240</td></tr>
              <tr><td>Coleção</td><td>Children's classics</td></tr>
              <tr><td>Classificação</td><td>Children &gt; Fantasy</td></tr>
            </tbody>
          </table>
        </div>
        <div id="synopsis-toggle-collapse">A young reader discovers her powers.</div>
        """;
}
