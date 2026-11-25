using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text;
using Bibliotheca.Backend.Data;
using Bibliotheca.Backend.Fetchers;
using Bibliotheca.Backend.Models;
using Bibliotheca.Backend.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bibliotheca.Backend.Tests;

public partial class HttpApplicationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportsPhotosForNewAndExistingAuthorsAndReusesThem(bool useDirectEdition)
    {
        var directory = Directory.CreateTempSubdirectory("bibliotheca-author-photos-");
        try
        {
            using var handler = new AuthorPhotosHandler("photos");
            await using var original = CreateFactory(directory);
            await using var factory = WithAuthorPhotos(original, handler);
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<BibliothecaDbContext>();
            var store = scope.ServiceProvider.GetRequiredService<StoreFetchedBookService>();
            var existing = new Author { AuthorName = "Eva Smith" };
            db.Authors.Add(existing);
            await db.SaveChangesAsync();
            var existingId = existing.AuthorId;
            db.ChangeTracker.Clear();

            var args = PhotoBook("9780140328721", "Roald Dahl", "Eva Smith", "Sam Doe", "Roald Dahl");
            var expectedPath = "/isbn/9780140328721.json";
            if (useDirectEdition)
            {
                args.Book.Isbn = "";
                args.Book.Url = "https://openlibrary.org/books/OL1M/Fantastic_Mr_Fox";
                expectedPath = "/books/OL1M.json";
            }
            var first = await store.ExecuteAsync(args);
            Assert.True(first.Result);
            db.ChangeTracker.Clear();
            var imported = await db.Books.Include(book => book.Authors).SingleAsync();
            Assert.Equal(3, imported.Authors!.Count);
            var newAuthor = Assert.Single(imported.Authors, author => author.AuthorName == "Roald Dahl");
            var existingAuthor = Assert.Single(imported.Authors, author => author.AuthorId == existingId);
            var noPhoto = Assert.Single(imported.Authors, author => author.AuthorName == "Sam Doe");
            Assert.Equal("https://covers.openlibrary.org/a/id/123-M.jpg?default=false", newAuthor.PhotoUrl);
            Assert.Equal("https://covers.openlibrary.org/a/id/321-M.jpg?default=false", existingAuthor.PhotoUrl);
            Assert.Null(noPhoto.PhotoUrl);
            Assert.Equal("Roald Dahl was born on 13 September 1916 in Cardiff. Roald Dahl died on 23 November 1990.", newAuthor.Biography);
            Assert.Equal("https://openlibrary.org/authors/OL1A", newAuthor.BiographySourceUrl);
            Assert.Equal("/authors/OL1A", newAuthor.SourceAuthorKey);
            Assert.Null(noPhoto.Biography);
            Assert.Equal(new[] { expectedPath, "/authors/OL1A.json", "/authors/OL2A.json", "/authors/OL3A.json" },
                handler.Paths.ToArray());

            var second = await store.ExecuteAsync(PhotoBook("9780140328738", "Roald Dahl", "Eva Smith"));
            Assert.True(second.Result);
            Assert.NotEqual(first.BookId, second.BookId);
            Assert.Equal(4, handler.Paths.Count);
            Assert.Equal(3, await db.Authors.CountAsync());

            using var browser = CreateClient(factory);
            using var anonymous = await browser.GetAsync($"/api/authors/{newAuthor.AuthorId}");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            await RegisterAndLoginAsync(browser);
            using var client = CreateApiClient(factory, browser);
            var tokens = await GetTokensAsync(browser, client);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GetProperty("access_token").GetString());
            var details = await client.GetFromJsonAsync<AuthorDetailsModel>($"/api/authors/{newAuthor.AuthorId}");
            Assert.NotNull(details);
            Assert.Equal(newAuthor.PhotoUrl, details.PhotoUrl);
            Assert.Equal(newAuthor.Biography, details.Biography);
            Assert.Equal(newAuthor.BiographySourceUrl, details.BiographySourceUrl);
            Assert.Equal(2, details.Books.Length);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("ambiguous")]
    [InlineData("wrong title")]
    [InlineData("failure")]
    [InlineData("timeout")]
    public async Task ImportsBooksAndAssociatesAuthorsWithoutASafePhoto(string mode)
    {
        var directory = Directory.CreateTempSubdirectory("bibliotheca-author-photos-");
        try
        {
            using var handler = new AuthorPhotosHandler(mode);
            await using var original = CreateFactory(directory);
            await using var factory = WithAuthorPhotos(original, handler);
            await using var scope = factory.Services.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<StoreFetchedBookService>();
            var db = scope.ServiceProvider.GetRequiredService<BibliothecaDbContext>();

            var args = PhotoBook("9780140328721", "Roald Dahl");
            if (mode == "wrong title")
                args.Book.Title = "Different book";
            var book = await store.ExecuteAsync(args);
            Assert.True(book.Result);
            db.ChangeTracker.Clear();
            var stored = await db.Books.Include(book => book.Authors).SingleAsync();
            var author = Assert.Single(stored.Authors!);
            Assert.Equal("Roald Dahl", author.AuthorName);
            Assert.Null(author.PhotoUrl);
            Assert.Null(author.Biography);
            Assert.Null(author.BiographySourceUrl);
            Assert.Equal(mode == "ambiguous" ? 3 : 1, handler.Paths.Count);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("wiki matching", "A fixture biography.", "https://en.wikipedia.org/?curid=42")]
    [InlineData("wiki homonym", null, null)]
    [InlineData("wiki unavailable", null, null)]
    public async Task UsesOnlyTheLinkedWikipediaArticleForTheSamePerson(string mode, string? expectedText, string? expectedSource)
    {
        var directory = Directory.CreateTempSubdirectory("bibliotheca-bio-");
        try
        {
            using var handler = new AuthorPhotosHandler(mode);
            await using var original = CreateFactory(directory);
            await using var factory = WithAuthorPhotos(original, handler);
            var lookup = factory.Services.GetRequiredService<IAuthorProfileLookup>();
            var profiles = await lookup.FindProfilesAsync(PhotoBook("9780140328721", "Roald Dahl").Book, ["Roald Dahl"], default);
            var profile = profiles["Roald Dahl"];
            Assert.Equal("/authors/OL1A", profile.SourceAuthorKey);
            Assert.NotNull(profile.PhotoUrl);
            Assert.Equal(expectedText, profile.Biography);
            Assert.Equal(expectedSource, profile.BiographySourceUrl);
            Assert.Equal("/w/api.php", handler.Paths.Last());
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public void ProfileRefreshPreservesAttributionAndRejectsAnotherPerson()
    {
        var author = new Author { AuthorName = "Same Name", SourceAuthorKey = "/authors/OL1A",
            Biography = "Existing summary", BiographySourceUrl = "https://openlibrary.org/authors/OL1A" };
        var homonym = new AuthorProfile("/authors/OL2A", "different.jpg", "Another person's life", "other");
        Assert.False(homonym.ApplyTo(author, refreshBiography: true));
        Assert.Null(author.PhotoUrl);
        var incomplete = new AuthorProfile("/authors/OL1A", null, "No attribution", null);
        Assert.False(incomplete.ApplyTo(author, refreshBiography: true));
        Assert.Equal("Existing summary", author.Biography);
        Assert.Equal("https://openlibrary.org/authors/OL1A", author.BiographySourceUrl);
    }

    [Fact]
    public async Task ReadsThePersistedAuthorKeyOnlyWhenTheNameStillMatches()
    {
        var directory = Directory.CreateTempSubdirectory("bibliotheca-author-key-");
        try
        {
            using var handler = new AuthorPhotosHandler("photos");
            await using var original = CreateFactory(directory);
            await using var factory = WithAuthorPhotos(original, handler);
            var lookup = factory.Services.GetRequiredService<IAuthorProfileLookup>();
            Assert.Null(await lookup.FindProfileByKeyAsync("https://example.invalid/author", "Eva Smith", default));
            Assert.Null(await lookup.FindProfileByKeyAsync("/authors/OL1A", "Eva Smith", default));
            var profile = await lookup.FindProfileByKeyAsync("/authors/OL2A", "Eva Smith", default);
            Assert.Equal("/authors/OL2A", profile!.SourceAuthorKey);
            Assert.Equal("https://covers.openlibrary.org/a/id/321-M.jpg?default=false", profile.PhotoUrl);
            Assert.Equal("Evangeline Smith was born in 1900.", profile.Biography);
            Assert.Equal(["/authors/OL1A.json", "/authors/OL2A.json"], handler.Paths.ToArray());
        }
        finally { directory.Delete(true); }
    }

    private static WebApplicationFactory<Program> WithAuthorPhotos(WebApplicationFactory<Program> original, AuthorPhotosHandler handler) =>
        original.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ExternalEditionSearch:Providers:openLibrary:ContactEmail"] = "test@example.invalid",
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAuthorPhotoLookup>();
                services.AddSingleton(services =>
                    services.GetServices<IExternalEditionProvider>().OfType<IAuthorPhotoLookup>().Single());
                services.AddHttpClient("openLibrary").ConfigurePrimaryHttpMessageHandler(() => handler);
            });
        });

    private static StoreFetchedBookArgs PhotoBook(string isbn, params string[] authors) => new()
    {
        Book = new FetchedBook { Isbn = isbn, Title = "Fantastic Mr Fox", Authors = authors, PublishedBy = "Puffin",
            Url = "https://example.invalid/book", Success = true }
    };

    private sealed class AuthorPhotosHandler(string mode) : HttpMessageHandler
    {
        public ConcurrentQueue<string> Paths { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Paths.Enqueue(path);
            if (mode == "timeout")
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            if (mode == "failure")
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            var content = path switch
            {
                "/isbn/9780140328721.json" when mode == "ambiguous" => """
                    {"key":"/books/OL1M","title":"Fantastic Mr. Fox","isbn_10":["0140328726"],
                    "authors":[{"key":"/authors/OL1A"},{"key":"/authors/OL2A"}]}
                    """,
                "/isbn/9780140328721.json" or "/books/OL1M.json" => """
                    {"key":"/books/OL1M","title":"Fantastic Mr. Fox","isbn_10":["0140328726"],
                    "authors":[{"key":"/authors/OL1A"},{"key":"/authors/OL2A"},{"key":"/authors/OL3A"}]}
                    """,
                "/authors/OL1A.json" when mode.StartsWith("wiki") => """
                    {"key":"/authors/OL1A","name":"Roald Dahl","photos":[123],"remote_ids":{"wikidata":"Q25161"},
                    "links":[{"url":"http://en.wikipedia.org/wiki/Roald_Dahl"}]}
                    """,
                "/authors/OL1A.json" => """
                    {"key":"/authors/OL1A","name":"Roald Dahl","photos":[-1,123],"birth_date":"13 September 1916",
                    "birth_place":"Cardiff","death_date":"23 November 1990"}
                    """,
                "/authors/OL2A.json" when mode == "ambiguous" => """{"key":"/authors/OL2A","name":"Roald Dahl"}""",
                "/authors/OL2A.json" => """{"key":"/authors/OL2A","name":"Evangeline Smith","alternate_names":["Eva Smith"],"photos":[321],"birth_date":"1900"}""",
                "/authors/OL3A.json" => """{"key":"/authors/OL3A","name":"Sam Doe","photos":[-1]}""",
                "/w/api.php" when mode == "wiki matching" => """
                    {"query":{"pages":[{"ns":0,"pageid":42,"pageprops":{"wikibase_item":"Q25161"},"extract":"A fixture biography."}]}}
                    """,
                "/w/api.php" when mode == "wiki homonym" => """
                    {"query":{"pages":[{"ns":0,"pageid":42,"pageprops":{"wikibase_item":"Q999"},"extract":"Another person."}]}}
                    """,
                _ => null,
            };
            return content is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, "application/json") };
        }
    }
}
