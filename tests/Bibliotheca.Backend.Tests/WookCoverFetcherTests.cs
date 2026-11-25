using System.Net;
using Bibliotheca.Backend.Fetchers;
using Bibliotheca.Backend.Options;
using Bibliotheca.Backend.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Bibliotheca.Backend.Tests;

public sealed class WookCoverFetcherTests
{
    private const string Isbn = "9789895870714";
    private const string ProductUrl = "https://www.wook.pt/livro/anjos-e-demonios/32145345";
    private const string ImageUrl = "https://img.wook.pt/capas/32145345.jpg";

    [Fact]
    public async Task DirectProductDownloadsOnlyMatchingIsbn()
    {
        var handler = new ImageHandler();
        var reader = new Reader(_ => new(ProductUrl, 200, Product("978-989-587-071-4", ImageUrl)));
        var fetcher = Create(reader, handler);

        var cover = await fetcher.FetchCoverAsync(Isbn, default);

        Assert.True(cover.Success);
        Assert.Equal(ImageUrl, cover.Url);
        Assert.Equal(new byte[] { 1, 2, 3 }, cover.Image);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(0, fetcher.Priority);
    }

    [Theory]
    [InlineData("9780140328721", ImageUrl)]
    [InlineData("", ImageUrl)]
    [InlineData(Isbn, "https://example.org/image.jpg")]
    [InlineData(Isbn, "")]
    public async Task RejectsWrongOrMissingIsbnAndUntrustedOrMissingImage(string isbn, string image)
    {
        var handler = new ImageHandler();
        var fetcher = Create(new Reader(_ => new(ProductUrl, 200, Product(isbn, image))), handler);
        Assert.False((await fetcher.FetchCoverAsync(Isbn, default)).Success);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task SearchResultsRequireProductDetailsBeforeDownloading()
    {
        const string search = """
          <main class="search-results">
            <article><h2><a href="/livro/wrong/111">Wrong</a></h2></article>
            <article><h2><a href="/livro/anjos-e-demonios/32145345">Right</a></h2></article>
          </main>
          """;
        var reader = new Reader(uri => uri.AbsolutePath switch
        {
            "/pesquisa" => new(uri.AbsoluteUri, 200, search),
            "/livro/wrong/111" => new(uri.AbsoluteUri, 200, Product("9780140328721", ImageUrl)),
            _ => new(ProductUrl, 200, Product(Isbn, ImageUrl))
        });
        var handler = new ImageHandler();
        Assert.True((await Create(reader, handler).FetchCoverAsync(Isbn, default)).Success);
        Assert.Equal(3, reader.Calls);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task IgnoresProductRedirectedToDifferentEdition()
    {
        const string search = """<main class="search-results"><article><h2><a href="/livro/wrong/111">Wrong</a></h2></article></main>""";
        var reader = new Reader(uri => uri.AbsolutePath == "/pesquisa"
            ? new(uri.AbsoluteUri, 200, search) : new(ProductUrl, 200, Product(Isbn, ImageUrl)));
        var handler = new ImageHandler();
        Assert.False((await Create(reader, handler).FetchCoverAsync(Isbn, default)).Success);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task EmptySearchFallsBack()
    {
        var reader = new Reader(uri => new(uri.AbsoluteUri, 200, "<main>Não foram encontrados resultados</main>"));
        var fallback = new Fallback();
        var service = new FetchBookCoverService([Create(reader, new ImageHandler()), fallback], NullLogger<FetchBookCoverService>.Instance);
        Assert.True((await service.ExecuteAsync(new() { Isbn = Isbn })).Success);
        Assert.Equal(1, fallback.Calls);
    }

    [Theory]
    [InlineData(403, false)]
    [InlineData(200, true)]
    public async Task BlockedPageOrDownloadFailureFallsBack(int status, bool failDownload)
    {
        var reader = new Reader(_ => new(ProductUrl, status, Product(Isbn, ImageUrl)));
        var handler = new ImageHandler { Fail = failDownload };
        var fallback = new Fallback();
        var service = new FetchBookCoverService([fallback, Create(reader, handler)], NullLogger<FetchBookCoverService>.Instance);
        var result = await service.ExecuteAsync(new() { Isbn = Isbn });
        Assert.True(result.Success);
        Assert.Equal(1, fallback.Calls);
    }

    [Fact]
    public async Task CancellationStopsBeforeFallback()
    {
        using var source = new CancellationTokenSource();
        var reader = new Reader(_ => { source.Cancel(); return new(ProductUrl, 200, Product(Isbn, ImageUrl)); });
        var fallback = new Fallback();
        var service = new FetchBookCoverService([fallback, Create(reader, new ImageHandler())], NullLogger<FetchBookCoverService>.Instance);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ExecuteAsync(new() { Isbn = Isbn }, source.Token));
        Assert.Equal(0, fallback.Calls);
    }

    private static WookCoverFetcher Create(Reader reader, ImageHandler handler) =>
        new(reader, handler, Microsoft.Extensions.Options.Options.Create(new FetcherOptions()));

    private static string Product(string isbn, string image) => $"""
        <meta property="og:image" content="{image}">
        <table><caption><p class="font-medium">Anjos e Demónios</p></caption>
        <tr><td>ISBN:</td><td>{isbn}</td></tr></table>
        """;

    private sealed class Reader(Func<Uri, WookPage> read) : IWookPageReader
    {
        public int Calls { get; private set; }
        public Task<WookPage> ReadAsync(Uri address, CancellationToken token) { Calls++; return Task.FromResult(read(address)); }
    }

    private sealed class ImageHandler : IWookCoverReader
    {
        public int Calls { get; private set; }
        public bool Fail { get; init; }
        public Task<FetchedCover> ReadCoverAsync(Uri productAddress, string expectedIsbn, CancellationToken cancellationToken)
        {
            Calls++;
            if (Fail) throw new HttpRequestException("Unavailable");
            return Task.FromResult(new FetchedCover { Success = true, Url = ImageUrl, MimeType = "image/jpeg", Image = [1, 2, 3] });
        }
    }

    private sealed class Fallback : ICoverFetcher, IFetcherPriority
    {
        public int Priority => 10;
        public int Calls { get; private set; }
        public Task<FetchedCover> FetchCoverAsync(string isbn, CancellationToken cancellationToken)
        { Calls++; return Task.FromResult(new FetchedCover { Success = true }); }
    }
}
