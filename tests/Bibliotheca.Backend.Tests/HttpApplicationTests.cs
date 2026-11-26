using Bibliotheca.Backend.Models;
using Bibliotheca.Backend.Controllers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bibliotheca.Backend.Data;
using Bibliotheca.Backend.Fetchers;
using Bibliotheca.Backend.Jobs;
using Bibliotheca.Backend.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bibliotheca.Backend.Tests;

public partial class HttpApplicationTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public async Task ExternalSearchAggregatesEditionsAndReturnsPartialResults()
    {
        var directory = Directory.CreateTempSubdirectory("bibliotheca-search-tests-");
        try
        {
            await using var baseFactory = CreateFactory(directory);
            var canResolveNoIsbn = true;
            var primary = new ExternalProviderFixture("primary", (_, _, limit, credential, _) =>
            {
                Assert.Equal(3, limit);
                Assert.Equal("fixture-secret", credential);
                return Task.FromResult<IReadOnlyList<ExternalEdition>>([
                    new("matilda", "Matilda", "Roald Dahl", "0-14-032872-6"),
                    new("no-isbn", "Matilda without ISBN"),
                    new("no-isbn", "Same edition repeated"),
                    new("over-limit", "Should not appear")
                ]);
            }, RequiresCredential: true, Resolve: id => id switch
            {
                "matilda" => new("matilda", "Matilda", Isbn: "9780140328721"),
                "no-isbn" when canResolveNoIsbn => new("no-isbn", "Selected edition without ISBN", "Selected Author", Publisher: "Selected publisher"),
                "no-isbn" => throw new HttpRequestException("Provider offline"),
                "new-isbn" => new("new-isbn", "Selected edition by ISBN", Isbn: "9780140328738"),
                _ => null
            });
            var secondary = new ExternalProviderFixture("secondary", (_, _, _, _, _) =>
                Task.FromResult<IReadOnlyList<ExternalEdition>>([
                    new("other-matilda", "Matilda secondary", Isbn: "978 0140328721"),
                    new("no-isbn", "Different edition without ISBN")
                ]));
            var broken = new ExternalProviderFixture("broken", (_, _, _, _, _) => throw new HttpRequestException());
            var slow = new ExternalProviderFixture("slow", async (_, _, _, _, token) =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return [];
            });
            var unconfigured = new ExternalProviderFixture("unconfigured",
                (_, _, _, _, _) => throw new InvalidOperationException(), RequiresCredential: true);
            var disabled = new ExternalProviderFixture("disabled", (_, _, _, _, _) => throw new InvalidOperationException());
            await using var factory = baseFactory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ExternalEditionSearch:Providers:primary:Enabled"] = "true",
                    ["ExternalEditionSearch:Providers:primary:Priority"] = "1",
                    ["ExternalEditionSearch:Providers:primary:ResultLimit"] = "3",
                    ["ExternalEditionSearch:Providers:primary:CredentialKey"] = "TestProviderSecret",
                    ["ExternalEditionSearch:Providers:secondary:Enabled"] = "true",
                    ["ExternalEditionSearch:Providers:broken:Enabled"] = "true",
                    ["ExternalEditionSearch:Providers:slow:Enabled"] = "true",
                    ["ExternalEditionSearch:Providers:slow:TimeoutSeconds"] = "1",
                    ["ExternalEditionSearch:Providers:unconfigured:Enabled"] = "true",
                    ["TestProviderSecret"] = "fixture-secret",
                }));
                builder.ConfigureTestServices(services =>
                {
                    foreach (var provider in new[] { secondary, primary, broken, slow, unconfigured, disabled })
                        services.AddSingleton<IExternalEditionProvider>(provider);
                });
            });
            using var client = CreateClient(factory);
            const string search = "/api/external-editions/search";
            const string providers = "/api/external-editions/providers";
            using var anonymousSearch = await client.GetAsync(search + "?title=Matilda");
            using var anonymousProviders = await client.GetAsync(providers);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousSearch.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousProviders.StatusCode);
            var openApi = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
            Assert.True(openApi.GetProperty("paths").TryGetProperty(search, out _));
            Assert.True(openApi.GetProperty("paths").TryGetProperty(providers, out _));

            await RegisterAndLoginAsync(client);
            using var apiClient = CreateApiClient(factory, client);
            var tokens = await GetTokensAsync(client, apiClient);
            apiClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", tokens.GetProperty("access_token").GetString());
            using var invalidSearch = await apiClient.GetAsync(search + "?title=%20");
            Assert.Equal(HttpStatusCode.BadRequest, invalidSearch.StatusCode);

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BibliothecaDbContext>();
            var book = new Book { Title = "Matilda", Isbn = "0-14-032872-6", CreatedBy = "test", ModifiedBy = "test" };
            db.Books.Add(book);
            var foreignShelf = new Shelve
            {
                ShelveId = 42,
                ShelveName = "Bob's shelf",
                User = new User { Name = "bob", FullName = "Bob", Email = "bob@example.invalid", Password = "fixture", Settings = "{}" },
            };
            db.Shelves.Add(foreignShelf);
            await db.SaveChangesAsync();

            var statusJson = await apiClient.GetStringAsync(providers);
            Assert.DoesNotContain("fixture-secret", statusJson);
            Assert.DoesNotContain("TestProviderSecret", statusJson);
            var statuses = JsonSerializer.Deserialize<ExternalProviderStatus[]>(statusJson, JsonSerializerOptions.Web)!;
            Assert.Equal("active", Assert.Single(statuses, row => row.Id == "primary").State);
            Assert.False(Assert.Single(statuses, row => row.State == "requiresConfiguration").Available);
            Assert.All(statuses.Where(row => row.Id is "disabled" or "openLibrary"), status =>
            {
                Assert.Equal("disabled", status.State);
                Assert.False(status.Available);
            });

            var results = await apiClient.GetFromJsonAsync<ExternalEditionSearchResponse>(search + "?title=Matilda&author=Dahl");
            Assert.Equal(["broken", "slow"], results!.FailedProviders);
            Assert.Equal(3, results.Items.Count);
            var matched = results.Items[0];
            Assert.Equal("matilda", matched.ExternalId);
            Assert.Equal("9780140328721", matched.Isbn);
            Assert.Equal(book.BookId, matched.BookId);
            Assert.Equal(["primary", "secondary"], matched.Sources.Select(source => source.ProviderId));
            Assert.Equal(2, results.Items.Count(item => item.ExternalId == "no-isbn" && item.BookId is null));

            const string importPath = "/api/external-editions/import";
            var request = new { providerId = "primary", externalId = "no-isbn", shelfId = 1 };
            using var anonymousEditionImport = await client.PostAsJsonAsync(importPath, request);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousEditionImport.StatusCode);
            using var wrongShelf = await apiClient.PostAsJsonAsync(importPath,
                new { request.providerId, request.externalId, shelfId = foreignShelf.ShelveId });
            Assert.Equal(HttpStatusCode.NotFound, wrongShelf.StatusCode);
            Assert.Empty(await db.BookImports.AsNoTracking().ToListAsync());
            Assert.Empty(await db.UsersBooks.AsNoTracking().ToListAsync());
            using var unknownEdition = await apiClient.PostAsJsonAsync(importPath, new { request.providerId, externalId = "unknown" });
            Assert.Equal(HttpStatusCode.NotFound, unknownEdition.StatusCode);
            using var existingEdition = await apiClient.PostAsJsonAsync(importPath,
                new { request.providerId, externalId = "matilda", request.shelfId });
            Assert.Equal(HttpStatusCode.OK, existingEdition.StatusCode);
            Assert.Equal(book.BookId, (await existingEdition.Content.ReadFromJsonAsync<ExistingExternalEditionResponse>())!.BookId);

            var parallelImports = await Task.WhenAll(apiClient.PostAsJsonAsync(importPath, request), apiClient.PostAsJsonAsync(importPath, request));
            using var firstImport = parallelImports[0];
            using var secondImport = parallelImports[1];
            var queuedImport = Assert.Single(parallelImports.Select(response => response.Headers.Location).OfType<Uri>().Distinct());
            var status = await WaitForImportAsync(apiClient, queuedImport);
            Assert.Equal("completed", status.Status);
            var imported = await db.Books.AsNoTracking().SingleAsync(row => row.BookId == status.BookId);
            Assert.Equal("", imported.Isbn);
            Assert.Equal("Selected edition without ISBN", imported.Title);
            Assert.Equal(1, await db.UsersBooks.Where(row => row.BookId == imported.BookId).Select(row => row.ShelveId).SingleAsync());
            canResolveNoIsbn = false;
            using var repeatedImport = await apiClient.PostAsJsonAsync(importPath, request);
            Assert.Equal(HttpStatusCode.OK, repeatedImport.StatusCode);
            Assert.Equal(imported.BookId, (await repeatedImport.Content.ReadFromJsonAsync<ExistingExternalEditionResponse>())!.BookId);

            using var byIsbn = await apiClient.PostAsJsonAsync(importPath, new { request.providerId, externalId = "new-isbn" });
            Assert.Equal(HttpStatusCode.Accepted, byIsbn.StatusCode);
            var isbnStatus = await WaitForImportAsync(apiClient, byIsbn.Headers.Location!);
            Assert.Equal("completed", isbnStatus.Status);
            Assert.Equal("Selected edition by ISBN", await db.Books.Where(row => row.Isbn == "9780140328738").Select(row => row.Title).SingleAsync());

            var refreshed = await apiClient.GetFromJsonAsync<ExternalEditionSearchResponse>(search + "?title=Matilda");
            Assert.Equal(imported.BookId, Assert.Single(refreshed!.Items, item => item.ExternalId == "no-isbn" &&
                item.Sources.Any(source => source.ProviderId == "primary")).BookId);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private sealed record ExternalProviderFixture(
        string Id,
        Func<string, string?, int, string?, CancellationToken, Task<IReadOnlyList<ExternalEdition>>> Search,
        bool RequiresCredential = false,
        Func<string, ExternalEdition?>? Resolve = null) : IExternalEditionProvider, IExternalEditionResolver
    {
        public string Name => Id;

        public Task<IReadOnlyList<ExternalEdition>> SearchAsync(
            string title, string? author, int limit, string? credential, CancellationToken cancellationToken)
            => Search(title, author, limit, credential, cancellationToken);

        public Task<ExternalEdition?> GetEditionAsync(string externalId, string? credential, CancellationToken cancellationToken)
            => Task.FromResult(Resolve?.Invoke(externalId));
    }

    [Fact]
    public async Task OpenLibrarySearchResolvesEditionsAndRetainsResultsWhenAnotherWorkFails()
    {
        var directory = Directory.CreateTempSubdirectory("bibliotheca-open-library-tests-");
        try
        {
            await using var baseFactory = CreateFactory(directory);
            await using var factory = baseFactory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ExternalEditionSearch:Providers:openLibrary:ContactEmail"] = "test@example.invalid",
                    }));
                builder.ConfigureTestServices(services => services.AddHttpClient("openLibrary")
                    .ConfigurePrimaryHttpMessageHandler(() => new OpenLibraryHandler()));
            });
            var provider = Assert.Single(factory.Services.GetServices<IExternalEditionProvider>(), item => item.Id == "openLibrary");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var editions = await provider.SearchAsync("Matilda & friends", "Roald Dahl", 4, null, deadline.Token);
            var edition = Assert.Single(editions);
            Assert.Equal("OL1M", edition.ExternalId);
            Assert.Equal("Roald Dahl", edition.Author);
            Assert.Equal("eng", edition.Language);
            Assert.Equal("Test publisher", edition.Publisher);
            Assert.Equal(1988, edition.Year);
            Assert.Equal("https://covers.openlibrary.org/b/id/123-M.jpg", edition.CoverUrl);
            Assert.Null(edition.Isbn);

            var resolver = Assert.IsAssignableFrom<IExternalEditionResolver>(provider);
            var resolved = await resolver.GetEditionAsync(edition.ExternalId, null, deadline.Token);
            Assert.NotNull(resolved);
            Assert.Equal(edition.ExternalId, resolved.ExternalId);
            Assert.Null(resolved.Isbn);
            Assert.Null(await resolver.GetEditionAsync("OL1W", null, deadline.Token));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private sealed class OpenLibraryHandler : HttpMessageHandler
    {
        private const string Edition = """
            {"key":"/books/OL1M","title":"Matilda","publishers":["Test publisher"],
             "languages":[{"key":"/languages/eng"}],"publish_date":"October 1988","covers":[123]}
            """;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = Assert.IsType<Uri>(request.RequestUri);
            Assert.Equal("https://openlibrary.org", uri.GetLeftPart(UriPartial.Authority));
            Assert.Contains("test@example.invalid", request.Headers.UserAgent.ToString());
            var query = QueryHelpers.ParseQuery(uri.Query);
            var response = uri.AbsolutePath switch
            {
                "/search.json" => SearchResponse(),
                "/works/OL1W/editions.json" => JsonResponse("{\"entries\":[" + Edition + "]}"),
                "/works/OL2W/editions.json" => new HttpResponseMessage(HttpStatusCode.TooManyRequests),
                "/books/OL1M.json" => JsonResponse(Edition),
                _ => throw new InvalidOperationException($"Unexpected Open Library request {uri.AbsolutePath}"),
            };
            return Task.FromResult(response);

            HttpResponseMessage SearchResponse()
            {
                Assert.Equal("Matilda & friends", query["title"].ToString());
                Assert.Equal("Roald Dahl", query["author"].ToString());
                return JsonResponse("""
                    {"docs":[{"key":"/works/OL1W","author_name":["Roald Dahl"]},{"key":"/works/OL2W"}]}
                    """);
            }
        }

        private static HttpResponseMessage JsonResponse(string json) =>
            new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    [Fact]
    public async Task GoogleBooksSearchMapsOptionalFieldsAndSurfacesQuotaErrors()
    {
        var directory = Directory.CreateTempSubdirectory("bibliotheca-google-books-tests-");
        try
        {
            await using var baseFactory = CreateFactory(directory);
            await using var factory = baseFactory.WithWebHostBuilder(builder =>
                builder.ConfigureTestServices(services => services.AddHttpClient("googleBooks")
                    .ConfigurePrimaryHttpMessageHandler(() => new GoogleBooksHandler())));
            var provider = Assert.Single(factory.Services.GetServices<IExternalEditionProvider>(), item => item.Id == "googleBooks");
            Assert.True(provider.RequiresCredential);
            await Assert.ThrowsAsync<InvalidOperationException>(() => provider.SearchAsync("Matilda", null, 5, null, default));
            var rows = await provider.SearchAsync("Matilda", "Roald Dahl", 5, "fixture-key", default);
            var edition = Assert.Single(rows);
            Assert.Equal("volume_1", edition.ExternalId);
            Assert.Equal("9780140328721", edition.Isbn);
            Assert.Equal(1988, edition.Year);
            Assert.Null(edition.CoverUrl);
            var resolved = await Assert.IsAssignableFrom<IExternalEditionResolver>(provider)
                .GetEditionAsync(edition.ExternalId, "fixture-key", default);
            Assert.NotNull(resolved);
            Assert.Equal(edition with { Details = null }, resolved with { Details = null });
            Assert.Equal(new[] { "J. K. Rowling", "John Tiffany", "Le Guin, Ursula" }, resolved.Details!.Authors);
            var quota = await Assert.ThrowsAsync<HttpRequestException>(
                () => provider.SearchAsync("Quota failure", null, 5, "fixture-key", default));
            Assert.Equal(HttpStatusCode.TooManyRequests, quota.StatusCode);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private sealed class GoogleBooksHandler : HttpMessageHandler
    {
        private const string Volume = """
            {"id":"volume_1","volumeInfo":{"title":"Matilda","publishedDate":"1988-04",
             "authors":["J. K. Rowling","John Tiffany","Le Guin, Ursula"],
             "industryIdentifiers":[{"type":"ISBN_10","identifier":"0140328726"}]}}
            """;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = Assert.IsType<Uri>(request.RequestUri);
            var query = QueryHelpers.ParseQuery(uri.Query);
            Assert.Equal("fixture-key", query["key"].ToString());
            if (query.TryGetValue("q", out var terms) && terms.ToString().Contains("Quota failure"))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests));

            var payload = Volume;
            if (uri.AbsolutePath == "/books/v1/volumes")
            {
                Assert.Equal("intitle:\"Matilda\" inauthor:\"Roald Dahl\"", query["q"].ToString());
                Assert.Equal("5", query["maxResults"].ToString());
                payload = "{\"items\":[" + Volume + ",{\"id\":\"incomplete\"}]}";
            }
            else
            {
                Assert.Equal("/books/v1/volumes/volume_1", uri.AbsolutePath);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        }
    }

    [Fact]
    public async Task HardcoverSearchResolvesEditionsAndHandlesQuotaAndGraphQlErrors()
    {
        var directory = Directory.CreateTempSubdirectory("bibliotheca-hardcover-tests-");
        try
        {
            await using var baseFactory = CreateFactory(directory);
            await using var factory = baseFactory.WithWebHostBuilder(builder =>
                builder.ConfigureTestServices(services => services.AddHttpClient("hardcover")
                    .ConfigurePrimaryHttpMessageHandler(() => new HardcoverHandler())));
            var provider = Assert.Single(factory.Services.GetServices<IExternalEditionProvider>(), item => item.Id == "hardcover");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var editions = await provider.SearchAsync("Matilda", "Roald Dahl", 5, "fixture-token", timeout.Token);
            var edition = Assert.Single(editions);
            Assert.Equal("10", edition.ExternalId);
            Assert.Equal("hardcover", edition.Format);
            Assert.Equal("English", edition.Language);
            Assert.Equal("Roald Dahl, Example Coauthor", edition.Author);
            Assert.Equal("Puffin", edition.Publisher);
            Assert.Equal("https://example.invalid/cover.jpg", edition.CoverUrl);
            Assert.Null(edition.Isbn);
            var resolved = await Assert.IsAssignableFrom<IExternalEditionResolver>(provider)
                .GetEditionAsync("10", "fixture-token", timeout.Token);
            Assert.NotNull(resolved);
            Assert.Equal(edition with { Details = null }, resolved with { Details = null });
            Assert.Equal(new[] { "Roald Dahl", "Example Coauthor" }, resolved.Details!.Authors);
            var quota = await Assert.ThrowsAsync<HttpRequestException>(
                () => provider.SearchAsync("Quota failure", null, 5, "fixture-token", timeout.Token));
            Assert.Equal(HttpStatusCode.TooManyRequests, quota.StatusCode);
            await Assert.ThrowsAsync<HttpRequestException>(
                () => provider.SearchAsync("GraphQL failure", null, 5, "fixture-token", timeout.Token));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task HardcoverScriptUsesTheSearchAndResolveEndpointsThroughTheExistingProvider()
    {
        var directory = Directory.CreateTempSubdirectory("bibliotheca-hardcover-pilot-");
        try
        {
            await using var baseFactory = CreateFactory(directory);
            await using var factory = baseFactory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Extractors:Directory"] = Path.Combine(AppContext.BaseDirectory, "provider-fixtures"),
                        ["Extractors:Providers:hardcover:Enabled"] = "true",
                        ["Extractors:Providers:hardcover:ResourceOrigins:0"] = "https://api.hardcover.app"
                    }));
                builder.ConfigureTestServices(services => services.AddHttpClient("hardcover")
                    .ConfigurePrimaryHttpMessageHandler(() => new HardcoverHandler()));
            });
            var provider = Assert.Single(factory.Services.GetServices<IExternalEditionProvider>(), item => item.Id == "hardcover");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var edition = Assert.Single(await provider.SearchAsync("Matilda", "Roald Dahl", 5, "fixture-token", timeout.Token));
            Assert.Equal("10", edition.ExternalId);
            Assert.Equal("Matilda", edition.Title);
            var resolved = await Assert.IsAssignableFrom<IExternalEditionResolver>(provider)
                .GetEditionAsync("10", "fixture-token", timeout.Token);
            Assert.NotNull(resolved);
            Assert.Equal(edition with { Details = null }, resolved with { Details = null });
            Assert.Equal(new[] { "Roald Dahl", "Example Coauthor" }, resolved.Details!.Authors);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private sealed class HardcoverHandler : HttpMessageHandler
    {
        private const string Edition = """
            {"id":10,"title":"Matilda","edition_format":"hardcover","release_year":1988,"language":{"language":"English"},
             "publisher":{"name":"Puffin"},"image":{"url":"https://example.invalid/cover.jpg"},
             "contributions":[{"author":{"name":"Quentin Blake"},"contributor_role":{"name":"Illustrator"}}],
             "book":{"slug":"matilda","contributions":[{"author":{"name":"Roald Dahl"}},
                     {"author":{"name":"Example Coauthor"},"contribution":"Co-author"}]}}
            """;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("https://api.hardcover.app/v1/graphql", request.RequestUri?.AbsoluteUri);
            Assert.Equal("Bearer fixture-token", request.Headers.Authorization?.ToString());
            var body = await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken);
            var query = body.GetProperty("query").GetString()!;
            var variables = body.GetProperty("variables");
            string payload;
            if (query.StartsWith("query SearchBooks", StringComparison.Ordinal))
            {
                var phrase = variables.GetProperty("query").GetString();
                if (phrase == "Quota failure")
                    return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                if (phrase == "GraphQL failure")
                    return JsonResponse("""{"errors":[{"message":"Test failure"}]}""");
                Assert.Equal("Matilda Roald Dahl", phrase);
                payload = """{"data":{"search":{"ids":[1],"error":null}}}""";
            }
            else if (query.StartsWith("query SearchEditions", StringComparison.Ordinal))
            {
                Assert.Equal(1, Assert.Single(variables.GetProperty("ids").EnumerateArray()).GetInt32());
                payload = "{\"data\":{\"books\":[{\"id\":1,\"editions\":[" + Edition + ",{\"id\":11}]}]}}";
            }
            else
            {
                Assert.StartsWith("query ResolveEdition", query);
                Assert.Equal(10, variables.GetProperty("id").GetInt32());
                payload = "{\"data\":{\"editions_by_pk\":" + Edition + "}}";
            }
            return JsonResponse(payload);
        }

        private static HttpResponseMessage JsonResponse(string json) =>
            new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    [Fact]
    public async Task FreshInstallationSupportsRegistrationLoginAndAuthenticatedLibrary()
    {
        var directory = Directory.CreateTempSubdirectory("bibliotheca-tests-");
        try
        {
            await using var factory = CreateFactory(directory);
            using var client = CreateClient(factory);

            using var unauthorized = await client.GetAsync("/api/account/settings");
            Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
            using var anonymousImport = await client.PostAsync("/api/books/fetch?isbn=9780140328721", content: null);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousImport.StatusCode);
            using var anonymousHub = await client.PostAsync("/api/bibliotheca-hub/negotiate?negotiateVersion=1", content: null);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousHub.StatusCode);
            using var registrationWithoutToken = await client.PostAsync("/account/register", new FormUrlEncodedContent(
                new Dictionary<string, string> { ["InviteCode"] = BootstrapCode }));
            Assert.Equal(HttpStatusCode.BadRequest, registrationWithoutToken.StatusCode);

            await RegisterAndLoginAsync(client, factory);
            using var apiClient = CreateApiClient(factory, client);
            apiClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token");
            using var invalidToken = await apiClient.GetAsync("/api/account/settings");
            Assert.Equal(HttpStatusCode.Unauthorized, invalidToken.StatusCode);
            apiClient.DefaultRequestHeaders.Authorization = null;
            var tokens = await GetTokensAsync(client, apiClient);
            apiClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", tokens.GetProperty("access_token").GetString());
            Assert.NotEmpty(tokens.GetProperty("refresh_token").GetString()!);
            using var forbidden = await apiClient.DeleteAsync("/api/books/42");
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            using var authenticatedHub = await apiClient.PostAsync("/api/bibliotheca-hub/negotiate?negotiateVersion=1", content: null);
            Assert.Equal(HttpStatusCode.OK, authenticatedHub.StatusCode);
            using var settings = await apiClient.GetAsync("/api/account/settings");
            Assert.Equal(HttpStatusCode.OK, settings.StatusCode);
            var profile = await apiClient.GetFromJsonAsync<JsonElement>("/connect/userinfo");
            Assert.Equal("alice", profile.GetProperty("sub").GetString());
            Assert.Equal("alice@example.invalid", profile.GetProperty("email").GetString());
            using var invite = await apiClient.PostAsync("/api/account/invite-code", content: null);
            Assert.Equal(HttpStatusCode.Forbidden, invite.StatusCode);

            var shelves = await apiClient.GetFromJsonAsync<JsonElement>("/api/library/shelves");
            Assert.Equal("Alice Reader", shelves.GetProperty("library").GetString());
            Assert.Equal("Favorites", shelves.GetProperty("shelves")[0].GetProperty("shelve").GetString());

            const string isbn = "9780140328721";
            using var upload = new MultipartFormDataContent { { new StringContent(isbn), "file", "isbn.txt" } };
            using var response = await apiClient.PostAsync("/api/library/_upload", upload);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            var uploadStatus = await WaitForImportAsync(apiClient, response.Headers.Location!);
            Assert.Equal("completed", uploadStatus.Status);
            Assert.Equal(1, uploadStatus.Succeeded);

            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (true)
            {
                var list = await apiClient.GetFromJsonAsync<JsonElement>("/api/books?list=unshelved", deadline.Token);
                var books = list.GetProperty("items");
                if (books.GetArrayLength() > 0)
                {
                    Assert.Equal("Matilda", books[0].GetProperty("title").GetString());
                    var bookId = books[0].GetProperty("id").GetInt32();
                    Assert.Equal(new byte[] { 1, 2, 3 }, await apiClient.GetByteArrayAsync($"/api/books/{bookId}/cover", deadline.Token));
                    break;
                }
                await Task.Delay(100, deadline.Token);
            }

            using var logout = await client.GetAsync(QueryHelpers.AddQueryString("/connect/logout",
                new Dictionary<string, string?>
                {
                    ["id_token_hint"] = tokens.GetProperty("id_token").GetString(),
                    ["post_logout_redirect_uri"] = "http://localhost:5173/",
                }));
            Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
            Assert.Equal("http://localhost:5173/", logout.Headers.Location?.OriginalString);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task FailedImportDoesNotBlockSubsequentIsbnRequests()
    {
        var directory = Directory.CreateTempSubdirectory("bibliotheca-tests-");
        try
        {
            await using var factory = CreateFactory(directory);
            using var client = CreateClient(factory);

            await RegisterAndLoginAsync(client);
            using var apiClient = CreateApiClient(factory, client);
            var tokens = await GetTokensAsync(client, apiClient);
            apiClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", tokens.GetProperty("access_token").GetString());

            using var otherBrowser = CreateClient(factory);
            using var otherApi = CreateApiClient(factory, otherBrowser);
            using (var userScope = factory.Services.CreateScope())
            {
                var db = userScope.ServiceProvider.GetRequiredService<BibliothecaDbContext>();
                var bob = new User
                {
                    Name = "bob", FullName = "Bob Reader", Email = "bob@example.invalid", Settings = "{}",
                };
                var hasher = userScope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.IPasswordHasher<User>>();
                bob.Password = hasher.HashPassword(bob, "private test password");
                db.Users.Add(bob);
                await db.SaveChangesAsync();
            }
            await LoginAsync(otherBrowser, "bob");
            var otherTokens = await GetTokensAsync(otherBrowser, otherApi);
            otherApi.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", otherTokens.GetProperty("access_token").GetString());

            using var hubDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var aliceHub = await ConnectHubAsync(factory, apiClient, hubDeadline.Token);
            using var secondAliceHub = await ConnectHubAsync(factory, apiClient, hubDeadline.Token);
            using var bobHub = await ConnectHubAsync(factory, otherApi, hubDeadline.Token);

            const string failedIsbn = "9780140328752";
            const string batchIsbn = "9780140328738";
            const string singleIsbn = "9780140328745";
            using var batch = await apiClient.PutAsJsonAsync("/api/fetch/isbn", new { Isbns = new[] { failedIsbn, batchIsbn } });
            Assert.Equal(HttpStatusCode.Accepted, batch.StatusCode);
            using var single = await apiClient.PostAsync($"/api/books/fetch?isbn={singleIsbn}", content: null);
            Assert.Equal(HttpStatusCode.Accepted, single.StatusCode);
            var batchStatus = await WaitForImportAsync(apiClient, batch.Headers.Location!);
            Assert.Equal("completedWithErrors", batchStatus.Status);
            Assert.Equal(1, batchStatus.Succeeded);
            Assert.Equal(1, batchStatus.Failed);
            Assert.Equal("completed", (await WaitForImportAsync(apiClient, single.Headers.Location!)).Status);

            var anotherImport = new BookImport
            {
                Owner = "another-reader", Isbns = ["9780140328721"], Total = 1,
                Succeeded = 1, CompletedAtUtc = DateTime.UtcNow,
            };
            using (var scope = factory.Services.CreateScope())
            {
                var database = scope.ServiceProvider.GetRequiredService<BibliothecaDbContext>();
                database.BookImports.Add(anotherImport);
                await database.SaveChangesAsync();
            }

            var history = await apiClient.GetFromJsonAsync<ImportBatchSummary[]>("/api/imports");
            Assert.Equal(2, history!.Length);
            Assert.Contains(history, import => import.Id == batchStatus.Id);
            using var privateImport = await apiClient.GetAsync($"/api/imports/{anotherImport.Id}");
            Assert.Equal(HttpStatusCode.NotFound, privateImport.StatusCode);

            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (true)
            {
                using var scope = factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<BibliothecaDbContext>();
                var imported = await db.Books.Select(book => book.Isbn).ToListAsync(deadline.Token);
                if (imported.Count >= 2)
                {
                    Assert.Equal(new[] { batchIsbn, singleIsbn }, imported.OrderBy(isbn => isbn));
                    break;
                }
                await Task.Delay(100, deadline.Token);
            }

            foreach (var hub in new[] { aliceHub, secondAliceHub })
            {
                var failed = await ReadBookImportedAsync(hub, hubDeadline.Token);
                Assert.Equal(failedIsbn, failed.GetProperty("isbn").GetString());
                Assert.False(failed.GetProperty("success").GetBoolean());
                Assert.Equal("processing", failed.GetProperty("import").GetProperty("status").GetString());
                var succeeded = await ReadBookImportedAsync(hub, hubDeadline.Token);
                Assert.Equal(batchIsbn, succeeded.GetProperty("isbn").GetString());
                Assert.True(succeeded.GetProperty("success").GetBoolean());
                Assert.Equal("completedWithErrors", succeeded.GetProperty("import").GetProperty("status").GetString());
            }

            const string bobIsbn = "9780140328721";
            using var bobImport = await otherApi.PostAsync($"/api/books/fetch?isbn={bobIsbn}", content: null);
            Assert.Equal(HttpStatusCode.Accepted, bobImport.StatusCode);
            var bobNotification = await ReadBookImportedAsync(bobHub, hubDeadline.Token);
            Assert.Equal(bobIsbn, bobNotification.GetProperty("isbn").GetString());
            Assert.True(bobNotification.GetProperty("success").GetBoolean());

        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ScannedBatchDeduplicatesAndRetriesOnlyFailuresOnTheOriginalShelf()
    {
        var directory = Directory.CreateTempSubdirectory("bibliotheca-scan-tests-");
        try
        {
            await using var factory = CreateFactory(directory);
            using var browser = CreateClient(factory);
            await RegisterAndLoginAsync(browser);
            using var client = CreateApiClient(factory, browser);
            var tokens = await GetTokensAsync(browser, client);
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", tokens.GetProperty("access_token").GetString());

            var shelves = await client.GetFromJsonAsync<JsonElement>("/api/library/shelves");
            var shelfId = shelves.GetProperty("shelves")[0].GetProperty("shelveId").GetInt32();
            var batchId = Guid.NewGuid();
            const string successful = "9780140328721";
            const string failed = "9780140328752";
            const string corrected = "9780140328738";

            using var rejected = await client.PutAsJsonAsync("/api/fetch/isbn",
                new { batchId, shelfId = int.MaxValue, isbns = new[] { successful } });
            Assert.Equal(HttpStatusCode.NotFound, rejected.StatusCode);

            using var first = await client.PutAsJsonAsync("/api/fetch/isbn",
                new { batchId, shelfId, isbns = new[] { successful } });
            using var duplicate = await client.PutAsJsonAsync("/api/fetch/isbn",
                new { batchId, shelfId, isbns = new[] { "0140328726" } });
            using var second = await client.PutAsJsonAsync("/api/fetch/isbn",
                new { batchId, shelfId, isbns = new[] { failed } });
            Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
            Assert.Equal(HttpStatusCode.Accepted, duplicate.StatusCode);
            Assert.Equal(first.Headers.Location, duplicate.Headers.Location);
            Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
            Assert.Equal(1, (await WaitForImportAsync(client, first.Headers.Location!)).Succeeded);
            var failure = await WaitForImportAsync(client, second.Headers.Location!);
            Assert.Equal(failed, Assert.Single(failure.IsbnFailures).Isbn);
            Assert.Equal(batchId, failure.BatchId);

            var history = await client.GetFromJsonAsync<ImportBatchSummary[]>("/api/imports");
            var summary = Assert.Single(history!, row => row.Id == batchId);
            Assert.Equal((2, 1, 0, 1, 1), (summary.Total, summary.Succeeded, summary.Pending, summary.Failed, summary.IsbnFailureCount));
            var page = await client.GetFromJsonAsync<PaginatedResult<BatchIsbnFailure>>($"/api/imports/{batchId}/failures");
            Assert.Equal(failure.Id, Assert.Single(page!.Items).ImportId);
            using var rejectedRetry = await client.PostAsJsonAsync($"/api/imports/{batchId}/retry",
                new { items = new[] { new { importId = failure.Id, position = 1, isbn = corrected } } });
            Assert.Equal(HttpStatusCode.BadRequest, rejectedRetry.StatusCode);

            using var retry = await client.PostAsJsonAsync($"/api/imports/{batchId}/retry",
                new { items = new[] { new { importId = failure.Id, position = 0, isbn = corrected } } });
            Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);
            var completedRetry = await WaitForImportAsync(client, retry.Headers.Location!);
            Assert.Equal(1, completedRetry.Total);
            Assert.Equal(1, completedRetry.Succeeded);
            Assert.Equal(shelfId, completedRetry.ShelfId);

            using var scope = factory.Services.CreateScope();
            var database = scope.ServiceProvider.GetRequiredService<BibliothecaDbContext>();
            var assigned = await database.UsersBooks.Where(row => row.ShelveId == shelfId)
                .Select(row => row.Book.Isbn).OrderBy(isbn => isbn).ToArrayAsync();
            Assert.Equal(new[] { successful, corrected }, assigned);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "ImportTiming")]
    public async Task MeasureTwentyIsbnImports()
    {
        var directory = Directory.CreateTempSubdirectory("bibliotheca-import-timing-");
        try
        {
            await using var original = CreateFactory(directory);
            await using var factory = original.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IBookFetcher>();
                services.RemoveAll<ICoverFetcher>();
                services.AddSingleton<IBookFetcher, TimedBookFetcher>();
                services.AddSingleton<ICoverFetcher, TimedCoverFetcher>();
            }));
            using var scope = factory.Services.CreateScope();
            var database = scope.ServiceProvider.GetRequiredService<BibliothecaDbContext>();
            var queue = scope.ServiceProvider.GetRequiredService<BookImportQueue>();
            var isbns = Enumerable.Range(70, 20).Select(number =>
                {
                    var prefix = $"9780140328{number}";
                    var checksum = prefix.Select((digit, index) => (digit - '0') * (index % 2 == 0 ? 1 : 3)).Sum();
                    return prefix + (10 - checksum % 10) % 10;
                }).ToArray();

            var timings = new ConcurrentDictionary<string, ConcurrentQueue<double>>();
            using var listener = new MeterListener();
            listener.InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == "Bibliotheca.IsbnImport")
                    meterListener.EnableMeasurementEvents(instrument);
            };
            listener.SetMeasurementEventCallback<double>((instrument, measurement, _, _) =>
                timings.GetOrAdd(instrument.Name, _ => new()).Enqueue(measurement));
            listener.Start();

            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var clock = Stopwatch.StartNew();
            var accepted = Assert.IsType<Microsoft.AspNetCore.Mvc.AcceptedResult>(
                await queue.SubmitAsync("benchmark", isbns, deadline.Token));
            var importId = Assert.IsType<BookImportStatus>(accepted.Value).Id;
            TimeSpan? first = null;
            BookImport landed;
            while (true)
            {
                if (first is null && await database.Books.AnyAsync(deadline.Token))
                    first = clock.Elapsed;
                landed = await database.BookImports.AsNoTracking().SingleAsync(row => row.Id == importId, deadline.Token);
                if (landed.CompletedAtUtc.HasValue)
                    break;
                await Task.Delay(10, deadline.Token);
            }

            foreach (var (stage, measurements) in timings.OrderBy(entry => entry.Key))
                output.WriteLine($"{stage}: {measurements.Count} samples, mean {measurements.Average():F1} ms, total {measurements.Sum():F1} ms");
            output.WriteLine($"First book {first?.TotalMilliseconds:F0} ms; batch {clock.Elapsed.TotalMilliseconds:F0} ms; failures {landed.Failed}.");
            Assert.NotNull(first);
            Assert.Equal(2, landed.Failed);
            Assert.Equal(18, landed.Succeeded);
            Assert.Equal(18, await database.Books.Select(book => book.Isbn).Distinct().CountAsync(deadline.Token));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private sealed class TimedBookFetcher : IBookFetcher
    {
        public async Task<FetchedBook> FetchBookAsync(string isbn, CancellationToken cancellationToken)
        {
            await Task.Delay(14 + (isbn[^2] - '0') * 3, cancellationToken);
            return new FetchedBook
            {
                Isbn = isbn, Title = $"Book {isbn}", Authors = ["Roald Dahl"], PublishedBy = "Test publisher",
                Url = "https://example.invalid/book", Success = isbn is not ("9780140328752" or "9780140328851"),
            };
        }
    }

    private sealed class TimedCoverFetcher : ICoverFetcher
    {
        public async Task<FetchedCover> FetchCoverAsync(string isbn, CancellationToken cancellationToken)
        {
            await Task.Delay(36 + (isbn[^2] - '0') * 5, cancellationToken);
            return new FetchedCover
            {
                Image = [1, 2, 3], MimeType = "image/png", Url = "https://example.invalid/cover.png", Success = true,
            };
        }
    }

    [Fact]
    public async Task ImportWorkerResumesAnInterruptedBatchAfterHostRestart()
    {
        const string remainingIsbn = "9780140328721";
        var directory = Directory.CreateTempSubdirectory("bibliotheca-tests-");
        try
        {
            var interrupted = new BookImport
            {
                Owner = "alice", Isbns = ["9780140328738", remainingIsbn], Total = 2,
                Succeeded = 1, StartedAtUtc = DateTime.UtcNow,
            };
            await using (var original = CreateFactory(directory))
            {
                using var scope = original.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<BibliothecaDbContext>();
                db.BookImports.Add(interrupted);
                await db.SaveChangesAsync();
            }

            await using var restarted = CreateFactory(directory);
            using var reader = restarted.Services.CreateScope();
            var database = reader.ServiceProvider.GetRequiredService<BibliothecaDbContext>();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (true)
            {
                var status = await database.BookImports.AsNoTracking().SingleAsync(deadline.Token);
                if (status.CompletedAtUtc.HasValue)
                {
                    Assert.Equal(interrupted.Id, status.Id);
                    Assert.Equal(2, status.Succeeded);
                    Assert.Equal(remainingIsbn, (await database.Books.SingleAsync(deadline.Token)).Isbn);
                    break;
                }
                await Task.Delay(100, deadline.Token);
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ImportWorkerResumesAnExternalEditionFromItsSnapshotAfterHostRestart()
    {
        var directory = Directory.CreateTempSubdirectory("bibliotheca-external-restart-tests-");
        try
        {
            var interrupted = new BookImport
            {
                Owner = "alice", Isbns = [], Total = 1, StartedAtUtc = DateTime.UtcNow,
                EditionProviderId = "unavailable-provider", EditionExternalId = "edition-1",
                SelectedEdition = new ExternalEdition("edition-1", "Selected edition", "Selected author",
                    Language: "por", Publisher: "Selected publisher", Year: 2020),
            };
            await using (var original = CreateFactory(directory))
            await using (var stoppedWorker = original.WithWebHostBuilder(builder =>
                builder.ConfigureTestServices(services =>
                    services.Remove(services.Single(service => service.ImplementationType == typeof(BookImportWorker))))))
            {
                using var scope = stoppedWorker.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<BibliothecaDbContext>();
                db.BookImports.Add(interrupted);
                await db.SaveChangesAsync();
            }

            await using var restarted = CreateFactory(directory);
            using var reader = restarted.Services.CreateScope();
            var database = reader.ServiceProvider.GetRequiredService<BibliothecaDbContext>();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (true)
            {
                var status = await database.BookImports.AsNoTracking().SingleAsync(deadline.Token);
                if (status.CompletedAtUtc.HasValue)
                {
                    Assert.Equal(interrupted.Id, status.Id);
                    Assert.Equal(1, status.Succeeded);
                    var reference = await database.BookEditionReferences.AsNoTracking()
                        .Include(row => row.Book).ThenInclude(book => book.Publisher).SingleAsync(deadline.Token);
                    Assert.Equal(interrupted.EditionProviderId, reference.ProviderId);
                    Assert.Equal(interrupted.EditionExternalId, reference.ExternalId);
                    Assert.Equal(reference.BookId, status.ResultBookId);
                    Assert.Equal("", reference.Book.Isbn);
                    Assert.Equal("Selected edition", reference.Book.Title);
                    Assert.Equal("Selected publisher", reference.Book.Publisher!.Name);
                    Assert.Equal(2020, reference.Book.PublishedOn?.Year);
                    break;
                }
                await Task.Delay(100, deadline.Token);
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task OpenApiDescribesTheCatalogueAndBookQueriesRequireAuthentication()
    {
        var directory = Directory.CreateTempSubdirectory("bibliotheca-tests-");
        try
        {
            await using var factory = CreateFactory(directory);
            using var client = CreateClient(factory);
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BibliothecaDbContext>();
                db.Books.Add(new Book
                {
                    Isbn = "9780140328721", Title = "Matilda", CreatedBy = "test", ModifiedBy = "test",
                    BookCover = new BookCover { Cover = [1, 2, 3], MimeType = "image/png" },
                });
                await db.SaveChangesAsync();
            }

            foreach (var path in new[] { "/api/books?list=recent", "/api/books?list=reading", "/api/books/1", "/api/books/list", "/api/books/isbn", "/api/authors", "/api/authors/1", "/api/genres", "/api/collections", "/api/series", "/api/tags" })
            {
                using var anonymous = await client.GetAsync(path);
                Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            }
            await RegisterAndLoginAsync(client);
            using var authenticated = CreateApiClient(factory, client);
            var tokens = await GetTokensAsync(client, authenticated);
            authenticated.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GetProperty("access_token").GetString());
            var catalogue = await authenticated.GetFromJsonAsync<JsonElement>("/api/books?list=recent");
            Assert.Equal("Matilda", catalogue.GetProperty("items")[0].GetProperty("title").GetString());

            using var cover = await client.GetAsync("/api/books/1/cover");
            Assert.Equal(HttpStatusCode.OK, cover.StatusCode);
            Assert.True(cover.Headers.CacheControl?.Public);
            Assert.Equal(TimeSpan.FromDays(1), cover.Headers.CacheControl?.MaxAge);
            Assert.DoesNotContain("Authorization", cover.Headers.Vary);
            Assert.Equal(new byte[] { 1, 2, 3 }, await cover.Content.ReadAsByteArrayAsync());
            Assert.NotNull(cover.Headers.ETag);
            using var conditional = new HttpRequestMessage(HttpMethod.Get, "/api/books/1/cover");
            conditional.Headers.IfNoneMatch.Add(cover.Headers.ETag);
            using var unchanged = await client.SendAsync(conditional);
            Assert.Equal(HttpStatusCode.NotModified, unchanged.StatusCode);
            Assert.Empty(await unchanged.Content.ReadAsByteArrayAsync());
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BibliothecaDbContext>();
                var changedCover = await db.Set<BookCover>().SingleAsync();
                changedCover.Cover = [4, 5, 6];
                await db.SaveChangesAsync();
            }
            using var changedRequest = new HttpRequestMessage(HttpMethod.Get, "/api/books/1/cover");
            changedRequest.Headers.IfNoneMatch.Add(cover.Headers.ETag);
            changedRequest.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
            using var changed = await client.SendAsync(changedRequest);
            Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
            Assert.NotEqual(cover.Headers.ETag, changed.Headers.ETag);
            Assert.Equal(new byte[] { 4, 5, 6 }, await changed.Content.ReadAsByteArrayAsync());
            var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
            var paths = document.GetProperty("paths");
            Assert.True(paths.GetProperty("/api/books").TryGetProperty("get", out _));
            Assert.True(paths.GetProperty("/api/books/{id}").TryGetProperty("delete", out _));
            Assert.True(paths.GetProperty("/api/library/shelves").TryGetProperty("get", out _));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ReadingDateRoundTripsThroughBookDetailsAndSupportsStatusOnlyRequests()
    {
        var directory = Directory.CreateTempSubdirectory("bibliotheca-reading-date-tests-");
        try
        {
            await using var factory = CreateFactory(directory);
            using var browser = CreateClient(factory);
            await RegisterAndLoginAsync(browser);
            using var client = CreateApiClient(factory, browser);
            var tokens = await GetTokensAsync(browser, client);
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", tokens.GetProperty("access_token").GetString());

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BibliothecaDbContext>();
            var book = new Book { Isbn = "9780140328721", Title = "Matilda", CreatedBy = "test", ModifiedBy = "test" };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            var path = $"/api/books/{book.BookId}";
            const string chosenDate = "2024-02-29";

            using var read = await client.PostAsJsonAsync(path + "/status", new { status = "Read", readingDate = chosenDate });
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            var details = await client.GetFromJsonAsync<JsonElement>(path);
            Assert.Equal(chosenDate, details.GetProperty("readingDate").GetString());
            Assert.Equal("Read", details.GetProperty("userBook").GetProperty("readingStatus").GetString());
            var personal = await db.UsersBooks.AsNoTracking().SingleAsync();
            Assert.Equal(new DateTime(2024, 2, 29), personal.ReadingTimestamp);

            using var invalid = await client.PostAsJsonAsync(path + "/status", new { status = "Read", readingDate = "2024-02-30" });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal(personal.ReadingTimestamp, (await db.UsersBooks.AsNoTracking().SingleAsync()).ReadingTimestamp);

            using var reading = await client.PostAsJsonAsync(path + "/status", new { status = "Reading" });
            Assert.Equal(HttpStatusCode.OK, reading.StatusCode);
            details = await client.GetFromJsonAsync<JsonElement>(path);
            Assert.Equal(JsonValueKind.Null, details.GetProperty("readingDate").ValueKind);
            Assert.Null((await db.UsersBooks.AsNoTracking().SingleAsync()).ReadingTimestamp);

            var before = DateOnly.FromDateTime(DateTime.Now);
            using var statusOnly = await client.PostAsJsonAsync(path + "/status", new { status = "Read" });
            Assert.Equal(HttpStatusCode.OK, statusOnly.StatusCode);
            details = await client.GetFromJsonAsync<JsonElement>(path);
            var after = DateOnly.FromDateTime(DateTime.Now);
            Assert.InRange(details.GetProperty("readingDate").Deserialize<DateOnly>(), before, after);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task UserDirectoryRequiresManagerAccessAndReturnsSearchablePersonalCounts()
    {
        var directory = Directory.CreateTempSubdirectory("bibliotheca-user-directory-tests-");
        try
        {
            await using var baseFactory = CreateFactory(directory);
            await using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration(
                (_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Authorization:CatalogueManagerUserIds:0"] = "1",
                })));
            using var browser = CreateClient(factory);
            const string path = "/api/users";
            const string refreshPath = "/api/catalogue-refresh";
            const string authorRefreshPath = "/api/author-refresh";
            const string authorDuplicatesPath = "/api/author-duplicates";
            var review = new ReviewAuthorDuplicateRequest("reject", "fixture-version", null);
            using var duplicateView = await browser.GetAsync(authorDuplicatesPath);
            using var duplicateScan = await browser.PostAsync(authorDuplicatesPath + "/scan", null);
            using var duplicateReview = await browser.PostAsJsonAsync(authorDuplicatesPath + "/1/review", review);
            Assert.Equal(HttpStatusCode.Unauthorized, duplicateView.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, duplicateScan.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, duplicateReview.StatusCode);
            using var anonymous = await browser.GetAsync(path);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            using var anonymousRefresh = await browser.GetAsync(refreshPath);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousRefresh.StatusCode);
            using var anonymousAuthorRefresh = await browser.GetAsync(authorRefreshPath);
            using var anonymousAuthorStart = await browser.PostAsync(authorRefreshPath, null);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousAuthorRefresh.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousAuthorStart.StatusCode);

            await RegisterAndLoginAsync(browser);
            using var manager = CreateApiClient(factory, browser);
            var tokens = await GetTokensAsync(browser, manager);
            manager.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", tokens.GetProperty("access_token").GetString());
            using var acceptedRefresh = await manager.PostAsync(refreshPath, null);
            Assert.Equal(HttpStatusCode.Accepted, acceptedRefresh.StatusCode);
            var refresh = await manager.GetFromJsonAsync<JsonElement>(refreshPath);
            Assert.Equal(0, refresh.GetProperty("latest").GetProperty("total").GetInt32());
            using var acceptedAuthorRefresh = await manager.PostAsync(authorRefreshPath, null);
            Assert.Equal(HttpStatusCode.Accepted, acceptedAuthorRefresh.StatusCode);
            var authorRefresh = await manager.GetFromJsonAsync<AuthorRefreshOverview>(authorRefreshPath);
            Assert.Equal(0, authorRefresh!.AuthorCount);
            Assert.NotNull(authorRefresh.Latest!.CompletedAtUtc);
            using var acceptedDuplicateScan = await manager.PostAsync(authorDuplicatesPath + "/scan", null);
            Assert.Equal(HttpStatusCode.Accepted, acceptedDuplicateScan.StatusCode);
            var duplicateQueue = await manager.GetFromJsonAsync<AuthorDuplicateOverview>(authorDuplicatesPath);
            Assert.False(duplicateQueue!.Latest!.Active);
            using var managerDuplicateReview = await manager.PostAsJsonAsync(authorDuplicatesPath + "/1/review", review);
            Assert.Equal(HttpStatusCode.NotFound, managerDuplicateReview.StatusCode);

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BibliothecaDbContext>();
            var aliceShelf = await db.Shelves.Include(shelf => shelf.User).SingleAsync();
            var bob = new User { Name = "bob-account", FullName = "Bob Reader", Email = "bob@example.invalid", Settings = "{}" };
            var carol = new User { Name = "carol-account", FullName = "Émilie Reader", Email = "carol@example.invalid", Settings = "{}", Password = "fixture" };
            var hasher = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.IPasswordHasher<User>>();
            bob.Password = hasher.HashPassword(bob, "private test password");
            var bobShelf = new Shelve { ShelveId = 1, ShelveName = "Owned", User = bob };
            db.Shelves.AddRange(bobShelf, new Shelve { ShelveId = 2, ShelveName = "Borrowed", User = bob });
            db.Users.Add(carol);

            var books = Enumerable.Range(1, 4).Select(number => new Book
            {
                Title = $"Book {number}", Isbn = "", CreatedBy = "test", ModifiedBy = "test",
            }).ToArray();
            db.Books.AddRange(books);
            db.UsersBooks.Add(new UserBook { User = aliceShelf.User, Shelve = aliceShelf, Book = books[0], ReadingStatus = BookReadingStatus.Read });
            foreach (var (personalBook, status) in books.Zip(new[] { BookReadingStatus.Read, BookReadingStatus.Reading, BookReadingStatus.Unread }))
                db.UsersBooks.Add(new UserBook { User = bob, Shelve = bobShelf, Book = personalBook, ReadingStatus = status });
            await db.SaveChangesAsync();

            using var otherBrowser = CreateClient(factory);
            await LoginAsync(otherBrowser, bob.Name);
            using var reader = CreateApiClient(factory, otherBrowser);
            var readerTokens = await GetTokensAsync(otherBrowser, reader);
            reader.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", readerTokens.GetProperty("access_token").GetString());
            using var forbidden = await reader.GetAsync(path);
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            using var forbiddenRefreshView = await reader.GetAsync(refreshPath);
            using var forbiddenRefreshStart = await reader.PostAsync(refreshPath, null);
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenRefreshView.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenRefreshStart.StatusCode);
            using var forbiddenAuthorRefreshView = await reader.GetAsync(authorRefreshPath);
            using var forbiddenAuthorRefreshStart = await reader.PostAsync(authorRefreshPath, null);
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenAuthorRefreshView.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenAuthorRefreshStart.StatusCode);
            using var forbiddenDuplicateView = await reader.GetAsync(authorDuplicatesPath);
            using var forbiddenDuplicateScan = await reader.PostAsync(authorDuplicatesPath + "/scan", null);
            using var forbiddenDuplicateReview = await reader.PostAsJsonAsync(authorDuplicatesPath + "/1/review", review);
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenDuplicateView.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenDuplicateScan.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenDuplicateReview.StatusCode);

            var names = await manager.GetFromJsonAsync<UserDirectoryResponse>(path);
            Assert.Equal(3, names!.Total);
            Assert.Equal(["alice", "bob-account", "carol-account"], names.Items.Select(user => user.Username));
            var largest = await manager.GetFromJsonAsync<UserDirectoryResponse>(path + "?sort=library&pageSize=2");
            Assert.Equal(["bob-account", "alice"], largest!.Items.Select(user => user.Username));
            Assert.Equal(new UserDirectoryEntry(bob.Id, bob.Name, bob.FullName, bob.Email, 3, 1, 1, 1), largest.Items[0]);
            Assert.Equal(1, largest.Items[1].BookCount);
            var next = await manager.GetFromJsonAsync<UserDirectoryResponse>(path + "?sort=library&pageSize=2&page=2");
            Assert.Equal(new UserDirectoryEntry(carol.Id, carol.Name, carol.FullName, carol.Email, 0, 0, 0, 0), Assert.Single(next!.Items));

            var fullName = await manager.GetFromJsonAsync<UserDirectoryResponse>(path + "?search=%20BOB%20READER%20");
            Assert.Equal(bob.Id, Assert.Single(fullName!.Items).Id);
            var accented = await manager.GetFromJsonAsync<UserDirectoryResponse>(path + "?search=%C3%89milie");
            Assert.Equal(carol.Id, Assert.Single(accented!.Items).Id);
            var wildcard = await manager.GetFromJsonAsync<UserDirectoryResponse>(path + "?search=%25");
            Assert.Empty(wildcard!.Items);
            var byUsername = await manager.GetFromJsonAsync<JsonElement>(path + "?search=carol-account");
            var entry = Assert.Single(byUsername.GetProperty("items").EnumerateArray());
            Assert.Equal(carol.Id, entry.GetProperty("id").GetInt32());
            Assert.Equal(["bookCount", "email", "fullName", "id", "read", "reading", "unread", "username"],
                entry.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));

            using var invalid = await manager.GetAsync(path + "?page=0");
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static WebApplicationFactory<Program> CreateFactory(DirectoryInfo directory) =>
        new ApplicationFactory(Path.Combine(directory.FullName, "test.db"));

    private const string BootstrapCode = "test-only-bootstrap-code-with-at-least-32-characters";

    private static async Task<BookImportStatus> WaitForImportAsync(HttpClient client, Uri location)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (true)
        {
            var status = await client.GetFromJsonAsync<BookImportStatus>(location, deadline.Token);
            if (status!.Status is "completed" or "completedWithErrors")
                return status;
            await Task.Delay(100, deadline.Token);
        }
    }

    private static async Task RegisterAndLoginAsync(
        HttpClient browser, WebApplicationFactory<Program>? legacyPasswordFixture = null)
    {
        using var firstLogin = await browser.GetAsync("/account/login?returnUrl=%2Fdashboard");
        Assert.Equal(HttpStatusCode.Redirect, firstLogin.StatusCode);
        var registerUri = new Uri(browser.BaseAddress!, firstLogin.Headers.Location!);
        Assert.Equal("/account/register", registerUri.AbsolutePath);
        Assert.False(QueryHelpers.ParseQuery(registerUri.Query).ContainsKey("InviteCode"));
        var registrationForm = await browser.GetStringAsync(registerUri);
        using var register = await browser.PostAsync("/account/register", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["InviteCode"] = BootstrapCode, ["Username"] = "alice", ["Name"] = "Alice Reader",
            ["Email"] = "alice@example.invalid", ["Password"] = "private test password", ["ReturnUrl"] = "/dashboard",
            ["__RequestVerificationToken"] = GetFormToken(registrationForm),
        }));
        Assert.Equal(HttpStatusCode.Redirect, register.StatusCode);
        Assert.Equal("/dashboard", register.Headers.Location?.OriginalString);

        using var scope = legacyPasswordFixture?.Services.CreateScope();
        var database = scope?.ServiceProvider.GetRequiredService<BibliothecaDbContext>();
        const string password = "private test password";
        if (database is not null)
        {
            var account = await database.Users.SingleAsync();
            account.Password = password.Sha256();
            await database.SaveChangesAsync();
            database.ChangeTracker.Clear();
        }

        await LoginAsync(browser, "alice");
        if (database is not null)
        {
            var account = await database.Users.SingleAsync();
            Assert.Equal(Microsoft.AspNetCore.Identity.PasswordVerificationResult.Success,
                new Microsoft.AspNetCore.Identity.PasswordHasher<User>().VerifyHashedPassword(account, account.Password, password));
        }
    }

    private static async Task LoginAsync(HttpClient browser, string username)
    {
        var loginForm = await browser.GetStringAsync("/account/login");
        using var login = await browser.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = username, ["password"] = "private test password", ["returnUrl"] = "/dashboard",
            ["__RequestVerificationToken"] = GetFormToken(loginForm),
        }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/dashboard", login.Headers.Location?.OriginalString);
    }

    private static async Task<WebSocket> ConnectHubAsync(
        WebApplicationFactory<Program> factory, HttpClient api, CancellationToken cancellationToken)
    {
        var client = factory.Server.CreateWebSocketClient();
        var url = QueryHelpers.AddQueryString("wss://localhost/api/bibliotheca-hub", "access_token",
            api.DefaultRequestHeaders.Authorization!.Parameter!);
        var socket = await client.ConnectAsync(new Uri(url), cancellationToken);
        try
        {
            await socket.SendAsync("{\"protocol\":\"json\",\"version\":1}\u001e"u8.ToArray(),
                WebSocketMessageType.Text, true, cancellationToken);
            var handshake = await ReadHubFrameAsync(socket, cancellationToken);
            Assert.False(handshake.TryGetProperty("error", out _));
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static async Task<JsonElement> ReadBookImportedAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        JsonElement message;
        do
        {
            message = await ReadHubFrameAsync(socket, cancellationToken);
        } while (message.GetProperty("type").GetInt32() == 6);
        Assert.Equal("BookImported", message.GetProperty("target").GetString());
        return message.GetProperty("arguments")[0];
    }

    private static async Task<JsonElement> ReadHubFrameAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        // SignalR delimits JSON records with a record separator, independently of WebSocket frames.
        using var record = new MemoryStream();
        var next = new byte[1];
        while (true)
        {
            var result = await socket.ReceiveAsync(next.AsMemory(), cancellationToken);
            Assert.Equal(WebSocketMessageType.Text, result.MessageType);
            if (result.Count == 0) continue;
            if (next[0] == 0x1e) break;
            record.WriteByte(next[0]);
        }

        return JsonSerializer.Deserialize<JsonElement>(record.ToArray());
    }

    private static string GetFormToken(string html)
    {
        var token = System.Text.RegularExpressions.Regex.Match(
            html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        return token;
    }

    private static HttpClient CreateApiClient(WebApplicationFactory<Program> factory, HttpClient browser) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = browser.BaseAddress!,
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    private static async Task<JsonElement> GetTokensAsync(HttpClient browser, HttpClient api)
    {
        const string callback = "http://localhost:5173/oidc-callback";
        var verifier = Base64UrlTextEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64UrlTextEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        using var consent = await browser.GetAsync(QueryHelpers.AddQueryString("/connect/authorize",
            new Dictionary<string, string?>
            {
                ["client_id"] = "bibliotheca-web",
                ["redirect_uri"] = callback,
                ["response_type"] = "code",
                ["scope"] = "openid profile email offline_access",
                ["code_challenge"] = challenge,
                ["code_challenge_method"] = "S256",
            }));
        Assert.Equal(HttpStatusCode.Redirect, consent.StatusCode);
        var code = QueryHelpers.ParseQuery(consent.Headers.Location!.Query)["code"].ToString();
        Assert.NotEmpty(code);
        using var token = await api.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = "bibliotheca-web",
            ["redirect_uri"] = callback,
            ["code_verifier"] = verifier,
            ["code"] = code,
        }));
        token.EnsureSuccessStatusCode();
        return await token.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

    private sealed class ApplicationFactory(string databasePath) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:SQLiteDefault"] = $"Data Source={databasePath};Pooling=False",
                    ["Fetchers:SaveTempFiles"] = "false",
                    ["Authentication:BootstrapCode"] = BootstrapCode,
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IBookFetcher>();
                services.RemoveAll<ICoverFetcher>();
                services.AddSingleton<IBookFetcher, FixtureBookFetcher>();
                services.AddSingleton<ICoverFetcher, FixtureCoverFetcher>();
                services.RemoveAll<IAuthorPhotoLookup>();
                services.AddSingleton<IAuthorPhotoLookup>(NoAuthorPhotoLookup.Instance);
            });
        }
    }

    private sealed class FixtureBookFetcher : IBookFetcher
    {
        public Task<FetchedBook> FetchBookAsync(string isbn, CancellationToken cancellationToken) => Task.FromResult(new FetchedBook
        {
            Isbn = isbn, Title = isbn == "9780140328721" ? "Matilda" : $"Book {isbn}",
            Authors = ["Roald Dahl"], PublishedBy = "Test publisher",
            Url = "https://example.invalid/matilda", Success = isbn != "9780140328752",
        });
    }

    private sealed class FixtureCoverFetcher : ICoverFetcher
    {
        public Task<FetchedCover> FetchCoverAsync(string isbn, CancellationToken cancellationToken) => Task.FromResult(new FetchedCover
        {
            Image = [1, 2, 3], MimeType = "image/png", Url = "https://example.invalid/matilda.png", Success = true,
        });
    }
}
