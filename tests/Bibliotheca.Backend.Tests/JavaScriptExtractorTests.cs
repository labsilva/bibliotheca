using System.Net;
using Bibliotheca.Backend.Extractors;
using Bibliotheca.Backend.Fetchers;
using Bibliotheca.Backend.Options;
using Bibliotheca.Backend.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;

namespace Bibliotheca.Backend.Tests;

public class JavaScriptExtractorTests
{
    private const string Isbn = "9782047330081";
    private const string Origin = "https://www.bertrand.pt";
    private const string Product = Origin + "/livro/example/1";

    [Theory]
    [InlineData("""{"isbn":"9789722536486","title":"Wrong edition"}""")]
    [InlineData("""{"isbn":"9782047330081","title":"Example","source":"https://other.example/"}""")]
    [InlineData("""{"isbn":"9782047330081","title":" "}""")]
    public async Task InvalidScriptResultKeepsMetadataFallback(string result)
    {
        var invalid = new ResultFetcher(result);
        var fallback = new ResultFetcher("""{"isbn":"9782047330081","title":"Fallback"}""");
        var service = new FetchBookService([invalid, fallback], NullLogger<FetchBookService>.Instance);

        var book = await service.ExecuteAsync(new() { Isbn = Isbn, FailOnProviderErrors = true });
        Assert.Equal("Fallback", book.Title);
    }

    [Fact]
    public async Task ProhibitedRedirectIsNeverSent()
    {
        using var handler = new FixtureHandler((string _) => new(HttpStatusCode.Redirect)
        {
            Headers = { Location = new Uri("https://other.example/forbidden") }
        });
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            ExtractorTransport.SendAsync(client, new(Product), [Origin], 4_096, HttpMethod.Get, null, null, default));
        Assert.Equal([Product], handler.Requests);
    }

    [Fact]
    public async Task PendingPromiseHonorsDeadline()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ExtractorRuntime.EvaluateAsync(
            "async function extract() { await new Promise(() => {}); }", "metadata", Isbn,
            (_, _, _) => throw new InvalidOperationException(), deadline.Token));
    }

    [Fact]
    public async Task JintMemoryLimitStillAppliesAfterAnAsynchronousHostCall()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<Jint.Runtime.MemoryLimitExceededException>(() => ExtractorRuntime.EvaluateAsync(
            """
            async function extract(host) {
                await host.goto("https://www.bertrand.pt/");
                const value = "x".repeat(20 * 1024 * 1024);
                return value.length;
            }
            """, "metadata", Isbn, async (_, _, _) =>
            {
                await Task.Yield();
                return "null";
            }, deadline.Token));
    }

    [Fact]
    public void RestartReadsUpdatedScriptAndRequiresExplicitApproval()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            var provider = root.CreateSubdirectory("bertrand");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "extractor-fixture", "manifest.json"),
                Path.Combine(provider.FullName, "manifest.json"));
            var script = Path.Combine(provider.FullName, "extract.js");
            var settings = new Dictionary<string, string?>
            {
                ["Directory"] = root.FullName,
                ["Providers:bertrand:Enabled"] = "true",
                ["Providers:bertrand:NavigationOrigins:0"] = Origin,
                ["Providers:bertrand:ResourceOrigins:0"] = Origin,
                ["Providers:bertrand:ResourceOrigins:1"] = "https://img.bertrand.pt"
            };
            File.WriteAllText(script, "first");
            var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            var first = Assert.Single(ExtractorDefinition.Load(config, root.FullName));
            File.WriteAllText(script, "second");
            var second = Assert.Single(ExtractorDefinition.Load(config, root.FullName));
            Assert.Equal("first", first.Script);
            Assert.Equal("second", second.Script);
            config["Providers:bertrand:ResourceOrigins:0"] = "https://other.example";
            Assert.Throws<InvalidDataException>(() => ExtractorDefinition.Load(config, root.FullName));
        }
        finally { root.Delete(recursive: true); }
    }

    [ExtractorBrowserFact]
    public async Task BertrandPilotMatchesExistingCatalogueOnSharedFixtures()
    {
        const string search = Origin + "/pesquisa/" + Isbn;
        const string wrongProduct = Origin + "/livro/wrong/2";
        const string coverUrl = Origin + "/cover.png";
        var coverBytes = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        var pages = new Dictionary<string, string>
        {
            [search] = """
                <a href="/livro/wrong/2">Wrong edition</a>
                <a href="/livro/example/1">Matching edition</a>
                <img src="https://other.example/no">
                """,
            [wrongProduct] = """
                <script type="application/ld+json">{"isbn":"9789722536486","name":"Wrong"}</script>
                """,
            [Product] = """
                <script type="application/ld+json">{</script>
                <script type="application/ld+json">
                    {"@graph":[{"isbn":"9789722536486","name":"Wrong edition"}, {
                        "isbn":"9782047330081","name":"Example",
                        "author":[{"name":"Jane Doe","url":"/autor/jane-doe/12"},"jane doe","John Doe"],
                        "publisher":{"name":"Publisher"},"datePublished":"2024-05-03",
                        "description":"Synopsis","inLanguage":"pt","numberOfPages":224,
                        "image":{"url":"/cover.png"}
                    }]}
                </script>
                """
        };
        var legacyReader = new BertrandFixtureReader(pages, coverBytes);
        var legacyLookup = new BertrandCatalogueLookup(legacyReader, NullLogger<BertrandCatalogueLookup>.Instance);
        var expectedBook = await new BertrandBookFetcher(legacyLookup).FetchBookAsync(Isbn, default);
        var expectedCover = await new BertrandCoverFetcher(legacyLookup, legacyReader).FetchCoverAsync(Isbn, default);

        var directory = Path.Combine(AppContext.BaseDirectory, "extractor-fixture");
        var script = await File.ReadAllTextAsync(Path.Combine(directory, "extract.js"));
        var definition = new ExtractorDefinition(new("bertrand", 1, "extract.js", ["metadata", "cover"],
            [Origin], [Origin]), script, 6);
        var requests = new List<string>();
        var redirectCover = false;
        var runtime = new ExtractorRuntime(Microsoft.Extensions.Options.Options.Create(new PlaywrightOptions()), new FixtureClientFactory(request =>
        {
            var url = request.RequestUri!.AbsoluteUri;
            requests.Add(url);
            if (pages.TryGetValue(url, out var html))
            {
                var document = Html(html);
                if (url == Product)
                    document.Headers.TryAddWithoutValidation("Set-Cookie", "bertrand-cover=fixture; Secure; HttpOnly; Path=/");
                return document;
            }
            if (url != coverUrl) throw new InvalidOperationException("Unexpected fixture request");
            if (!request.Headers.TryGetValues("Cookie", out var cookies) || !cookies.Contains("bertrand-cover=fixture"))
                return new(HttpStatusCode.Forbidden);
            if (redirectCover)
                return new(HttpStatusCode.Redirect) { Headers = { Location = new Uri("/redirected.png", UriKind.Relative) } };
            return new(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(coverBytes) { Headers = { ContentType = new("image/png") } }
            };
        }));
        var fetcher = new JavaScriptExtractorFetcher(definition, runtime);

        var book = await fetcher.FetchBookAsync(Isbn, default);
        Assert.True(expectedBook.Success);
        Assert.True(book.Success);
        Assert.Equal(expectedBook.Isbn, book.Isbn);
        Assert.Equal(expectedBook.Title, book.Title);
        Assert.Equal(expectedBook.Url, book.Url);
        Assert.Equal(expectedBook.Authors, book.Authors);
        Assert.Equal(expectedBook.HasStructuredAuthors, book.HasStructuredAuthors);
        Assert.Equal(expectedBook.PublishedBy, book.PublishedBy);
        Assert.Equal(expectedBook.PublishedOn, book.PublishedOn);
        Assert.Equal(expectedBook.Metadata.OrderBy(pair => pair.Key), book.Metadata.OrderBy(pair => pair.Key));

        var cover = await fetcher.FetchCoverAsync(Isbn, default);
        Assert.True(expectedCover.Success);
        Assert.True(cover.Success);
        Assert.Equal(expectedCover.Url, cover.Url);
        Assert.Equal(expectedCover.MimeType, cover.MimeType);
        Assert.Equal(expectedCover.Image, cover.Image);
        Assert.Equal([search, wrongProduct, Product, search, wrongProduct, Product, coverUrl], requests);

        requests.Clear();
        redirectCover = true;
        Assert.False((await fetcher.FetchCoverAsync(Isbn, default)).Success);
        Assert.Equal([search, wrongProduct, Product, coverUrl], requests);
    }

    [ExtractorBrowserFact]
    public async Task BiographyPilotsRequireAConfirmedEditionAndUnambiguousAuthor()
    {
        foreach (var id in new[] { "wook", "bertrand", "goodreads" })
        {
            var origin = "https://www." + id + (id == "goodreads" ? ".com" : ".pt");
            var portuguese = id != "goodreads";
            var product = origin + (portuguese ? "/livro/example/1" : "/book/show/1.Example");
            var profilePath = portuguese ? "/autor/jane-doe/12" : "/author/show/12.Jane_Doe";
            var otherPath = portuguese ? "/autor/jane-doe/13" : "/author/show/13.Jane_Doe";
            var profile = origin + profilePath;
            var mode = "match";
            var requests = new System.Collections.Concurrent.ConcurrentQueue<string>();
            var runtime = new ExtractorRuntime(Microsoft.Extensions.Options.Options.Create(new PlaywrightOptions()),
                new FixtureClientFactory(request =>
                {
                    var url = request.RequestUri!.AbsoluteUri;
                    requests.Enqueue(url);
                    if (request.RequestUri.AbsolutePath is "/pesquisa" or "/search" ||
                        request.RequestUri.AbsolutePath.StartsWith("/pesquisa/", StringComparison.Ordinal))
                        return new(HttpStatusCode.Redirect) { Headers = { Location = new Uri(product) } };
                    if (url == profile)
                    {
                        if (mode == "redirect")
                            return new(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://blocked.example/author") } };
                        var profileName = mode == "different-name" ? "Different Person" : "Jane Doe";
                        return id switch
                        {
                            "wook" => Html($$"""
                                <main itemtype="https://schema.org/Person">
                                  <h1 itemprop="name">{{profileName}}</h1>
                                  <div itemprop="description"><p class="bio-text">A biography.</p></div>
                                </main>
                                """),
                            "bertrand" => Html($$"""
                                <script type="application/ld+json">
                                  {"@type":"Person","name":"{{profileName}}","description":"A <b>biography</b>."}
                                </script>
                                """),
                            _ => Html($$"""
                                <h1 class="authorName">{{profileName}}</h1><div class="aboutAuthorInfo">A biography.</div>
                                """)
                        };
                    }
                    if (url != product) throw new InvalidOperationException("Unexpected author fixture request.");
                    var isbn = mode == "wrong-isbn" ? "9789895870714" : Isbn;
                    var ambiguous = mode == "ambiguous";
                    var extraCredit = ambiguous
                        ? "," + System.Text.Json.JsonSerializer.Serialize(new { name = "Jane Doe", url = otherPath })
                        : "";
                    return id switch
                    {
                        "wook" => Html($$"""
                            <table><caption><p class="authors">
                              <a href="#author-12">Jane Doe</a>
                              {{(ambiguous ? "<a href='#author-13'>Jane Doe</a>" : "")}}
                            </p></caption><tr><td>ISBN:</td><td>{{isbn}}</td></tr></table>
                            <a href="{{profilePath}}">Profile</a><a href="{{otherPath}}">Other profile</a>
                            """),
                        "bertrand" => Html($$"""
                            <script type="application/ld+json">{
                              "isbn":"{{isbn}}","name":"Example","author":[{"name":"Jane Doe","url":"{{profilePath}}"}
                              {{extraCredit}}]
                            }</script>
                            """),
                        _ => Html($$"""
                            <div class="ContributorLinksList">
                              <a class="ContributorLink" href="{{profilePath}}"><span class="ContributorLink__name">Jane Doe</span></a>
                              {{(ambiguous ? "<a class='ContributorLink' href='" + otherPath + "'><span class='ContributorLink__name'>Jane Doe</span></a>" : "")}}
                            </div>
                            <button aria-label="Book details and editions">Details</button>
                            <script>
                            document.querySelector('button').onclick = () => document.body.insertAdjacentHTML(
                              'beforeend', '<div class="EditionDetails"><dl class="DescListItem"><dt>ISBN</dt><dd>{{isbn}}</dd></dl></div>');
                            </script>
                            """)
                    };
                }));
            var folder = id == "bertrand" ? Path.Combine(AppContext.BaseDirectory, "extractor-fixture") :
                Path.Combine(AppContext.BaseDirectory, "provider-fixtures", id);
            var script = await File.ReadAllTextAsync(Path.Combine(folder, "extract.js"));
            var definition = new ExtractorDefinition(new(id, 1, "extract.js", ["authorBiography"], [origin], [origin]), script, 6);
            var lookup = new JavaScriptAuthorBiographyLookup(definition, runtime);
            AuthorBiographyEdition[] editions = [new(Isbn, id == "wook" ? "livro/example/1" : null)];

            var biography = await lookup.FindAsync("Jane Doe", editions, default);
            Assert.NotNull(biography);
            Assert.Equal("A biography.", biography.Biography);
            Assert.Equal(profile, biography.SourceUrl);
            if (id == "wook") Assert.Equal([product, profile], requests.ToArray());

            requests.Clear();
            mode = "ambiguous";
            Assert.True((await lookup.FindAsync("Jane Doe", editions, default))?.Ambiguous, id);
            Assert.DoesNotContain(profile, requests);

            requests.Clear();
            mode = "wrong-isbn";
            Assert.Null(await lookup.FindAsync("Jane Doe", editions, default));
            Assert.DoesNotContain(profile, requests);

            requests.Clear();
            mode = "different-name";
            Assert.Null(await lookup.FindAsync("Jane Doe", editions, default));
            Assert.Contains(profile, requests);

            requests.Clear();
            mode = "redirect";
            await Assert.ThrowsAsync<Jint.Runtime.PromiseRejectedException>(() => lookup.FindAsync("Jane Doe", editions, default));
            Assert.DoesNotContain(requests, url => url.Contains("blocked.example", StringComparison.Ordinal));
        }
    }

    [ExtractorBrowserFact]
    public async Task CoverPilotsOnlyDownloadImagesForTheRequestedEdition()
    {
        const string isbn = "9780140328721";
        const string amazon = "https://www.amazon.com";
        const string amazonCdn = "https://m.media-amazon.com";
        const string isbndb = "https://isbndb.com";
        const string isbndbCdn = "https://images.isbndb.com";
        var options = Microsoft.Extensions.Options.Options.Create(new PlaywrightOptions());
        var bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jR1sAAAAASUVORK5CYII=");
        foreach (var (id, origin, cdn, pagePath) in new[]
        {
            ("amazon", amazon, amazonCdn, "/s?k=" + isbn),
            ("isbndb", isbndb, isbndbCdn, "/book/" + isbn)
        })
        {
            var page = origin + pagePath;
            var image = cdn + "/cover.png";
            var matches = true;
            var requests = new List<string>();
            var runtime = new ExtractorRuntime(options, new FixtureClientFactory(request =>
            {
                var url = request.RequestUri!.AbsoluteUri;
                requests.Add(url);
                if (url == image)
                    return new(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(bytes) { Headers = { ContentType = new("image/png") } }
                    };
                if (url != page) throw new InvalidOperationException("Unexpected cover pilot request.");
                return id == "amazon"
                    ? Html($$"""
                        <div data-component-type="s-search-result" data-asin="0000000000">
                          <h2>Wrong edition</h2><img class="s-image" src="https://blocked.example/wrong.png">
                        </div>
                        <div data-component-type="s-search-result" data-asin="{{(matches ? "0140328726" : "1111111111")}}">
                          <h2>Matilda</h2><img class="s-image" src="{{image}}">
                        </div>
                        """)
                    : Html($$"""
                        <h1>Matilda</h1><span itemprop="isbn">{{(matches ? isbn : "9789895870714")}}</span>
                        <div class="artwork"><object data="{{image}}"></object></div>
                        """);
            }));
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Directory"] = Path.Combine(AppContext.BaseDirectory, "provider-fixtures"),
                [$"Providers:{id}:Enabled"] = "true",
                [$"Providers:{id}:NavigationOrigins:0"] = origin,
                [$"Providers:{id}:ResourceOrigins:0"] = cdn,
                [$"Providers:{id}:ResourceOrigins:1"] = id == "amazon" ? "https://images-na.ssl-images-amazon.com" : origin
            }).Build();
            var definition = Assert.Single(ExtractorDefinition.Load(configuration, AppContext.BaseDirectory));
            Assert.Equal(id == "amazon" ? 2 : 100, definition.Priority);
            var fetcher = new JavaScriptExtractorFetcher(definition, runtime);
            var cover = await fetcher.FetchCoverAsync(isbn, default);
            Assert.True(cover.Success, id);
            Assert.Equal(image, cover.Url);
            Assert.Equal(bytes, cover.Image);

            matches = false;
            requests.Clear();
            Assert.False((await fetcher.FetchCoverAsync(isbn, default)).Success);
            // HTML can load the image before the extractor verifies the ISBN. The
            // explicit cover navigation is a separate request and must not occur.
            Assert.InRange(requests.Count(url => url == image), 0, 1);
        }
    }

    [ExtractorBrowserFact]
    public async Task GoodreadsPilotMatchesExistingParserAfterOpeningEditionDetails()
    {
        const string origin = "https://www.goodreads.com";
        const string search = origin + "/search?query=" + Isbn;
        const string product = origin + "/book/show/123.Example";
        const string details = """
            <div class="EditionDetails">
              <dl class="DescListItem"><dt>ISBN</dt><dd>9782047330081</dd></dl>
              <dl class="DescListItem"><dt>Format</dt><dd>120 pages</dd></dl>
              <dl class="DescListItem"><dt>Published</dt><dd>January 16, 1988 by Publisher</dd></dl>
            </div>
            """;
        const string description = """
            <div class="BookPageTitleSection__title"><h1>Example</h1></div>
            <div class="ContributorLinksList">
              <a class="ContributorLink"><span class="ContributorLink__name">Jane Doe</span></a>
              <a class="ContributorLink"><span class="ContributorLink__name">Jim Kay</span>
                <span class="ContributorLink__role">(Illustrator)</span></a>
            </div>
            <div class="BookPageMetadataSection__description">Synopsis</div>
            """;
        var expectedHtml = description + details;
        var options = Microsoft.Extensions.Options.Options.Create(new PlaywrightOptions());
        var existing = new GoodReadsBookFetcher(Microsoft.Extensions.Options.Options.Create(new FetcherOptions()),
            options, NullLogger<GoodReadsBookFetcher>.Instance).ParseResponse(product, ref expectedHtml);
        var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "provider-fixtures", "goodreads", "extract.js"));
        var definition = new ExtractorDefinition(new("goodreads", 1, "extract.js", ["metadata"], [origin], [origin]), script, 10);
        var correctEdition = true;
        var requests = new List<string>();
        var runtime = new ExtractorRuntime(options, new FixtureClientFactory(request =>
        {
            var url = request.RequestUri!.AbsoluteUri;
            requests.Add(url);
            return url switch
            {
                search => new(HttpStatusCode.Redirect) { Headers = { Location = new Uri(product) } },
                product => Html(description + """
                    <button aria-label="Book details and editions">Details</button>
                    <script>
                    document.querySelector('button').addEventListener('click', () => {
                      document.body.insertAdjacentHTML('beforeend',
                    """ + System.Text.Json.JsonSerializer.Serialize(correctEdition ? details : details.Replace(Isbn, "9789895870714"))
                    + ");});</script>"),
                _ => throw new InvalidOperationException("Unexpected Goodreads request.")
            };
        }));
        var fetcher = new JavaScriptExtractorFetcher(definition, runtime);
        var actual = await fetcher.FetchBookAsync(Isbn, default);
        Assert.True(actual.Success);
        Assert.Equal(existing.Url, actual.Url);
        Assert.Equal(existing.Isbn, actual.Isbn);
        Assert.Equal(existing.Title, actual.Title);
        Assert.Equal(existing.Authors, actual.Authors);
        Assert.Equal(existing.PublishedBy, actual.PublishedBy);
        Assert.Equal(existing.PublishedOn, actual.PublishedOn);
        Assert.Equal(existing.Metadata[FetchedBook.FetchedBookMetadataSynopsis], actual.Metadata[FetchedBook.FetchedBookMetadataSynopsis]);
        Assert.Equal(existing.Metadata[FetchedBook.FetchedBookMetadataLanguage], actual.Metadata[FetchedBook.FetchedBookMetadataLanguage]);
        Assert.Equal([search, product], requests);

        correctEdition = false;
        Assert.False((await fetcher.FetchBookAsync(Isbn, default)).Success);
    }

    [ExtractorBrowserFact]
    public async Task BertrandPilotReadsCompactJsonLdFromLargePageAndNavigatesToCdnCover()
    {
        const string cdn = "https://img.bertrand.pt";
        const string image = cdn + "/cover.png";
        const string search = Origin + "/pesquisa/" + Isbn;
        var html = $$"""
            <script type="application/ld+json">{"isbn":"{{Isbn}}","name":"Example","image":"{{image}}"}</script>
            <script type="application/json">"{{new string('x', 1_110_000)}}"</script>
            """;
        var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "extractor-fixture", "extract.js"));
        var definition = new ExtractorDefinition(new("bertrand", 1, "extract.js", ["cover"], [Origin], [Origin, cdn]), script, 6);
        var bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jR1sAAAAASUVORK5CYII=");
        var requests = new List<string>();
        var runtime = new ExtractorRuntime(Microsoft.Extensions.Options.Options.Create(new PlaywrightOptions()),
            new FixtureClientFactory(request =>
            {
                var url = request.RequestUri!.AbsoluteUri;
                requests.Add(url);
                return url switch
                {
                    search => new(HttpStatusCode.Redirect) { Headers = { Location = new Uri(Product) } },
                    Product => Html(html),
                    image when request.Headers.Referrer?.AbsoluteUri == Product => new(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(bytes) { Headers = { ContentType = new("image/png") } }
                    },
                    _ => throw new InvalidOperationException("Unexpected Bertrand CDN request.")
                };
            }));
        var cover = await new JavaScriptExtractorFetcher(definition, runtime).FetchCoverAsync(Isbn, default);
        Assert.True(cover.Success);
        Assert.Equal(image, cover.Url);
        Assert.Equal(bytes, cover.Image);
        Assert.Equal([search, Product, image], requests);
    }

    [ExtractorBrowserFact]
    public async Task WookPilotMatchesCatalogueAndNavigatesToCoverWithProductReferer()
    {
        const string isbn = "9780140328721";
        const string origin = "https://www.wook.pt";
        const string imageOrigin = "https://img.wook.pt";
        const string search = origin + "/pesquisa?keyword=" + isbn;
        const string product = origin + "/livro/matilda/123";
        const string wrong = origin + "/livro/wrong/456";
        const string image = imageOrigin + "/covers/matilda.png";
        var coverBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jR1sAAAAASUVORK5CYII=");
        var productHtml = $"""<meta property="og:image" content="{image}">""" + WookMetadataTests.ProductHtml(isbn);
        var expected = WookCatalogue.ReadProduct(productHtml, new Uri(product));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Directory"] = Path.Combine(AppContext.BaseDirectory, "provider-fixtures"),
            ["Providers:wook:Enabled"] = "true",
            ["Providers:wook:NavigationOrigins:0"] = origin,
            ["Providers:wook:ResourceOrigins:0"] = origin,
            ["Providers:wook:ResourceOrigins:1"] = imageOrigin
        }).Build();
        var definition = Assert.Single(ExtractorDefinition.Load(configuration, AppContext.BaseDirectory));
        Assert.Equal(5, definition.Priority);
        Assert.Equal(0, definition.CoverPriority);
        var requests = new List<string>();
        var direct = false;
        var redirectCover = false;
        var factory = new FixtureClientFactory(request =>
        {
            var url = request.RequestUri!.AbsoluteUri;
            requests.Add(url);
            return url switch
            {
                search when direct => new(HttpStatusCode.Redirect) { Headers = { Location = new Uri(product) } },
                search => Html("""
                    <main class="search-results">
                        <a href="/livro/wrong/456">Wrong</a>
                        <a href="/livro/matilda/123">Matilda</a>
                    </main>
                    """),
                wrong => Html(WookMetadataTests.ProductHtml("9789895870714")),
                product => Html(productHtml),
                image when redirectCover => new(HttpStatusCode.Redirect)
                {
                    Headers = { Location = new Uri("/covers/redirected.png", UriKind.Relative) }
                },
                image when request.Headers.Referrer?.AbsoluteUri == product => new(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(coverBytes) { Headers = { ContentType = new("image/png") } }
                },
                image => new(HttpStatusCode.Forbidden),
                _ => throw new InvalidOperationException("Unexpected fixture request")
            };
        });
        var runtime = new ExtractorRuntime(Microsoft.Extensions.Options.Options.Create(new PlaywrightOptions()), factory);
        var fetcher = new JavaScriptExtractorFetcher(definition, runtime);
        var book = await fetcher.FetchBookAsync(isbn, default);
        Assert.True(book.Success);
        Assert.Equal(expected.Isbn, book.Isbn);
        Assert.Equal(expected.Title, book.Title);
        Assert.Equal(expected.Subtitle, book.Subtitle);
        Assert.Equal(expected.Authors, book.Authors);
        Assert.Equal(expected.HasStructuredAuthors, book.HasStructuredAuthors);
        Assert.Equal(expected.Publisher, book.PublishedBy);
        Assert.Equal(expected.PublishedOn, book.PublishedOn);
        Assert.Equal(expected.Collection, book.Metadata[FetchedBook.FetchedBookMetadataCollection]);
        Assert.Equal(expected.Pages, book.Metadata[FetchedBook.FetchedBookMetadataTotalPages]);
        Assert.Equal(expected.Language, book.Metadata[FetchedBook.FetchedBookMetadataLanguage]);
        Assert.Equal(expected.Synopsis, book.Metadata[FetchedBook.FetchedBookMetadataSynopsis]);
        Assert.Equal(expected.Genres, Assert.IsType<string[]>(book.Metadata[FetchedBook.FetchedBookMetadataGenres]));
        Assert.Equal([search, wrong, product], requests);

        requests.Clear();
        direct = true;
        var cover = await fetcher.FetchCoverAsync(isbn, default);
        Assert.True(cover.Success);
        Assert.Equal(expected.CoverUrl, cover.Url);
        Assert.Equal("image/png", cover.MimeType);
        Assert.Equal(coverBytes, cover.Image);
        Assert.Equal([search, product, image], requests);

        requests.Clear();
        redirectCover = true;
        Assert.False((await fetcher.FetchCoverAsync(isbn, default)).Success);
        Assert.Equal([search, product, image], requests);
    }

    [ExtractorBrowserFact]
    public async Task BrowserSubmitsSearchAndBlocksUnapprovedRequestsBeforeContact()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        const string search = Origin + "/search";
        const string query = Origin + "/results?isbn=" + Isbn;
        const string resourceOrigin = "https://assets.example";
        const string redirect = Origin + "/redirect";
        using var handler = new FixtureHandler((string url) => url switch
        {
            search => Html("""
                <form action="/results">
                    <input name="isbn">
                    <button>Search</button>
                </form>
                """),
            query => Html("""
                <iframe src="https://assets.example/not-a-navigation-origin"></iframe>
                <img src="https://blocked.example/image">
                <button id="open">Open</button>
                <script>
                    document.querySelector('#open').onclick = () => location.assign('/livro/example/1');
                </script>
                """),
            Product => Html("""
                <img src="/redirect">
                <a href="https://assets.example/not-a-navigation-origin">Forbidden</a>
                <h1>Example</h1>
                """),
            redirect => new(HttpStatusCode.Redirect)
            {
                Headers = { Location = new Uri("https://blocked.example/redirect-target") }
            },
            _ => throw new InvalidOperationException("Unexpected fixture request")
        });
        using var client = new HttpClient(handler);
        var definition = new ExtractorDefinition(new("fixture", 1, "extract.js", ["metadata"],
            [Origin], [Origin, resourceOrigin]), """
            async function extract(host, operation, isbn) {
                await host.goto("https://www.bertrand.pt/search");
                await host.fill("input", isbn);
                await host.click("button");
                await host.waitForSelector("#open");
                await host.click("#open");
                await host.waitForSelector("h1");
                return { isbn, title: (await host.text("h1"))[0] };
            }
            """, 6);

        var result = await ExtractorRuntime.RunInBrowserAsync(browser, client, definition, "metadata", Isbn, deadline.Token);

        Assert.Equal("Example", result!.ToBook().Title);
        Assert.Equal(Product, result.Source.AbsoluteUri);
        Assert.Equal([search, query, Product, redirect], handler.Requests);
    }

    [ExtractorBrowserFact]
    public async Task WookExtractorCannotNavigateToGoogle()
    {
        const string wook = "https://wook.pt";
        const string search = wook + "/search";
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var handler = new FixtureHandler((string url) => url == search
            ? Html("""<a href="https://google.pt/">Google</a>""")
            : throw new InvalidOperationException("Unexpected fixture request"));
        using var client = new HttpClient(handler);
        var definition = new ExtractorDefinition(new("wook", 1, "extract.js", ["metadata"], [wook], []), """
            async function extract(host) {
                await host.goto("https://wook.pt/search");
                try {
                    await host.goto("https://google.pt/");
                } catch (error) {
                    if (!String(error).includes("approved HTTPS origin")) throw error;
                    return null;
                }
                throw new Error("The Google navigation was accepted.");
            }
            """, 6);

        var result = await ExtractorRuntime.RunInBrowserAsync(browser, client, definition, "metadata", Isbn, deadline.Token);

        Assert.Null(result);
        Assert.Equal([search], handler.Requests);
    }

    [ExtractorBrowserFact]
    public async Task BrowserContextClosesWhenExecutionIsCancelled()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        using var deadline = new CancellationTokenSource();
        var documentRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var signal = new FixtureHandler((string _) =>
        {
            documentRequested.TrySetResult();
            return Html("<p>Fixture</p>");
        });
        using var fixtureClient = new HttpClient(signal);
        var definition = new ExtractorDefinition(new("fixture", 1, "extract.js", ["metadata"], [Origin], []),
            """
            async function extract(host) {
                await host.goto("https://www.bertrand.pt/livro/example/1");
                await new Promise(() => {});
            }
            """, 6);
        var run = ExtractorRuntime.RunInBrowserAsync(browser, fixtureClient, definition, "metadata", Isbn, deadline.Token);
        try
        {
            await documentRequested.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Single(browser.Contexts);
        }
        finally
        {
            await deadline.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        Assert.Empty(browser.Contexts);
    }

    private static HttpResponseMessage Html(string html) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(html, System.Text.Encoding.UTF8, "text/html")
    };

    private sealed class ResultFetcher(string json) : IBookFetcher
    {
        public async Task<FetchedBook> FetchBookAsync(string isbn, CancellationToken token)
        {
            var output = await ExtractorRuntime.EvaluateAsync(
                "async function extract() { return " + json + "; }", "metadata", isbn,
                (_, _, _) => throw new InvalidOperationException(), token);
            return ExtractorResult.Parse(output, isbn, new(Product), null)!.ToBook();
        }
    }

    private sealed class BertrandFixtureReader(IReadOnlyDictionary<string, string> pages, byte[] coverBytes)
        : IBertrandReader
    {
        public Task<BertrandPage> ReadAsync(Uri url, CancellationToken token) =>
            Task.FromResult(new BertrandPage(url.AbsoluteUri, 200, pages[url.AbsoluteUri]));

        public Task<FetchedCover> ReadCoverAsync(Uri product, string isbn, CancellationToken token)
        {
            var image = BertrandCatalogue.Parse(pages[product.AbsoluteUri], product, isbn)?.Image;
            return Task.FromResult(image is null ? new FetchedCover { Success = false } : new FetchedCover
            {
                Success = true, Url = image.AbsoluteUri, MimeType = "image/png", Image = coverBytes
            });
        }
    }

    private sealed class FixtureHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public FixtureHandler(Func<string, HttpResponseMessage> response)
            : this((HttpRequestMessage request) => response(request.RequestUri!.AbsoluteUri)) { }

        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var url = request.RequestUri!.AbsoluteUri;
            Requests.Add(url);
            return Task.FromResult(response(request));
        }
    }

    private sealed class FixtureClientFactory(Func<HttpRequestMessage, HttpResponseMessage> response) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new FixtureHandler(response));
    }

    private sealed class ExtractorBrowserFactAttribute : FactAttribute
    {
        public ExtractorBrowserFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("BIBLIOTHECA_EXTRACTOR_BROWSER_TESTS") != "1")
                Skip = "Requires Playwright Chromium. Set BIBLIOTHECA_EXTRACTOR_BROWSER_TESTS=1.";
        }
    }
}
