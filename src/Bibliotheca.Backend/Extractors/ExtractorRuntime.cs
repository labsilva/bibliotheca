using System.Text;
using System.Text.Json;
using Bibliotheca.Backend.Fetchers;
using Bibliotheca.Backend.Options;
using Jint;
using Jint.Runtime;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace Bibliotheca.Backend.Extractors;

internal sealed class ExtractorRuntime(IOptions<PlaywrightOptions> options, IHttpClientFactory clients)
    : PlaywrightFetcher(options)
{
    public const string ClientName = "JavaScriptExtractors";
    private static readonly TimeSpan ExecutionLimit = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CleanupLimit = TimeSpan.FromSeconds(2);

    public async Task<ExtractorResult?> RunAsync(ExtractorDefinition definition, string operation, string isbn,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(ExecutionLimit);
        using var hardStop = new CancellationTokenSource();
        using var registration = deadline.Token.Register(() => hardStop.CancelAfter(CleanupLimit));
        using var client = clients.CreateClient(ClientName);
        try
        {
            return await WithBrowserAsync(browser =>
                RunInBrowserAsync(browser, client, definition, operation, isbn, deadline.Token),
                hardStop.Token, waitForCleanup: true);
        }
        catch (Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (deadline.IsCancellationRequested)
                throw new TimeoutException("JavaScript extractor exceeded its deadline.");
            throw;
        }
    }

    internal static async Task<ExtractorResult?> RunInBrowserAsync(IBrowser browser, HttpClient client,
        ExtractorDefinition definition, string operation, string isbn, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var context = await browser.NewContextAsync(new()
        {
            JavaScriptEnabled = false,
            ServiceWorkers = ServiceWorkerPolicy.Block,
            Offline = true,
            AcceptDownloads = false
        });
        using var sessionStop = CancellationTokenSource.CreateLinkedTokenSource(token);
        ExtractorSession? session = null;
        try
        {
            token.ThrowIfCancellationRequested();
            var page = await context.NewPageAsync();
            session = new ExtractorSession(context, page, client, definition.Manifest, operation, sessionStop.Token);
            await session.ConfigureBrowserAsync();
            var json = await EvaluateAsync(definition.Script, operation, isbn, session.CallAsync, token);
            return ExtractorResult.Parse(json, isbn, session.Source, session.Cover);
        }
        finally
        {
            await sessionStop.CancelAsync();
            // Closing the context interrupts any outstanding Playwright call before we drain it.
            try { await context.CloseAsync(); }
            finally
            {
                if (session is not null) await session.DrainAsync();
            }
        }
    }

    internal static async Task<string> EvaluateAsync(string script, string operation, string isbn,
        Func<string, string, string, Task<string>> call, CancellationToken token)
    {
        var engine = new Engine(options =>
        {
            options.ExperimentalFeatures = ExperimentalFeature.TaskInterop;
            options.MaxStatements(100_000).LimitMemory(32 * 1024 * 1024).LimitRecursion(64).CancellationToken(token);
        });
        engine.SetValue("hostCall", call);
        engine.SetValue("operation", operation);
        engine.SetValue("isbn", isbn);
        Jint.Native.JsValue result;
        try
        {
            result = await engine.EvaluateAsync("""
                (async (call, operation, isbn, stringify, parse) => {
                    delete globalThis.hostCall;
                    const host = Object.freeze({
                        goto: async url => parse(await call("goto", url, "")),
                        text: async selector => parse(await call("text", selector, "")),
                        attribute: async (selector, name) => parse(await call("attribute", selector, name)),
                        downloadImage: async url => parse(await call("downloadImage", url, ""))
                    });
                """ + "\n" + script + "\n" + """
                    return stringify(await extract(host, operation, isbn));
                })(hostCall, operation, isbn, JSON.stringify, JSON.parse)
                """, cancellationToken: token);
        }
        catch (ExecutionCanceledException) when (token.IsCancellationRequested)
        {
            throw new OperationCanceledException(token);
        }
        if (!result.IsString() || result.AsString().Length > 32_768)
            throw new InvalidDataException("Extractor output is missing or exceeds its size limit.");
        return result.AsString();
    }
}

internal sealed class ExtractorSession(IBrowserContext context, IPage page, HttpClient client,
    ExtractorManifest manifest, string operation, CancellationToken token)
{
    private const int MaxDocumentBytes = 2 * 1024 * 1024;
    private const string DocumentUrl = "https://extractor.invalid/document";
    private Task<string>? pending;
    private int calls;
    private string? html;

    public Uri? Source { get; private set; }
    public FetchedCover? Cover { get; private set; }

    public Task ConfigureBrowserAsync() => context.RouteAsync("**/*", route =>
        html is not null && route.Request.Url == DocumentUrl && route.Request.IsNavigationRequest &&
        route.Request.Frame == page.MainFrame
            ? route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "text/html; charset=utf-8",
                Body = html,
                Headers = new Dictionary<string, string>
                {
                    ["Content-Security-Policy"] = "sandbox; default-src 'none'; base-uri 'none'; form-action 'none'",
                    ["X-DNS-Prefetch-Control"] = "off"
                }
            })
            : route.AbortAsync());

    public Task<string> CallAsync(string method, string input, string argument)
    {
        token.ThrowIfCancellationRequested();
        if (pending is { IsCompleted: false } || ++calls > 64 || input.Length > 2048 || argument.Length > 64)
            throw new InvalidDataException("Invalid or overlapping extractor host call.");
        return pending = DispatchAsync(method, input, argument);
    }

    public async Task DrainAsync()
    {
        if (pending is null) return;
        try { await pending; }
        catch (Exception) when (token.IsCancellationRequested) { }
    }

    private async Task<string> DispatchAsync(string method, string input, string argument)
    {
        switch (method)
        {
            case "goto":
                var destination = Resolve(input);
                Source = null;
                Cover = null;
                var document = await ExtractorTransport.GetAsync(client, destination, manifest.NavigationOrigins,
                    MaxDocumentBytes, token);
                if (document is null) return "null";
                if (document.MediaType?.ToLowerInvariant() is not ("text/html" or "application/xhtml+xml"))
                    throw new InvalidDataException("Extractor navigation requires HTML.");

                html = Encoding.UTF8.GetString(document.Body);
                await page.GotoAsync(DocumentUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
                Source = document.Url;
                return JsonSerializer.Serialize(Source.AbsoluteUri);

            case "text" when Source is not null:
            case "attribute" when Source is not null:
                var values = await page.Locator(input).EvaluateAllAsync<string[]>(
                    "(elements, name) => elements.slice(0, 64).map(e => (name ? e.getAttribute(name) : e.textContent)?.slice(0, 32768) ?? '')",
                    method == "attribute" ? argument : "");
                return JsonSerializer.Serialize(values);

            case "downloadImage" when operation == "cover" && Source is not null:
                Cover = null;
                var image = await ExtractorTransport.GetAsync(client, Resolve(input), manifest.ResourceOrigins,
                    CoverDownloader.MaxCoverBytes, token);
                var type = image?.MediaType?.ToLowerInvariant();
                if (image is null || image.Body.Length == 0 ||
                    type is not ("image/jpeg" or "image/png" or "image/webp" or "image/gif" or "image/avif"))
                    return "false";
                Cover = new() { Success = true, Url = image.Url.AbsoluteUri, MimeType = type, Image = image.Body };
                return "true";

            default:
                throw new InvalidDataException("Extractor host operation is unavailable.");
        }
    }

    private Uri Resolve(string input)
    {
        if (Uri.TryCreate(input, UriKind.Absolute, out var absolute) && absolute.Scheme != Uri.UriSchemeFile)
            return absolute;
        if (Source is not null && Uri.TryCreate(Source, input, out var relative))
            return relative;
        throw new InvalidDataException("Invalid extractor URL.");
    }
}
