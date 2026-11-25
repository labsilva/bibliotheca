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
        using var handler = new FixtureHandler(_ => new(HttpStatusCode.Redirect)
        {
            Headers = { Location = new Uri("https://other.example/forbidden") }
        });
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            ExtractorTransport.GetAsync(client, new(Product), [Origin], 4_096, default));
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
                ["Providers:bertrand:ResourceOrigins:0"] = Origin
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
    public async Task BertrandPilotUsesLocalFixturesForMetadataAndCover()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "extractor-fixture");
        var script = await File.ReadAllTextAsync(Path.Combine(directory, "extract.js"));
        var definition = new ExtractorDefinition(new("bertrand", 1, "extract.js", ["metadata", "cover"],
            [Origin], [Origin]), script, 6);
        var requests = new List<string>();
        var runtime = new ExtractorRuntime(Microsoft.Extensions.Options.Options.Create(new PlaywrightOptions()), new FixtureClientFactory(url =>
        {
            requests.Add(url);
            return url switch
            {
                Origin + "/pesquisa/" + Isbn => Html("<a href='/livro/example/1'>Example</a><img src='https://other.example/no'>"),
                Product => Html("""
                    <script type="application/ld+json">
                        {"@graph":[{"isbn":"9789722536486","name":"Wrong edition"},
                          {"isbn":"9782047330081","name":"Example","author":{"name":"Jane Doe"},"image":"/cover.png"}]}
                    </script>
                    """),
                Origin + "/cover.png" => new(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([137, 80, 78, 71, 13, 10, 26, 10])
                    {
                        Headers = { ContentType = new("image/png") }
                    }
                },
                _ => throw new InvalidOperationException("Unexpected fixture request")
            };
        }));
        var fetcher = new JavaScriptExtractorFetcher(definition, runtime);

        var book = await fetcher.FetchBookAsync(Isbn, default);
        Assert.Equal("Example", book.Title);
        Assert.Equal(["Jane Doe"], book.Authors);
        var cover = await fetcher.FetchCoverAsync(Isbn, default);
        Assert.True(cover.Success);
        Assert.Equal("image/png", cover.MimeType);
        Assert.Equal(Origin + "/cover.png", requests.Last());
        Assert.Equal(5, requests.Count);
    }

    [ExtractorBrowserFact]
    public async Task BrowserContextClosesWhenExecutionIsCancelled()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        using var deadline = new CancellationTokenSource();
        var documentRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var signal = new FixtureHandler(_ =>
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

    private sealed class FixtureHandler(Func<string, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var url = request.RequestUri!.AbsoluteUri;
            Requests.Add(url);
            return Task.FromResult(response(url));
        }
    }

    private sealed class FixtureClientFactory(Func<string, HttpResponseMessage> response) : IHttpClientFactory
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
