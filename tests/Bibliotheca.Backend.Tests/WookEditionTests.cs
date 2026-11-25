using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Bibliotheca.Backend.Data;
using Bibliotheca.Backend.Fetchers;
using Bibliotheca.Backend.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bibliotheca.Backend.Tests;

public partial class HttpApplicationTests
{
    private const string WookPrint = "livro/ensaio-sobre-a-cegueira/15825486";
    private const string WookIllustrated = "livro/ensaio-sobre-a-cegueira/32256945";
    private const string WookCanonical = "https://www.wook.pt/livro/ensaio-sobre-a-cegueira-ilustrado/32256945";
    private const string WookImage = "https://img.wook.pt/capas/32256945.jpg";

    [Fact]
    public async Task WookContributesDistinctEditionsAndImportsTheSelectedDetail()
    {
        var directory = Directory.CreateTempSubdirectory("bibliotheca-wook-tests-");
        try
        {
            var search = new WookReaderFixture((url, _) => Task.FromResult(url.AbsolutePath switch
            {
                "/pesquisa" => new WookPage(url.AbsoluteUri, 200, WookSearchHtml),
                "/" + WookIllustrated => new WookPage(WookCanonical, 200, WookProductHtml),
                _ => throw new InvalidOperationException("Unexpected Wook address.")
            }));
            var secondary = new ExternalProviderFixture("bookFixture", (_, _, _, _, _) =>
                Task.FromResult<IReadOnlyList<ExternalEdition>>([new("other-provider", "Other display title", Isbn: "9789720046833")]));
            await using var baseFactory = CreateFactory(directory);
            await using var factory = baseFactory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ExternalEditionSearch:Providers:wook:Enabled"] = "true",
                    ["ExternalEditionSearch:Providers:wook:Priority"] = "1",
                    ["ExternalEditionSearch:Providers:wook:ResultLimit"] = "2",
                    ["ExternalEditionSearch:Providers:bookFixture:Enabled"] = "true"
                }));
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IWookPageReader>();
                    services.AddSingleton<IWookPageReader>(search);
                    services.AddSingleton<IExternalEditionProvider>(secondary);
                });
            });
            using var browser = CreateClient(factory);
            await RegisterAndLoginAsync(browser);
            using var api = CreateApiClient(factory, browser);
            var tokens = await GetTokensAsync(browser, api);
            api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                tokens.GetProperty("access_token").GetString());

            var providers = await api.GetFromJsonAsync<ExternalProviderStatus[]>("/api/external-editions/providers");
            Assert.Equal(new ExternalProviderStatus("wook", "Wook", "active", true), providers![0]);
            const string url = "/api/external-editions/search?title=Ensaio%20sobre%20a%20Cegueira&author=Jos%C3%A9%20Saramago";
            var result = await api.GetFromJsonAsync<ExternalEditionSearchResponse>(url);
            Assert.Empty(result!.FailedProviders);
            Assert.Collection(result.Items,
                print =>
                {
                    Assert.Equal(WookPrint, print.ExternalId);
                    Assert.Equal("Ensaio sobre a Cegueira", print.Title);
                    Assert.Equal("José Saramago", print.Author);
                    Assert.Equal("9789720046833", print.Isbn);
                    Assert.Equal("Português", print.Language);
                    Assert.Equal("Porto Editora", print.Publisher);
                    Assert.Equal(2015, print.Year);
                    Assert.Equal("https://www.wook.pt/imagens/capa.jpg", print.CoverUrl);
                    Assert.Equal("https://www.wook.pt/" + WookPrint, print.Sources[0].Url);
                    Assert.Equal(["wook", "bookFixture"], print.Sources.Select(source => source.ProviderId));
                },
                illustrated =>
                {
                    Assert.Equal(WookIllustrated, illustrated.ExternalId);
                    Assert.Null(illustrated.Isbn);
                    Assert.Null(illustrated.Publisher);
                    Assert.Equal("wook", Assert.Single(illustrated.Sources).ProviderId);
                });
            var query = QueryHelpers.ParseQuery(Assert.Single(search.Requests).Query);
            Assert.Equal("Ensaio sobre a Cegueira José Saramago", query["keyword"].ToString());

            const string importPath = "/api/external-editions/import";
            var request = new { providerId = "wook", externalId = WookIllustrated };
            using var response = await api.PostAsJsonAsync(importPath, request);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            var status = await WaitForImportAsync(api, response.Headers.Location!);
            Assert.Equal("completed", status.Status);

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BibliothecaDbContext>();
            var book = await db.Books.AsNoTracking().Include(row => row.Authors).Include(row => row.Publisher)
                .Include(row => row.Collection).Include(row => row.Genres).Include(row => row.ExternalLinks)
                .SingleAsync(row => row.BookId == status.BookId);
            Assert.Equal("", book.Isbn);
            Assert.Equal("Edição ilustrada", book.Subtitle);
            Assert.Equal(["Autor Adicional", "José Saramago"], book.Authors!.Select(author => author.AuthorName).Order());
            Assert.Equal("Porto Editora", book.Publisher!.Name);
            Assert.Equal(new DateTime(2026, 1, 12), book.PublishedOn);
            Assert.Equal("Obras de José Saramago", book.Collection!.Name);
            Assert.Equal("Romance", Assert.Single(book.Genres!).Genre);
            Assert.Equal("356", book.AdditionalData![FetchedBook.FetchedBookMetadataTotalPages].ToString());
            Assert.Equal("Uma descrição de teste.", book.AdditionalData[FetchedBook.FetchedBookMetadataSynopsis].ToString());
            Assert.Contains(book.ExternalLinks, link => link.Url == WookCanonical);
            Assert.Contains(book.ExternalLinks, link => link.Url == WookImage);

            using var repeated = await api.PostAsJsonAsync(importPath, request);
            Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
            Assert.Equal(2, search.Requests.Count);
            var refreshed = await api.GetFromJsonAsync<ExternalEditionSearchResponse>(url);
            Assert.Equal(book.BookId, Assert.Single(refreshed!.Items, edition => edition.ExternalId == WookIllustrated).BookId);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task WookHandlesEmptySearchBlockedPagesMissingDetailsAndCancellation()
    {
        var directory = Directory.CreateTempSubdirectory("bibliotheca-wook-failures-");
        try
        {
            var page = new WookPage("https://www.wook.pt/pesquisa", 200,
                "<main><p>Não foram encontrados resultados.</p></main>");
            var reader = new WookReaderFixture((_, token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(page);
            });
            await using var baseFactory = CreateFactory(directory);
            await using var factory = baseFactory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ExternalEditionSearch:Providers:wook:Enabled"] = "true",
                    ["ExternalEditionSearch:Providers:bookFixture:Enabled"] = "true"
                }));
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IWookPageReader>();
                    services.AddSingleton<IWookPageReader>(reader);
                    services.AddSingleton<IExternalEditionProvider>(new ExternalProviderFixture("bookFixture", (_, _, _, _, _) =>
                        Task.FromResult<IReadOnlyList<ExternalEdition>>([new("fallback", "Fallback edition")])));
                });
            });
            using var scope = factory.Services.CreateScope();
            var aggregated = scope.ServiceProvider.GetRequiredService<IExternalEditionSearch>();
            var provider = Assert.Single(factory.Services.GetServices<IExternalEditionProvider>(), item => item.Id == "wook");
            var resolver = Assert.IsAssignableFrom<IExternalEditionResolver>(provider);
            Assert.Empty(await provider.SearchAsync(" Ensaio ", null, 5, null, default));
            Assert.Equal("Ensaio", QueryHelpers.ParseQuery(Assert.Single(reader.Requests).Query)["keyword"].ToString());

            page = page with { Status = 403, Html = "<title>Attention Required!</title>" };
            var results = await aggregated.SearchAsync("Ensaio", null, default);
            Assert.Equal(["wook"], results.FailedProviders);
            Assert.Equal("fallback", Assert.Single(results.Items).ExternalId);

            page = page with { Status = 200, Html = "<main><h1>Unexpected search page</h1></main>" };
            await Assert.ThrowsAsync<InvalidDataException>(() => provider.SearchAsync("Ensaio", null, 5, null, default));
            page = page with { Url = "https://www.wook.pt/" + WookIllustrated, Html = WookMinimalProductHtml };
            var redirected = Assert.Single(await provider.SearchAsync("Ensaio", null, 5, null, default));
            Assert.Equal(WookIllustrated, redirected.ExternalId);
            Assert.Null(redirected.Isbn);
            Assert.Null(redirected.Year);
            Assert.Null(redirected.Author);
            Assert.Equal("Livro", redirected.Format);
            page = page with { Url = "https://www.wook.pt/" + WookPrint };
            Assert.Null(await resolver.GetEditionAsync(WookIllustrated, null, default));
            page = page with { Status = 404 };
            Assert.Null(await resolver.GetEditionAsync(WookIllustrated, null, default));

            var requests = reader.Requests.Count;
            Assert.Null(await resolver.GetEditionAsync("//example.invalid/livro/edition/1", null, default));
            Assert.Equal(requests, reader.Requests.Count);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                provider.SearchAsync("Ensaio", null, 5, null, new CancellationToken(true)));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private sealed class WookReaderFixture(Func<Uri, CancellationToken, Task<WookPage>> read) : IWookPageReader
    {
        public List<Uri> Requests { get; } = [];

        public Task<WookPage> ReadAsync(Uri address, CancellationToken cancellationToken)
        {
            Requests.Add(address);
            return read(address, cancellationToken);
        }
    }

    private const string WookSearchHtml = """
        <main class="search-results">
          <article class="product-card" data-isbn="978-972-0-04683-3">
            <a href="/livro/ensaio-sobre-a-cegueira/15825486?tracking=search"><img data-src="/imagens/capa.jpg 265w" src="/imagens/capa.jpg"></a>
            <h2><a href="/livro/ensaio-sobre-a-cegueira/15825486">Ensaio sobre a Cegueira</a></h2>
            <p class="authors">de <a href="/autor/jose-saramago/2316">José Saramago</a></p>
            <p class="language">Português</p>
            <p class="publisher">Porto Editora</p>
            <time itemprop="datePublished" datetime="2015-01-01"></time>
          </article>
          <article><h2><a href="/livro/ensaio-sobre-a-cegueira/32256945">Ensaio sobre a Cegueira</a></h2></article>
          <article><h2><a href="/ebook/ensaio-sobre-a-cegueira/16161300">Ensaio sobre a Cegueira</a></h2></article>
        </main>
        """;

    private const string WookProductHtml = """
        <meta property="og:image" content="https://img.wook.pt/capas/32256945.jpg">
        <table>
          <caption>
            <p class="font-medium">Ensaio sobre a Cegueira</p>
            <p class="subtitle">Edição ilustrada</p>
            <p class="authors">de <a href="/autor/jose-saramago/2316">José Saramago</a> e
              <a href="/autor/autor-adicional/8888">Autor Adicional</a> ; Ilustração:
              <a href="/autor/ilustrador/9999">Ilustrador de Exemplo</a></p>
          </caption>
          <tbody>
            <tr><td>Editor:</td><td>Porto Editora</td></tr>
            <tr><td>Data de Lançamento:</td><td>12 de janeiro de 2026</td></tr>
            <tr><td>Idioma:</td><td>Português</td></tr>
            <tr><td>Encadernação:</td><td>Capa dura</td></tr>
            <tr><td>Páginas:</td><td>356</td></tr>
            <tr><td>Coleção:</td><td>Obras de José Saramago</td></tr>
            <tr><td>Classificação Temática:</td><td>Livros em Português &gt; Literatura &gt; Romance</td></tr>
          </tbody>
        </table>
        <div id="synopsis-toggle-collapse">Uma descrição de teste.</div>
        """;

    private const string WookMinimalProductHtml = """
        <table><caption><p class="font-medium">Ensaio sobre a Cegueira</p></caption></table>
        """;
}
