using Bibliotheca.Backend.Fetchers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bibliotheca.Backend.Tests;

public class BertrandTests
{
    private const string Isbn = "9782047330081";
    private const string Url = "https://www.bertrand.pt/livro/example/123";
    private const string Product = """
        <script type="application/ld+json">{"@type":"Book","name":"Example","isbn":"9782047330081","author":{"name":"Jane Doe","url":"https://www.bertrand.pt/autor/jane-doe/12"},"image":"https://www.bertrand.pt/capa/1.jpg","publisher":{"name":"Publisher"},"description":"Synopsis"}</script>
        """;

    [Fact]
    public void ParsesOnlyTheMatchingEdition()
    {
        var parsed = BertrandCatalogue.Parse(Product, new Uri(Url), Isbn);
        Assert.NotNull(parsed);
        Assert.Equal("Example", parsed.Book.Title);
        Assert.Equal("Jane Doe", Assert.Single(parsed.Book.Authors));
        Assert.Equal("Publisher", parsed.Book.PublishedBy);
        Assert.NotNull(parsed.Image);
        Assert.Null(BertrandCatalogue.Parse(Product, new Uri(Url), "9789722536486"));
    }

    [Fact]
    public void RejectsUntrustedImagesAndMalformedData()
    {
        var parsed = BertrandCatalogue.Parse(Product.Replace("https://www.bertrand.pt/capa/1.jpg", "https://evil.example/image"), new Uri(Url), Isbn);
        Assert.NotNull(parsed);
        Assert.Null(parsed.Image);
        Assert.Null(BertrandCatalogue.Parse("<script type='application/ld+json'>{</script>", new Uri(Url), Isbn));
    }

    [Fact]
    public async Task SearchesCandidatesAndRejectsWrongEdition()
    {
        var reader = new FakeReader();
        reader.Pages["https://www.bertrand.pt/pesquisa/" + Isbn] = new("https://www.bertrand.pt/pesquisa/" + Isbn, 200,
            "<a href='/livro/wrong/456'>Wrong</a><a href='/livro/example/123'>Right</a><a href='https://evil.example/livro/foo/123'>Bad</a>");
        reader.Pages["https://www.bertrand.pt/livro/wrong/456"] = new("https://www.bertrand.pt/livro/wrong/456", 200, Product.Replace(Isbn, "9789722536486"));
        reader.Pages[Url] = new(Url, 200, Product);
        var lookup = new BertrandCatalogueLookup(reader, NullLogger<BertrandCatalogueLookup>.Instance);
        var result = await lookup.FindAsync(Isbn, CancellationToken.None);
        Assert.Equal(Url, result?.Book.Url);
        Assert.Equal(3, reader.Requests);
    }

    [Fact]
    public async Task AuthorMustMatchEditionAndProfile()
    {
        var reader = new FakeReader();
        reader.Pages["https://www.bertrand.pt/pesquisa/" + Isbn] = new(Url, 200, Product);
        var authorUrl = "https://www.bertrand.pt/autor/jane-doe/12";
        reader.Pages[authorUrl] = new(authorUrl, 200, "<script type='application/ld+json'>{\"@type\":\"Person\",\"name\":\"Jane Doe\",\"description\":\"Biography\",\"image\":\"https://www.bertrand.pt/photo.jpg\"}</script>");
        var lookup = new BertrandAuthorLookup(new BertrandCatalogueLookup(reader, NullLogger<BertrandCatalogueLookup>.Instance), reader);
        Assert.Equal("Biography", (await lookup.FindAsync("Jane Doe", [Isbn], CancellationToken.None))?.Biography);
        Assert.Equal("https://www.bertrand.pt/photo.jpg", (await lookup.FindAsync("Jane Doe", [Isbn], CancellationToken.None))?.PhotoUrl);
        Assert.Null(await lookup.FindAsync("Another Person", [Isbn], CancellationToken.None));
        reader.Pages[authorUrl] = new(authorUrl, 200, "<script type='application/ld+json'>{\"@type\":\"Person\",\"name\":\"Jane Doe\",\"description\":\"Biography\",\"image\":\"https://evil.example/photo.jpg\"}</script>");
        Assert.Null((await lookup.FindAsync("Jane Doe", [Isbn], CancellationToken.None))?.PhotoUrl);
    }

    [Fact]
    public async Task CoverIsFetchedOnlyForConfirmedEdition()
    {
        var reader = new FakeReader();
        reader.Pages["https://www.bertrand.pt/pesquisa/" + Isbn] = new(Url, 200, Product);
        var lookup = new BertrandCatalogueLookup(reader, NullLogger<BertrandCatalogueLookup>.Instance);
        var fetcher = new BertrandCoverFetcher(lookup, reader);
        await fetcher.FetchCoverAsync(Isbn, CancellationToken.None);
        Assert.Equal(1, reader.CoverRequests);
        reader.Pages["https://www.bertrand.pt/pesquisa/" + Isbn] = new(Url, 200, Product.Replace(Isbn, "9789722536486"));
        Assert.False((await fetcher.FetchCoverAsync(Isbn, CancellationToken.None)).Success);
        Assert.Equal(1, reader.CoverRequests);
    }

    [Fact]
    public async Task NavigationFailureAndCancellationPropagate()
    {
        var reader = new FakeReader();
        var lookup = new BertrandCatalogueLookup(reader, NullLogger<BertrandCatalogueLookup>.Instance);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => lookup.FindAsync(Isbn, CancellationToken.None));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lookup.FindAsync(Isbn, cancelled.Token));
    }

    private sealed class FakeReader : IBertrandReader
    {
        public Dictionary<string, BertrandPage> Pages { get; } = new();
        public int Requests { get; private set; }
        public int CoverRequests { get; private set; }
        public Task<BertrandPage> ReadAsync(Uri url, CancellationToken token) { token.ThrowIfCancellationRequested(); Requests++; return Task.FromResult(Pages[url.AbsoluteUri]); }
        public Task<FetchedCover> ReadCoverAsync(Uri product, string isbn, CancellationToken token) { CoverRequests++; return Task.FromResult(new FetchedCover { Success = false }); }
    }
}
